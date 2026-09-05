using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace BgIptvPlayer.Native;

public partial class MainWindow : Window
{
    private static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly string LegacyPlaylistSettingPath = Path.Combine(SettingsDirectory, "playlist.txt");
    private static readonly string PlaylistCacheDirectory = Path.Combine(SettingsDirectory, "playlists");
    private static readonly string LibraryStateFilePath = Path.Combine(SettingsDirectory, "library.json");
    private const string DefaultPlaylistName = "Test Link";
    private const string DefaultPlaylistUrl = "https://raw.githubusercontent.com/Free-TV/IPTV/refs/heads/master/playlists/playlist_turkey.m3u8";
    private static readonly HttpClient PlaylistClient = CreatePlaylistClient();
    private static readonly HttpClient UpdateClient = CreateUpdateClient();
    private static readonly HttpClient UpdateDownloadClient = CreateUpdateDownloadClient();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _fullscreenControlsTimer;
    private readonly DispatcherTimer _fullscreenControlsRevealTimer;
    private readonly DispatcherTimer _playbackProgressTimer;
    private Media? _media;
    private List<Channel> _channels = [];
    private List<PlaylistEntry> _playlists = [];
    private EpgSnapshot _epg = new();
    private LibraryState _libraryState = new();
    private string _selectedGroup = "";
    private LibraryGroupKind _selectedGroupKind = LibraryGroupKind.Regular;
    private ContentKind _selectedContent = ContentKind.Live;
    private ContentKind _playingContent = ContentKind.Live;
    private Channel? _playingChannel;
    private Channel? _lastPlayingChannel;
    private DateTime _selectedEpgDate = DateTime.Today;
    private SeriesBrowserLevel _seriesBrowserLevel = SeriesBrowserLevel.Shows;
    private string? _selectedSeriesTitle;
    private int? _selectedSeriesSeason;
    private bool _isPlayerFullscreen;
    private bool _epgPanelWasVisibleBeforeFullscreen;
    private bool _fullscreenControlsVisible;
    private bool _fullscreenControlsRevealArmed = true;
    private bool _historyRecordedForCurrentPlayback;
    private bool _suppressGroupSelection;
    private bool _suppressChannelSelection;
    private bool _isSeeking;
    private long? _pendingResumePosition;
    private double _lastAudibleVolume = 80;
    private string? _availableUpdateUrl;
    private string? _updateSetupUrl;
    private string? _updateVersionTag;
    private long _updateSetupSize;
    private bool _updateInProgress;
    private WindowState _previousWindowState = WindowState.Normal;
    private Window? _fullscreenControlsOverlay;
    private Border? _fullscreenControlsOverlaySurface;
    private Grid? _fullscreenTimelinePanel;
    private Slider? _fullscreenTimeline;
    private TextBlock? _fullscreenTimeLabel;
    private TextBlock? _fullscreenNowPlaying;
    private Slider? _fullscreenVolumeSlider;
    private Avalonia.Controls.Shapes.Path? _fullscreenPlayPauseIcon;
    private Avalonia.Controls.Shapes.Path? _fullscreenVolumeWaveIcon;
    private Avalonia.Controls.Shapes.Path? _fullscreenVolumeMutedIcon;
    private Button? _fullscreenPreviousChannelButton;
    private Button? _fullscreenLastChannelButton;
    private Button? _fullscreenNextChannelButton;
    private Button? _fullscreenRewindButton;
    private Button? _fullscreenForwardButton;
    private Window? _playerOverlay;
    private TextBlock? _playerOverlayName;
    private TextBlock? _playerOverlayStatus;
    private TextBlock? _playerOverlayTimeLabel;
    private Slider? _playerOverlayTimeline;
    private Slider? _playerOverlayVolumeSlider;
    private Avalonia.Controls.Shapes.Path? _playerOverlayPlayPauseIcon;
    private Button? _playerOverlayPreviousButton;
    private Button? _playerOverlayLastButton;
    private Button? _playerOverlayNextButton;
    private Button? _playerOverlayRewindButton;
    private Button? _playerOverlayForwardButton;
    private bool _syncingPlayerOverlayVolume;
    private bool _syncingFullscreenVolume;
    private int _loadingDepth;
    private bool _isGlobalSearch;
    private string _settingsSection = "playlists";

    public MainWindow()
    {
        Localization.Initialize();
        InitializeComponent();
        UpdateHomeDashboard();
        Localization.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
        Timeline.AddHandler(PointerPressedEvent, Timeline_PointerPressed, RoutingStrategies.Tunnel, true);
        Timeline.AddHandler(PointerReleasedEvent, Timeline_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        Core.Initialize();
        _libVlc = new LibVLC("--network-caching=1800", "--http-reconnect", "--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.Volume = 80;
        PlayerView.MediaPlayer = _mediaPlayer;
        PlayerView.VideoDoubleClicked += (_, _) => SetPlayerFullscreen(!_isPlayerFullscreen);
        PlayerView.EscapePressed += (_, _) => SetPlayerFullscreen(false);
        PlayerView.VideoMouseMoved += (_, _) => HandleFullscreenPointerActivity();
        PlayerView.LayoutUpdated += (_, _) => UpdatePlayerOverlayBounds();
        PositionChanged += (_, _) => UpdatePlayerOverlayBounds();
        Resized += (_, _) => UpdatePlayerOverlayBounds();
        _fullscreenControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _fullscreenControlsTimer.Tick += (_, _) => HideFullscreenControls();
        _fullscreenControlsRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _fullscreenControlsRevealTimer.Tick += (_, _) =>
        {
            _fullscreenControlsRevealTimer.Stop();
            _fullscreenControlsRevealArmed = true;
        };
        _playbackProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _playbackProgressTimer.Tick += (_, _) => SaveCurrentPlaybackProgress();
        _playbackProgressTimer.Start();
        _mediaPlayer.Opening += (_, _) => SetStatus(L("Yayına bağlanılıyor..."));
        _mediaPlayer.Buffering += (_, e) => SetStatus($"{L("Yükleniyor")} %{e.Cache:0}");
        _mediaPlayer.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            PlaybackStatus.Text = _playingContent == ContentKind.Live && _playingChannel is { } liveChannel
                ? GetNowPlayingStatus(liveChannel)
                : L("Oynatılıyor");
            UpdatePlayerOverlayText();
            UpdatePlayPauseIcons(true);
            if (!_historyRecordedForCurrentPlayback && _playingChannel is { } playingChannel)
            {
                _historyRecordedForCurrentPlayback = true;
                TouchPlaybackHistory(playingChannel);
            }
            TryResumePlayback(_mediaPlayer.Length);
        });
        _mediaPlayer.Paused += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayPauseIcons(false));
        _mediaPlayer.Stopped += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayPauseIcons(false));
        _mediaPlayer.EncounteredError += (_, _) => SetStatus(L("Yayın açılamadı; kaynak çevrimdışı olabilir."));
        _mediaPlayer.TimeChanged += (_, e) => UpdateTimeline(e.Time, _mediaPlayer.Length);
        _mediaPlayer.LengthChanged += (_, e) =>
        {
            UpdateTimeline(_mediaPlayer.Time, e.Length);
            Dispatcher.UIThread.Post(() => TryResumePlayback(e.Length));
        };
        _mediaPlayer.SeekableChanged += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            var canSeek = _playingContent != ContentKind.Live && e.Seekable != 0 && _mediaPlayer.Length > 0;
            Timeline.IsEnabled = canSeek;
            UpdateSeekControls(canSeek);
            if (e.Seekable != 0) TryResumePlayback(_mediaPlayer.Length);
        });
        _mediaPlayer.EndReached += (_, _) => Dispatcher.UIThread.Post(MarkCurrentPlaybackCompleted);
        Closed += (_, _) =>
        {
            SaveCurrentPlaybackProgress();
            _playbackProgressTimer.Stop();
            _fullscreenControlsOverlay?.Close();
            _playerOverlay?.Close();
            _media?.Dispose();
            _mediaPlayer.Dispose();
            _libVlc.Dispose();
        };

        _playlists = LoadPlaylistSettings();
        _libraryState = LoadLibraryState();
        if (_playlists.Count > 0 && !_playlists.Any(p => p.IsActive)) _playlists[0].IsActive = true;
        SavePlaylistSettings();
        RefreshPlaylistSettingsView();
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
        if (argument is not null) AddOrActivatePlaylist(argument);
        var active = _playlists.FirstOrDefault(p => p.IsActive);
        Dispatcher.UIThread.Post(CheckForUpdatesAsync);
        if (argument is not null) Dispatcher.UIThread.Post(async () => await LoadPlaylistAsync(argument));
        else if (active is not null) Dispatcher.UIThread.Post(async () => await LoadPlaylistEntryAsync(active));
        else LoadingOverlay.IsVisible = false;
    }

    private static HttpClient CreateUpdateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BG-IPTV-Player/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // Kurulum dosyası büyük olduğu için sürüm kontrolünden ayrı, uzun zaman aşımlı istemci.
    private static HttpClient CreateUpdateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BG-IPTV-Player/1.0");
        return client;
    }

    private static HttpClient CreatePlaylistClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/x-mpegURL, application/vnd.apple.mpegurl, text/plain, */*");
        return client;
    }

    private enum UpdateCheckResult
    {
        Failed,
        UpToDate,
        Available
    }

    // Sürüm bilgisini okur; hem açılıştaki otomatik denetim hem de ayarlardaki düğme bunu kullanır.
    private async Task<UpdateCheckResult> FetchLatestReleaseAsync()
    {
        try
        {
            using var response = await UpdateClient.GetAsync("https://api.github.com/repos/berkguclukol/bg-iptv-player/releases/latest");
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = json.RootElement.GetProperty("tag_name").GetString();
            var url = json.RootElement.GetProperty("html_url").GetString();
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url)) return UpdateCheckResult.Failed;
            if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', 2)[0], out var latest)) return UpdateCheckResult.Failed;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (latest <= current) return UpdateCheckResult.UpToDate;

            _availableUpdateUrl = url;
            _updateVersionTag = tag;
            ReadUpdateSetupAsset(json.RootElement);
            return UpdateCheckResult.Available;
        }
        catch
        {
            // Güncelleme kontrolü uygulamanın açılışını ve oynatmayı etkilemez.
            return UpdateCheckResult.Failed;
        }
    }

    private async void CheckForUpdatesAsync()
    {
        if (await FetchLatestReleaseAsync() != UpdateCheckResult.Available) return;
        ShowUpdateBanner();
    }

    private void ShowUpdateBanner()
    {
        UpdateTitle.Text = $"BG IPTV Player {_updateVersionTag} {L("hazır")}";
        UpdateStatusText.Text = _updateSetupUrl is null
            ? L("İndirmek için sürüm sayfasını açın.")
            : L("İndirilip kurulur, ardından uygulama yeniden başlar.");
        UpdateNowButton.IsVisible = _updateSetupUrl is not null;
        UpdateBanner.IsVisible = true;
        RefreshUpdateSection();
    }

    // Ayarlar sayfasındaki güncelleme bölümünü mevcut duruma göre yazar.
    private void RefreshUpdateSection()
    {
        if (_updateInProgress) return;
        SettingsVersionText.Text = $"BG IPTV Player {AppVersion}";
        if (_availableUpdateUrl is null)
        {
            SettingsUpdateActions.IsVisible = false;
            SettingsUpdateStatus.Text = L("Uygulama her açılışta güncellemeleri kendiliğinden denetler.");
            return;
        }

        SettingsUpdateStatus.Text = $"BG IPTV Player {_updateVersionTag} {L("hazır")}";
        SettingsUpdateNowButton.IsVisible = _updateSetupUrl is not null;
        SettingsUpdateActions.IsVisible = true;
    }

    private async void CheckUpdatesNow_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateInProgress) return;
        CheckUpdatesButton.IsEnabled = false;
        SettingsUpdateActions.IsVisible = false;
        SettingsUpdateStatus.Text = L("Denetleniyor...");

        var result = await FetchLatestReleaseAsync();
        if (result == UpdateCheckResult.Available) ShowUpdateBanner();
        else
            SettingsUpdateStatus.Text = result == UpdateCheckResult.UpToDate
                ? L("Uygulamanın en güncel sürümünü kullanıyorsunuz.")
                : L("Güncelleme denetlenemedi. İnternet bağlantınızı kontrol edin.");

        CheckUpdatesButton.IsEnabled = true;
    }

    // Güncelleme durumu hem bildirim şeridinde hem ayarlar sayfasında görünür.
    private void SetUpdateStatus(string text)
    {
        UpdateStatusText.Text = text;
        SettingsUpdateStatus.Text = text;
    }

    // Yayındaki kurulum dosyasını bulur; yalnızca projenin kendi GitHub adresini kabul eder.
    private void ReadUpdateSetupAsset(JsonElement release)
    {
        _updateSetupUrl = null;
        _updateSetupSize = 0;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = ReadJsonString(asset, "name");
            if (name is null || !name.EndsWith("-Setup-x64.exe", StringComparison.OrdinalIgnoreCase)) continue;

            var downloadUrl = ReadJsonString(asset, "browser_download_url");
            if (!IsTrustedUpdateUrl(downloadUrl)) continue;

            _updateSetupUrl = downloadUrl;
            _updateSetupSize = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0;
            return;
        }
    }

    private static bool IsTrustedUpdateUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/berkguclukol/bg-iptv-player/releases/download/", StringComparison.OrdinalIgnoreCase);

    private void OpenUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_availableUpdateUrl)) return;
        OpenExternalUrl(_availableUpdateUrl);
    }

    // Kurulum dosyasını indirir, sessiz kurulumu başlatır ve uygulamadan çıkar.
    // Kurulum bittiğinde installer uygulamayı yeniden açar.
    private async void UpdateNow_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateInProgress || string.IsNullOrWhiteSpace(_updateSetupUrl)) return;
        _updateInProgress = true;
        UpdateNowButton.IsEnabled = false;
        SettingsUpdateNowButton.IsEnabled = false;

        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "BgIptvPlayerUpdate");
            Directory.CreateDirectory(directory);
            var setupPath = Path.Combine(directory, $"BG-IPTV-Player-{_updateVersionTag ?? "latest"}-Setup-x64.exe");

            SetUpdateStatus(L("İndiriliyor..."));
            using (var response = await UpdateDownloadClient.GetAsync(_updateSetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? _updateSetupSize;
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(setupPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
                var buffer = new byte[1024 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    received += read;
                    SetUpdateStatus(total > 0
                        ? $"{L("İndiriliyor")} %{received * 100 / total}"
                        : $"{L("İndiriliyor")} {received / 1024d / 1024d:0.0} MB");
                }
            }

            if (_updateSetupSize > 0 && new FileInfo(setupPath).Length != _updateSetupSize)
                throw new InvalidDataException(L("İndirilen dosya eksik."));

            SetUpdateStatus(L("Kurulum başlatılıyor..."));
            Process.Start(new ProcessStartInfo(setupPath)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
            });
            Close();
        }
        catch (Exception ex)
        {
            _updateInProgress = false;
            SetUpdateStatus($"{L("Güncelleme yapılamadı")}: {ex.Message}");
            UpdateNowButton.IsEnabled = true;
            SettingsUpdateNowButton.IsEnabled = true;
        }
    }

    private void DismissUpdate_Click(object? sender, RoutedEventArgs e) => UpdateBanner.IsVisible = false;

    private async void RefreshPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        var active = _playlists.FirstOrDefault(p => p.IsActive);
        if (active is not null) await LoadPlaylistEntryAsync(active, forceRefresh: active.IsRemote);
        else ShowSettings();
    }

    private async void AddPlaylistFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("M3U oynatma listesi seç"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("M3U oynatma listesi") { Patterns = ["*.m3u", "*.m3u8"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        AddOrActivatePlaylist(path);
        await LoadPlaylistAsync(path);
        HideSettings();
    }

    private async void AddPlaylistUrl_Click(object? sender, RoutedEventArgs e)
    {
        var value = PlaylistUrlBox.Text?.Trim() ?? "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            PlaylistUrlBox.Text = "";
            PlaylistUrlBox.Watermark = L("Geçerli bir http veya https adresi girin");
            return;
        }

        var name = PlaylistNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        var entry = AddOrActivatePlaylist(value, name);
        PlaylistNameBox.Text = "";
        PlaylistUrlBox.Text = "";
        await LoadPlaylistEntryAsync(entry, forceRefresh: true);
        HideSettings();
    }

    private async void AddXtreamPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        var server = XtreamServerBox.Text?.Trim() ?? "";
        var username = XtreamUsernameBox.Text?.Trim() ?? "";
        var password = XtreamPasswordBox.Text ?? "";
        if (!TryBuildXtreamUrls(server, username, password, out var playlistUrl, out var epgUrl, out var displayServer, out var baseUrl))
        {
            XtreamServerBox.Text = "";
            XtreamServerBox.Watermark = L("Sunucu, kullanıcı adı ve şifreyi kontrol edin");
            return;
        }

        var name = XtreamNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = displayServer;
        var entry = AddOrActivatePlaylist(playlistUrl, name, epgUrl, PlaylistSourceKind.Xtream, displayServer);
        entry.XtreamServer = baseUrl;
        entry.XtreamUsername = username;
        entry.XtreamPassword = password;
        SavePlaylistSettings();
        XtreamNameBox.Text = "";
        XtreamServerBox.Text = "";
        XtreamUsernameBox.Text = "";
        XtreamPasswordBox.Text = "";
        await LoadPlaylistEntryAsync(entry, forceRefresh: true);
        HideSettings();
    }

    private static bool TryBuildXtreamUrls(
        string server,
        string username,
        string password,
        out string playlistUrl,
        out string epgUrl,
        out string displayServer,
        out string baseUrl)
    {
        playlistUrl = "";
        epgUrl = "";
        displayServer = "";
        baseUrl = "";
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        if (!server.Contains("://", StringComparison.Ordinal)) server = "http://" + server;
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;

        var basePath = uri.AbsolutePath.TrimEnd('/');
        baseUrl = uri.GetLeftPart(UriPartial.Authority) + (basePath == "/" ? "" : basePath);
        var credentials = $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";
        playlistUrl = $"{baseUrl}/get.php?{credentials}&type=m3u_plus&output=ts";
        epgUrl = $"{baseUrl}/xmltv.php?{credentials}";
        displayServer = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return true;
    }

    private async Task LoadPlaylistEntryAsync(PlaylistEntry entry, bool forceRefresh = false)
    {
        BeginLoading($"{entry.Name} {L("hazırlanıyor...")}");
        try
        {
            var loadedFromXtreamApi = false;
            if (entry.IsXtream)
            {
                try
                {
                    await LoadXtreamApiAsync(entry, forceRefresh);
                    loadedFromXtreamApi = true;
                }
                catch
                {
                    SetLoadingStatus(L("Xtream API yanıt vermedi · M3U deneniyor..."));
                }
            }

            if (!loadedFromXtreamApi)
            {
                var sourcePath = await ResolvePlaylistPathAsync(entry, forceRefresh);
                await LoadPlaylistAsync(sourcePath, entry.Name);
            }
            if (!string.IsNullOrWhiteSpace(entry.EpgUrl))
            {
                try
                {
                    SetLoadingStatus(L("EPG bilgileri yükleniyor..."));
                    var epgPath = await ResolveEpgPathAsync(entry, forceRefresh);
                    _epg = await Task.Run(() => ParseXmlTv(epgPath));
                    RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
                    if (_playingChannel is { } playingChannel) UpdateEpgPanel(playingChannel, EpgPanel.IsVisible);
                    SetLoadingStatus($"{_channels.Count:N0} {L("içerik")} · {L("EPG hazır")}");
                }
                catch (Exception ex)
                {
                    _epg = new EpgSnapshot();
                    SetLoadingStatus($"{L("Liste hazır")} · {L("EPG alınamadı")}: {ex.Message}");
                }
            }
            else
            {
                _epg = new EpgSnapshot();
            }
        }
        catch (Exception ex)
        {
            PageTitle.Text = L("Liste yüklenemedi");
            SetLoadingStatus($"{L("Liste açılamadı")}: {ex.Message}");
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task LoadXtreamApiAsync(PlaylistEntry entry, bool forceRefresh)
    {
        if (!TryGetXtreamCredentials(entry, out var credentials))
            throw new InvalidDataException(L("Xtream hesap bilgileri okunamadı."));

        PageTitle.Text = L("Xtream hesabı doğrulanıyor...");
        SetLoadingStatus($"{entry.Name} · {L("Xtream hesabı doğrulanıyor...")}");
        var apiRoot = $"{credentials.Server}/player_api.php?username={Uri.EscapeDataString(credentials.Username)}&password={Uri.EscapeDataString(credentials.Password)}";
        using (var accountJson = JsonDocument.Parse(await PlaylistClient.GetStringAsync(apiRoot)))
        {
            if (!accountJson.RootElement.TryGetProperty("user_info", out var userInfo) ||
                ReadJsonString(userInfo, "auth") != "1")
                throw new UnauthorizedAccessException(L("Xtream hesabı doğrulanamadı."));

            var status = ReadJsonString(userInfo, "status");
            if (status is not null && status.Equals("Active", StringComparison.OrdinalIgnoreCase) == false)
                throw new UnauthorizedAccessException($"Xtream hesap durumu: {status}");
        }

        SetLoadingStatus(L("Xtream kategorileri ve içerikleri alınıyor..."));
        var liveCategoriesTask = PlaylistClient.GetStringAsync(apiRoot + "&action=get_live_categories");
        var liveStreamsTask = PlaylistClient.GetStringAsync(apiRoot + "&action=get_live_streams");
        var vodCategoriesTask = PlaylistClient.GetStringAsync(apiRoot + "&action=get_vod_categories");
        var vodStreamsTask = PlaylistClient.GetStringAsync(apiRoot + "&action=get_vod_streams");
        await Task.WhenAll(liveCategoriesTask, liveStreamsTask, vodCategoriesTask, vodStreamsTask);

        var liveCategories = ParseXtreamCategories(await liveCategoriesTask);
        var vodCategories = ParseXtreamCategories(await vodCategoriesTask);
        var channels = ParseXtreamStreams(await liveStreamsTask, liveCategories, credentials, ContentKind.Live);
        channels.AddRange(ParseXtreamStreams(await vodStreamsTask, vodCategories, credentials, ContentKind.Movie));

        // Series episode URLs differ between providers. Preserve the proven M3U
        // episode list while live TV and VOD come from the typed Xtream API.
        try
        {
            var m3uPath = await ResolvePlaylistPathAsync(entry, forceRefresh);
            channels.AddRange((await Task.Run(() => ParseM3u(m3uPath))).Where(channel => channel.Kind == ContentKind.Series));
        }
        catch
        {
            // Live TV and movies remain usable even when the provider omits M3U output.
        }

        _channels = channels
            .GroupBy(channel => channel.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        RefreshGroups();
        var liveCount = _channels.Count(channel => channel.Kind == ContentKind.Live);
        var movieCount = _channels.Count(channel => channel.Kind == ContentKind.Movie);
        var seriesCount = _channels.Count(channel => channel.Kind == ContentKind.Series);
        SetLoadingStatus($"Xtream API · {liveCount:N0} {L("canlı")} · {movieCount:N0} {L("film")} · {seriesCount:N0} {L("dizi")}");
    }

    private static Dictionary<string, string> ParseXtreamCategories(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException(L("Kategori yanıtı geçersiz."));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = ReadJsonString(element, "category_id");
            var name = ReadJsonString(element, "category_name");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) result[id] = name;
        }
        return result;
    }

    private static List<Channel> ParseXtreamStreams(
        string json,
        IReadOnlyDictionary<string, string> categories,
        XtreamCredentials credentials,
        ContentKind kind)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException(L("İçerik yanıtı geçersiz."));
        var result = new List<Channel>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = ReadJsonString(element, "stream_id");
            var name = ReadJsonString(element, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            var categoryId = ReadJsonString(element, "category_id") ?? "";
            var group = categories.TryGetValue(categoryId, out var categoryName) ? categoryName : "Diğer";
            var logo = ReadJsonString(element, "stream_icon");
            var tvgId = ReadJsonString(element, "epg_channel_id");
            var extension = kind == ContentKind.Live
                ? "ts"
                : (ReadJsonString(element, "container_extension")?.TrimStart('.') ?? "mp4");
            var section = kind == ContentKind.Live ? "live" : "movie";
            var url = $"{credentials.Server}/{section}/{Uri.EscapeDataString(credentials.Username)}/{Uri.EscapeDataString(credentials.Password)}/{id}.{extension}";
            result.Add(new Channel(name, url, group, logo, kind, tvgId));
        }
        return result;
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
            _ => null
        };
    }

    private static bool TryGetXtreamCredentials(PlaylistEntry entry, out XtreamCredentials credentials)
    {
        if (!string.IsNullOrWhiteSpace(entry.XtreamServer) &&
            !string.IsNullOrWhiteSpace(entry.XtreamUsername) &&
            !string.IsNullOrWhiteSpace(entry.XtreamPassword))
        {
            credentials = new XtreamCredentials(entry.XtreamServer.TrimEnd('/'), entry.XtreamUsername, entry.XtreamPassword);
            return true;
        }

        if (Uri.TryCreate(entry.Path, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(part => part.Length == 2)
                .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part[1]), StringComparer.OrdinalIgnoreCase);
            if (query.TryGetValue("username", out var username) && query.TryGetValue("password", out var password))
            {
                var path = uri.AbsolutePath;
                var slash = path.LastIndexOf('/');
                var basePath = slash > 0 ? path[..slash] : "";
                credentials = new XtreamCredentials(uri.GetLeftPart(UriPartial.Authority) + basePath, username, password);
                return true;
            }
        }

        credentials = default;
        return false;
    }

    private async Task<string> ResolveEpgPathAsync(PlaylistEntry entry, bool forceRefresh)
    {
        Directory.CreateDirectory(PlaylistCacheDirectory);
        var cachePath = Path.Combine(PlaylistCacheDirectory, $"{entry.Id}.xml");
        if (!forceRefresh && File.Exists(cachePath) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(12)) return cachePath;

        using var response = await PlaylistClient.GetAsync(entry.EpgUrl!, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var downloadPath = cachePath + ".download";
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            await input.CopyToAsync(output);

        await using (var input = new FileStream(downloadPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var first = input.ReadByte();
            var second = input.ReadByte();
            input.Position = 0;
            if (first == 0x1f && second == 0x8b)
            {
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                await using var output = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
                await gzip.CopyToAsync(output);
            }
            else
            {
                input.Close();
                File.Move(downloadPath, cachePath, true);
            }
        }
        if (File.Exists(downloadPath)) File.Delete(downloadPath);
        using (var reader = XmlReader.Create(cachePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
        {
            reader.MoveToContent();
            if (!reader.Name.Equals("tv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L("Sunucu geçerli XMLTV verisi döndürmedi."));
        }
        return cachePath;
    }

    private async Task<string> ResolvePlaylistPathAsync(PlaylistEntry entry, bool forceRefresh)
    {
        if (!entry.IsRemote)
        {
            if (!File.Exists(entry.Path)) throw new FileNotFoundException(L("Oynatma listesi dosyası bulunamadı."));
            return entry.Path;
        }

        Directory.CreateDirectory(PlaylistCacheDirectory);
        var cachePath = Path.Combine(PlaylistCacheDirectory, $"{entry.Id}.m3u");
        if (!forceRefresh && IsM3uFile(cachePath)) return cachePath;

        PageTitle.Text = "Liste indiriliyor...";
        SetLoadingStatus($"{entry.Name} indiriliyor...");
        using var response = await PlaylistClient.GetAsync(entry.Path, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var tempPath = cachePath + ".download";
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            var buffer = new byte[1024 * 1024];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                SetLoadingStatus(total > 0
                    ? $"{L("İndiriliyor")} %{received * 100 / total.Value}"
                    : $"{L("İndiriliyor")} {received / 1024d / 1024d:0.0} MB");
            }
        }
        if (!IsM3uFile(tempPath))
        {
            File.Delete(tempPath);
            throw new InvalidDataException(L("Sunucu geçerli bir M3U listesi döndürmedi."));
        }
        File.Move(tempPath, cachePath, true);
        return cachePath;
    }

    private static bool IsM3uFile(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 7) return false;
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true, 4096);
            for (var i = 0; i < 5 && reader.ReadLine() is { } line; i++)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                return line.TrimStart('\uFEFF').StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    private async Task LoadPlaylistAsync(string path, string? displayName = null)
    {
        PageTitle.Text = L("Liste yükleniyor...");
        BeginLoading($"{displayName ?? Path.GetFileName(path)} okunuyor...");
        try
        {
            _channels = await Task.Run(() => ParseM3u(path));
            RefreshGroups();
            var liveCount = _channels.Count(c => c.Kind == ContentKind.Live);
            var movieCount = _channels.Count(c => c.Kind == ContentKind.Movie);
            var seriesCount = _channels.Count(c => c.Kind == ContentKind.Series);
            SetLoadingStatus($"{liveCount:N0} {L("canlı")} · {movieCount:N0} {L("film")} · {seriesCount:N0} {L("dizi")}");
        }
        catch (Exception ex) { SetLoadingStatus($"{L("Liste açılamadı")}: {ex.Message}"); }
        finally { EndLoading(); }
    }

    private static List<Channel> ParseM3u(string path)
    {
        var result = new List<Channel>(10000);
        string? info = null;
        using var reader = new StreamReader(path, Encoding.UTF8, true, 1024 * 1024);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase)) { info = line; continue; }
            if (info is null || string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
            var name = ReadDisplayName(info);
            var url = line.Trim();
            var group = ReadAttribute(info, "group-title") ?? "Diğer";
            result.Add(new Channel(
                name,
                url,
                group,
                ReadAttribute(info, "tvg-logo"),
                ClassifyContent(url, group, name),
                ReadAttribute(info, "tvg-id")));
            info = null;
        }
        return result;
    }

    private static string ReadDisplayName(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ',' && !quoted) return line[(i + 1)..].Trim();
        }
        return L("İsimsiz kanal");
    }

    private static string? ReadAttribute(string line, string name)
    {
        var match = Regex.Match(line, $"(?:^|\\s){Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static EpgSnapshot ParseXmlTv(string path)
    {
        var snapshot = new EpgSnapshot();
        var now = DateTimeOffset.Now;
        var firstDay = DateTime.Today;
        var lastDayExclusive = firstDay.AddDays(7);
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Name.Equals("channel", StringComparison.OrdinalIgnoreCase))
            {
                var id = reader.GetAttribute("id")?.Trim();
                if (string.IsNullOrWhiteSpace(id)) continue;
                using var subtree = reader.ReadSubtree();
                while (subtree.Read())
                {
                    if (subtree.NodeType == XmlNodeType.Element && subtree.Name.Equals("display-name", StringComparison.OrdinalIgnoreCase))
                    {
                        var displayName = subtree.ReadElementContentAsString().Trim();
                        var normalized = NormalizeEpgName(displayName);
                        if (normalized.Length > 0) snapshot.ChannelIdByName.TryAdd(normalized, id);
                    }
                }
                continue;
            }

            if (!reader.Name.Equals("programme", StringComparison.OrdinalIgnoreCase)) continue;
            var channelId = reader.GetAttribute("channel")?.Trim();
            if (string.IsNullOrWhiteSpace(channelId) ||
                !TryParseXmlTvTime(reader.GetAttribute("start"), out var start) ||
                !TryParseXmlTvTime(reader.GetAttribute("stop"), out var stop)) continue;

            var localStart = start.LocalDateTime;
            var localStop = stop.LocalDateTime;
            if (localStop <= firstDay || localStart >= lastDayExclusive) continue;

            string title = "Program bilgisi";
            string? description = null;
            string? category = null;
            using (var subtree = reader.ReadSubtree())
            {
                while (subtree.Read())
                {
                    if (subtree.NodeType != XmlNodeType.Element) continue;
                    if (subtree.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                        title = subtree.ReadElementContentAsString().Trim();
                    else if (subtree.Name.Equals("desc", StringComparison.OrdinalIgnoreCase))
                        description = subtree.ReadElementContentAsString().Trim();
                    else if (subtree.Name.Equals("category", StringComparison.OrdinalIgnoreCase))
                        category ??= subtree.ReadElementContentAsString().Trim();
                }
            }

            var programme = new EpgProgramme(title, description, category, start, stop);
            if (!snapshot.Schedules.TryGetValue(channelId, out var schedule))
            {
                schedule = new EpgSchedule();
                snapshot.Schedules[channelId] = schedule;
            }
            schedule.Programs.Add(programme);
            if (start <= now && stop > now) schedule.Current = programme;
            else if (start > now && (schedule.Next is null || start < schedule.Next.Start)) schedule.Next = programme;
        }
        foreach (var schedule in snapshot.Schedules.Values) schedule.Programs.Sort((left, right) => left.Start.CompareTo(right.Start));
        return snapshot;
    }

    private static bool TryParseXmlTvTime(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = Regex.Match(value, @"^(?<date>\d{14})(?:\s*(?<offset>[+-]\d{4}))?");
        if (!match.Success || !DateTime.TryParseExact(
                match.Groups["date"].Value,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)) return false;

        var offset = TimeZoneInfo.Local.GetUtcOffset(date);
        if (match.Groups["offset"].Success)
        {
            var raw = match.Groups["offset"].Value;
            var sign = raw[0] == '-' ? -1 : 1;
            offset = TimeSpan.FromMinutes(sign * (int.Parse(raw.Substring(1, 2), CultureInfo.InvariantCulture) * 60 +
                                                   int.Parse(raw.Substring(3, 2), CultureInfo.InvariantCulture)));
        }
        try { result = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), offset); }
        catch { return false; }
        return true;
    }

    private static string NormalizeEpgName(string value)
    {
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(character);
        }
        return builder.ToString();
    }

    private EpgSchedule? FindEpgSchedule(Channel channel)
    {
        if (!string.IsNullOrWhiteSpace(channel.TvgId) && _epg.Schedules.TryGetValue(channel.TvgId, out var byId)) return byId;
        var name = NormalizeEpgName(channel.Name);
        return _epg.ChannelIdByName.TryGetValue(name, out var id) && _epg.Schedules.TryGetValue(id, out var byName)
            ? byName
            : null;
    }

    private string GetChannelSubtitle(Channel channel)
    {
        var schedule = FindEpgSchedule(channel);
        if (schedule?.Current is { } current)
        {
            var next = schedule.Next is { } upcoming ? $"  •  {L("Sırada")}: {upcoming.Title}" : "";
            return $"{L("Şimdi")}: {current.Title}{next}";
        }
        if (schedule?.Next is { } nextProgramme) return $"{nextProgramme.Start:HH:mm} · {nextProgramme.Title}";
        return channel.Group;
    }

    private string GetNowPlayingStatus(Channel channel)
    {
        var current = FindEpgSchedule(channel)?.Current;
        return current is null
            ? L("Canlı yayın oynatılıyor")
            : $"{L("Şimdi")} · {current.Title} · {current.Start:HH:mm}–{current.Stop:HH:mm}";
    }

    private static ContentKind ClassifyContent(string url, string group, string name)
    {
        var address = url.ToLowerInvariant();
        if (ContainsAny(address, "/series/", "type=series", "stream_type=series")) return ContentKind.Series;
        if (ContainsAny(address, "/movie/", "type=movie", "stream_type=movie", "type=vod", "stream_type=vod")) return ContentKind.Movie;
        if (ContainsAny(address, "/live/", "type=live", "stream_type=live")) return ContentKind.Live;

        var category = NormalizeClassifierText(group);
        var title = NormalizeClassifierText(name);

        // Some providers use these groups for linear/24-hour channels even when
        // the group name also contains words such as DIZI or SINEMA.
        if (category.StartsWith("▱", StringComparison.Ordinal) ||
            category.StartsWith("▰", StringComparison.Ordinal) ||
            category.StartsWith("TR:", StringComparison.Ordinal) ||
            ContainsAny(category, "CANLI", "LIVE", "RADYO", "MOBESE", "RAW 50 FPS"))
            return ContentKind.Live;

        // Episode notation is stronger evidence than a generic word in a title.
        if (Regex.IsMatch(title, @"\bS\s*\d{1,3}\s*E\s*\d{1,4}\b|\b\d{1,3}\s*X\s*\d{1,4}\b|\bSEZON\s*\d+.*\bBOLUM\s*\d+\b", RegexOptions.CultureInvariant))
            return ContentKind.Series;

        if (ContainsAny(category,
                "DIZI", "SERIES", "TV SHOW", "SEZON", "SEASON", "ANIME DIZI", "EGITIM SETLERI"))
            return ContentKind.Series;

        if (category.StartsWith("4K", StringComparison.Ordinal) || ContainsAny(category,
                "FILM", "MOVIE", "SINEMA", "VOD", "VIZYON", "YESILCAM", "MUBI", "IMDB", "BOLLYWOOD",
                "KLASIK", "WESTERN", "AKSIYON", "MACERA", "GIZEM", "DRAM", "KOMEDI", "ROMANTIK",
                "KORKU", "PSIKOLOJIK", "BILIM KURGU", "FANTASTIK", "POLISIYE", "SUC", "SAVAS", "TARIH",
                "ANIMASYON", "BELGESEL", "BLURAY", "ALTYAZILI", "NOSTALJI", "FOR ADULT", "YETISKIN",
                "EROTIC", "AILE", "STAND-UP", "TIYATRO", "ONERILER"))
            return ContentKind.Movie;

        return ContentKind.Live;
    }

    private static string NormalizeClassifierText(string value) => value
        .Trim()
        .ToUpperInvariant()
        .Replace('İ', 'I')
        .Replace('Ş', 'S')
        .Replace('Ç', 'C')
        .Replace('Ğ', 'G')
        .Replace('Ü', 'U')
        .Replace('Ö', 'O');

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);

    private void RefreshGroups(bool preserveSelection = false, bool resetSeriesBrowser = true)
    {
        UpdateHomeDashboard();
        var previousGroup = _selectedGroup;
        var previousKind = _selectedGroupKind;
        if (resetSeriesBrowser) ResetSeriesBrowser();
        var sectionChannels = _channels.Where(c => c.Kind == _selectedContent).ToList();
        var regularGroups = sectionChannels
            .GroupBy(c => c.Group)
            .Select(g => new ChannelGroup(g.Key, g.Count(), LibraryGroupKind.Regular))
            .OrderBy(g => ContainsAdult(g.Name) ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var groups = new List<ChannelGroup>();
        var favoriteCount = GetLibraryChannels(LibraryGroupKind.Favorites, _selectedContent).Count;
        if (favoriteCount > 0) groups.Add(new ChannelGroup("★ Favoriler", favoriteCount, LibraryGroupKind.Favorites));

        var recentChannels = GetLibraryChannels(LibraryGroupKind.Recent, _selectedContent);
        if (recentChannels.Count > 0)
            groups.Add(new ChannelGroup(L("◷ Son İzlenenler"), recentChannels.Count, LibraryGroupKind.Recent));

        if (_selectedContent != ContentKind.Live)
        {
            var continueWatchingChannels = GetLibraryChannels(LibraryGroupKind.ContinueWatching, _selectedContent);
            if (continueWatchingChannels.Count > 0)
                groups.Add(new ChannelGroup(L("▶ İzlemeye Devam Et"), continueWatchingChannels.Count, LibraryGroupKind.ContinueWatching));
        }

        groups.AddRange(regularGroups);
        _suppressGroupSelection = true;
        GroupList.ItemsSource = groups;
        var selectedIndex = preserveSelection
            ? groups.FindIndex(g => g.Kind == previousKind && string.Equals(g.Name, previousGroup, StringComparison.CurrentCultureIgnoreCase))
            : -1;
        if (selectedIndex < 0) selectedIndex = groups.Count > 0 ? 0 : -1;
        GroupList.SelectedIndex = selectedIndex;
        _suppressGroupSelection = false;
        var selected = selectedIndex >= 0 ? groups[selectedIndex] : null;
        _selectedGroup = selected?.Name ?? "";
        _selectedGroupKind = selected?.Kind ?? LibraryGroupKind.Regular;
        PageTitle.Text = selected?.Name ?? ContentTitle(_selectedContent);
        ApplyFilter();
    }

    private void GroupList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressGroupSelection) return;
        if (GroupList.SelectedItem is not ChannelGroup group) return;
        // Gruplar arasında gezinmek oynatmayı kesmez; yalnızca bir içerik seçilince geçiş yapılır.
        _isGlobalSearch = false;
        _selectedGroup = group.Name;
        _selectedGroupKind = group.Kind;
        ResetSeriesBrowser();
        PageTitle.Text = group.Name;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void LiveSection_Click(object? sender, RoutedEventArgs e) => OpenLibrary(ContentKind.Live);
    private void MovieSection_Click(object? sender, RoutedEventArgs e) => OpenLibrary(ContentKind.Movie);
    private void SeriesSection_Click(object? sender, RoutedEventArgs e) => OpenLibrary(ContentKind.Series);
    private void HomeLive_Click(object? sender, RoutedEventArgs e) => OpenLibrary(ContentKind.Live);
    private void HomeMovie_Click(object? sender, RoutedEventArgs e) => OpenLibrary(ContentKind.Movie);
    private void HomeSeries_Click(object? sender, RoutedEventArgs e) => OpenLibrary(ContentKind.Series);

    private void OpenLibrary(ContentKind kind)
    {
        HomePage.IsVisible = false;
        SettingsPage.IsVisible = false;
        ContentArea.IsVisible = true;
        Sidebar.IsVisible = true;
        HeaderPanel.IsVisible = true;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("350,*");
        LibrarySidebarTitle.Text = kind switch
        {
            ContentKind.Movie => L("FİLMLER"),
            ContentKind.Series => L("DİZİLER"),
            _ => L("CANLI TV")
        };
        SetContentSection(kind);
        if (_playingChannel is not null) Dispatcher.UIThread.Post(ShowPlayerOverlay);
    }

    private void BackHome_Click(object? sender, RoutedEventArgs e) => ShowHomePage();

    private void ShowHomePage()
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        StopPlaybackForNavigation();
        SettingsPage.IsVisible = false;
        ContentArea.IsVisible = false;
        Sidebar.IsVisible = false;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("0,*");
        UpdateHomeDashboard();
        HomePage.IsVisible = true;
    }

    private void HomeSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        StartGlobalSearch(HomeSearchBox.Text ?? "");
        e.Handled = true;
    }

    // Ana sayfadaki arama bölüm ayrımı yapmadan tüm kitaplıkta arar.
    private void StartGlobalSearch(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;

        _isGlobalSearch = true;
        HomePage.IsVisible = false;
        SettingsPage.IsVisible = false;
        ContentArea.IsVisible = true;
        Sidebar.IsVisible = true;
        HeaderPanel.IsVisible = true;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("350,*");

        _suppressGroupSelection = true;
        GroupList.SelectedIndex = -1;
        _suppressGroupSelection = false;

        SearchBox.Text = query;
        ApplyFilter();
        if (_playingChannel is not null) Dispatcher.UIThread.Post(ShowPlayerOverlay);
    }

    private void ApplyGlobalSearchFilter(string query)
    {
        const int limit = 500;
        var matches = _channels
            .Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var shown = matches.Take(limit).ToList();

        ChannelList.ItemsSource = shown.Select(channel => channel.Kind == ContentKind.Series
            ? MediaBrowserItem.FromEpisode(channel, channel.Group)
            : MediaBrowserItem.FromChannel(channel, GetChannelSubtitle(channel))).ToList();

        PageTitle.Text = query.Length == 0 ? L("Arama") : $"\"{query}\"{L(" sonuçları")}";
        BrowserTitle.Text = L("TÜM İÇERİKLER");
        SeriesBackButton.IsVisible = false;
        ClearHistoryButton.IsVisible = false;
        ChannelCount.Text = matches.Count > shown.Count
            ? $"{L("ilk")} {shown.Count:N0} / {matches.Count:N0} {L("sonuç")}"
            : $"{matches.Count:N0} {L("sonuç")}";
    }

    private void UpdateHomeDashboard()
    {
        var now = DateTime.Now;
        HomeTimeText.Text = now.ToString("HH:mm");
        HomeDateText.Text = now.ToString("d MMMM dddd");
        HomeLiveCount.Text = $"{_channels.Count(c => c.Kind == ContentKind.Live):N0} kanal";
        HomeMovieCount.Text = $"{_channels.Count(c => c.Kind == ContentKind.Movie):N0} film";
        var seriesCount = _channels
            .Where(c => c.Kind == ContentKind.Series)
            .Select(c => string.IsNullOrWhiteSpace(c.Series.Title) ? c.Name : c.Series.Title)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();
        HomeSeriesCount.Text = $"{seriesCount:N0} dizi";
    }

    private void SelectLibraryGroup(LibraryGroupKind kind)
    {
        if (GroupList.ItemsSource is not IEnumerable<ChannelGroup> groups) return;
        var match = groups.FirstOrDefault(g => g.Kind == kind);
        if (match is not null) GroupList.SelectedItem = match;
    }

    private void BeginLoading(string message)
    {
        _loadingDepth++;
        LoadingStatusText.Text = message;
        LoadingOverlay.IsVisible = true;
    }

    private void EndLoading()
    {
        _loadingDepth = Math.Max(0, _loadingDepth - 1);
        if (_loadingDepth == 0) LoadingOverlay.IsVisible = false;
    }

    // Yükleme sırasında hem oynatıcı satırını hem açılış ekranını aynı metinle besler.
    private void SetLoadingStatus(string message)
    {
        PlaybackStatus.Text = message;
        if (LoadingOverlay.IsVisible) LoadingStatusText.Text = message;
    }

    private void SetContentSection(ContentKind kind)
    {
        _isGlobalSearch = false;
        _selectedContent = kind;
        SetActiveClass(LiveSectionButton, kind == ContentKind.Live);
        SetActiveClass(MovieSectionButton, kind == ContentKind.Movie);
        SetActiveClass(SeriesSectionButton, kind == ContentKind.Series);
        RefreshGroups();
    }

    private static void SetActiveClass(Button button, bool active)
    {
        if (active && !button.Classes.Contains("active")) button.Classes.Add("active");
        else if (!active) button.Classes.Remove("active");
    }

    private static string ContentTitle(ContentKind kind) => kind switch
    {
        ContentKind.Movie => "Filmler",
        ContentKind.Series => "Diziler",
        _ => L("Canlı TV")
    };

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        if (_isGlobalSearch)
        {
            ApplyGlobalSearchFilter(query);
            return;
        }

        if (_selectedGroupKind != LibraryGroupKind.Regular)
        {
            ApplyLibraryFilter(query);
            return;
        }

        ClearHistoryButton.IsVisible = false;
        if (_selectedContent == ContentKind.Series)
        {
            ApplySeriesFilter(query);
            return;
        }

        var channels = _channels
            .Where(c => c.Kind == _selectedContent && c.Group == _selectedGroup &&
                        (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        ChannelList.ItemsSource = channels.Select(channel => MediaBrowserItem.FromChannel(channel, GetChannelSubtitle(channel))).ToList();
        BrowserTitle.Text = _selectedContent == ContentKind.Movie ? L("FİLMLER") : L("KANALLAR");
        SeriesBackButton.IsVisible = false;
        ChannelCount.Text = _selectedContent == ContentKind.Movie ? $"{channels.Count:N0} film" : $"{channels.Count:N0} kanal";
    }

    private void ApplyLibraryFilter(string query)
    {
        var channels = GetLibraryChannels(_selectedGroupKind, _selectedContent)
            .Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        ChannelList.ItemsSource = channels.Select(channel =>
        {
            var state = FindLibraryItem(channel);
            var progressBadge = _selectedGroupKind == LibraryGroupKind.ContinueWatching && state is not null
                ? $"%{Math.Clamp((int)Math.Round(state.PositionMs * 100d / Math.Max(1, state.DurationMs)), 1, 99)}"
                : null;
            var subtitle = _selectedGroupKind switch
            {
                LibraryGroupKind.Recent => $"Son izlendi · {FormatRelativeTime(state?.LastWatchedAt)}",
                LibraryGroupKind.ContinueWatching => $"{L("Kaldığın yer")} · {FormatTime(state?.PositionMs ?? 0)}",
                _ => channel.Group
            };

            var canRemove = _selectedGroupKind is LibraryGroupKind.Recent or LibraryGroupKind.ContinueWatching;
            return channel.Kind == ContentKind.Series
                ? MediaBrowserItem.FromEpisode(channel, subtitle, progressBadge, canRemove)
                : MediaBrowserItem.FromChannel(channel, subtitle, progressBadge, canRemove);
        }).ToList();

        BrowserTitle.Text = _selectedGroupKind switch
        {
            LibraryGroupKind.Favorites => L("FAVORİLER"),
            LibraryGroupKind.Recent => L("SON İZLENENLER"),
            _ => L("İZLEMEYE DEVAM ET")
        };
        SeriesBackButton.IsVisible = false;
        ClearHistoryButton.IsVisible = channels.Count > 0 &&
            _selectedGroupKind is LibraryGroupKind.Recent or LibraryGroupKind.ContinueWatching;
        ClearHistoryText.Text = _selectedGroupKind == LibraryGroupKind.ContinueWatching
            ? "Listeyi Temizle"
            : "Geçmişi Temizle";
        ChannelCount.Text = $"{channels.Count:N0} {L("içerik")}";
    }

    private static bool ContainsAdult(string value) =>
        value.Contains("adult", StringComparison.OrdinalIgnoreCase);

    private void ApplySeriesFilter(string query)
    {
        var groupEpisodes = _channels
            .Where(c => c.Kind == ContentKind.Series && c.Group == _selectedGroup)
            .ToList();

        if (_seriesBrowserLevel == SeriesBrowserLevel.Shows)
        {
            var shows = groupEpisodes
                .GroupBy(c => c.Series.Title, StringComparer.CurrentCultureIgnoreCase)
                .Where(g => query.Length == 0 ||
                            g.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                            g.Any(c => c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(MediaBrowserItem.FromSeries)
                .ToList();
            ChannelList.ItemsSource = shows;
            BrowserTitle.Text = L("DİZİLER");
            SeriesBackButton.IsVisible = false;
            ChannelCount.Text = $"{shows.Count:N0} dizi";
            return;
        }

        var seriesEpisodes = groupEpisodes
            .Where(c => string.Equals(c.Series.Title, _selectedSeriesTitle, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        if (_seriesBrowserLevel == SeriesBrowserLevel.Seasons)
        {
            var seasons = seriesEpisodes
                .GroupBy(c => c.Series.Season)
                .OrderBy(g => g.Key.HasValue ? 0 : 1)
                .ThenBy(g => g.Key)
                .Select(g => MediaBrowserItem.FromSeason(_selectedSeriesTitle ?? "Dizi", g.Key, g.Count(), g.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.LogoUrl))?.LogoUrl))
                .ToList();
            ChannelList.ItemsSource = seasons;
            BrowserTitle.Text = _selectedSeriesTitle?.ToUpperInvariant() ?? "SEZONLAR";
            SeriesBackButton.IsVisible = true;
            ChannelCount.Text = $"{seasons.Count:N0} sezon";
            return;
        }

        var episodes = seriesEpisodes
            .Where(c => c.Series.Season == _selectedSeriesSeason &&
                        (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(c => c.Series.Episode ?? int.MaxValue)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(channel => MediaBrowserItem.FromEpisode(channel))
            .ToList();
        ChannelList.ItemsSource = episodes;
        BrowserTitle.Text = _selectedSeriesSeason.HasValue ? $"{L("SEZON")} {_selectedSeriesSeason}" : L("DİĞER BÖLÜMLER");
        SeriesBackButton.IsVisible = true;
        ChannelCount.Text = $"{episodes.Count:N0} {L("bölüm")}";
    }

    private void ResetSeriesBrowser()
    {
        _seriesBrowserLevel = SeriesBrowserLevel.Shows;
        _selectedSeriesTitle = null;
        _selectedSeriesSeason = null;
        if (SeriesBackButton is not null) SeriesBackButton.IsVisible = false;
    }

    private void SeriesBack_Click(object? sender, RoutedEventArgs e)
    {
        if (_seriesBrowserLevel == SeriesBrowserLevel.Episodes)
        {
            _seriesBrowserLevel = SeriesBrowserLevel.Seasons;
            _selectedSeriesSeason = null;
        }
        else if (_seriesBrowserLevel == SeriesBrowserLevel.Seasons)
        {
            ResetSeriesBrowser();
        }

        SearchBox.Text = "";
        PageTitle.Text = _seriesBrowserLevel == SeriesBrowserLevel.Shows ? _selectedGroup : _selectedSeriesTitle ?? _selectedGroup;
        ApplyFilter();
    }

    private void ChannelList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressChannelSelection) return;
        if (ChannelList.SelectedItem is not MediaBrowserItem item) return;
        if (item.Kind == MediaBrowserItemKind.Series)
        {
            _selectedSeriesTitle = item.SeriesTitle;
            _seriesBrowserLevel = SeriesBrowserLevel.Seasons;
            PageTitle.Text = item.Name;
            ChannelList.SelectedIndex = -1;
            ApplyFilter();
            return;
        }

        if (item.Kind == MediaBrowserItemKind.Season)
        {
            _selectedSeriesSeason = item.Season;
            _seriesBrowserLevel = SeriesBrowserLevel.Episodes;
            PageTitle.Text = item.Name;
            ChannelList.SelectedIndex = -1;
            ApplyFilter();
            return;
        }

        if (item.Channel is not { } channel) return;
        PlayChannel(channel);
    }

    private void PlayChannel(Channel channel)
    {
        SaveCurrentPlaybackProgress();
        if (_playingChannel is { } current && !string.Equals(current.Url, channel.Url, StringComparison.OrdinalIgnoreCase))
            _lastPlayingChannel = current;
        _mediaPlayer.Stop();
        PlayerPlaceholder.IsVisible = false;
        PlayerView.IsVisible = true;
        _media?.Dispose();
        _media = new Media(_libVlc, new Uri(channel.Url));
        _media.AddOption(":network-caching=1800");
        _media.AddOption(":http-reconnect");
        NowPlaying.Text = channel.Name;
        NowPlayingLogo.LogoUrl = channel.LogoUrl;
        NowPlayingLogo.Initials = channel.Initials;
        NowPlayingLogo.IsVisible = true;
        PlaybackKindBadge.Text = channel.Badge;
        _playingContent = channel.Kind;
        _playingChannel = channel;
        _selectedEpgDate = DateTime.Today;
        // EPG verisini hazırla, ancak oynatma alanını kullanıcı istemeden daraltma.
        UpdateEpgPanel(channel, false);
        _historyRecordedForCurrentPlayback = false;
        _pendingResumePosition = GetResumePosition(channel);
        UpdateFavoriteButton();
        PlaybackStatus.Text = L("Yayına bağlanılıyor...");
        PlayPauseButton.IsEnabled = true;
        UpdateChannelNavigationButtons();
        PlayPauseIcon.Data = Avalonia.Media.Geometry.Parse("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z");
        Timeline.Value = 0;
        Timeline.IsEnabled = false;
        UpdateSeekControls(false);
        TimelinePanel.IsVisible = false;
        if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,104");
        TimeLabel.Text = "CANLI";
        _mediaPlayer.Play(_media);
        Dispatcher.UIThread.Post(ShowPlayerOverlay);
    }

    private void StopPlaybackForNavigation()
    {
        if (_playingChannel is null && _media is null) return;

        SaveCurrentPlaybackProgress();
        _mediaPlayer.Stop();
        _media?.Dispose();
        _media = null;
        _playingChannel = null;
        _pendingResumePosition = null;
        _historyRecordedForCurrentPlayback = false;
        HidePlayerOverlay();
        SetEpgPanelVisibility(false);

        PlayerView.IsVisible = false;
        PlayerPlaceholder.IsVisible = true;
        NowPlaying.Text = L("İzlemek için bir kanal seçin");
        NowPlayingLogo.IsVisible = false;
        NowPlayingLogo.LogoUrl = null;
        PlaybackStatus.Text = L("Hazır");
        PlaybackKindBadge.Text = "HAZIR";
        PlayPauseButton.IsEnabled = false;
        Timeline.Value = 0;
        Timeline.Maximum = 1;
        Timeline.IsEnabled = false;
        TimelinePanel.IsVisible = false;
        TimeLabel.Text = "CANLI";
        UpdateSeekControls(false);
        UpdateChannelNavigationButtons();
        UpdateFavoriteButton();
        UpdatePlayPauseIcons(false);

        _suppressChannelSelection = true;
        ChannelList.SelectedIndex = -1;
        _suppressChannelSelection = false;
    }

    private void ToggleEpgPanel_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingChannel is not { Kind: ContentKind.Live } channel) return;
        if (EpgPanel.IsVisible) SetEpgPanelVisibility(false);
        else UpdateEpgPanel(channel, true);
    }

    private void CloseEpgPanel_Click(object? sender, RoutedEventArgs e) => SetEpgPanelVisibility(false);

    private void SetEpgPanelVisibility(bool visible)
    {
        var shouldShow = visible && !_isPlayerFullscreen;
        EpgPanel.IsVisible = shouldShow;
        PlayerSurfaceLayout.ColumnDefinitions = shouldShow
            ? new ColumnDefinitions("*,310")
            : new ColumnDefinitions("*,0");
        Dispatcher.UIThread.Post(UpdatePlayerOverlayBounds);
    }

    private void UpdateEpgPanel(Channel channel, bool showPanel)
    {
        var isLive = channel.Kind == ContentKind.Live;
        EpgButton.IsEnabled = isLive;
        if (!isLive)
        {
            SetEpgPanelVisibility(false);
            EpgProgrammeList.ItemsSource = null;
            return;
        }

        EpgChannelName.Text = channel.Name;
        RefreshEpgProgrammeList(channel);
        if (showPanel) SetEpgPanelVisibility(true);
    }

    private void RefreshEpgProgrammeList(Channel? channel = null)
    {
        channel ??= _playingChannel;
        if (channel is not { Kind: ContentKind.Live }) return;

        EpgDateText.Text = _selectedEpgDate.Date == DateTime.Today
            ? L("BUGÜN")
            : _selectedEpgDate.ToString("d MMMM dddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        EpgPreviousDayButton.IsEnabled = _selectedEpgDate.Date > DateTime.Today;
        EpgNextDayButton.IsEnabled = _selectedEpgDate.Date < DateTime.Today.AddDays(6);
        var query = EpgSearchBox.Text?.Trim() ?? "";
        var programmes = FindEpgSchedule(channel)?.Programs ?? [];
        var items = programmes
            .Where(programme => programme.Start.LocalDateTime.Date == _selectedEpgDate.Date &&
                                (query.Length == 0 || programme.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .Select(programme => new EpgProgrammeItem(programme, DateTimeOffset.Now))
            .ToList();
        EpgProgrammeList.ItemsSource = items;
        EpgProgrammeList.IsVisible = items.Count > 0;
        EpgEmptyText.IsVisible = items.Count == 0;
        EpgEmptyText.Text = query.Length > 0
            ? L("Aramanızla eşleşen program bulunamadı.")
            : L("Bu gün için yayın akışı bulunamadı.");

        if (items.FindIndex(item => item.IsCurrent) is var currentIndex && currentIndex >= 0)
            Dispatcher.UIThread.Post(() => EpgProgrammeList.ScrollIntoView(currentIndex));
    }

    private void EpgSearchBox_TextChanged(object? sender, TextChangedEventArgs e) => RefreshEpgProgrammeList();

    private void EpgPreviousDay_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedEpgDate.Date <= DateTime.Today) return;
        _selectedEpgDate = _selectedEpgDate.AddDays(-1);
        RefreshEpgProgrammeList();
    }

    private void EpgNextDay_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedEpgDate.Date >= DateTime.Today.AddDays(6)) return;
        _selectedEpgDate = _selectedEpgDate.AddDays(1);
        RefreshEpgProgrammeList();
    }

    private List<Channel> GetNavigableChannels()
    {
        var visible = (ChannelList.ItemsSource as IEnumerable<MediaBrowserItem>)?
            .Where(item => item.Channel is { Kind: ContentKind.Live })
            .Select(item => item.Channel!)
            .ToList();
        if (visible is { Count: > 0 }) return visible;
        return _channels.Where(channel => channel.Kind == ContentKind.Live).ToList();
    }

    private void PlayAdjacentChannel(int offset)
    {
        var channels = GetNavigableChannels();
        if (channels.Count == 0) return;
        var currentIndex = _playingChannel is null
            ? -1
            : channels.FindIndex(channel => string.Equals(channel.Url, _playingChannel.Url, StringComparison.OrdinalIgnoreCase));
        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + offset % channels.Count + channels.Count) % channels.Count;
        PlayChannel(channels[nextIndex]);
        SelectPlayingChannelInList(channels[nextIndex]);
    }

    private void ReturnToLastChannel()
    {
        if (_lastPlayingChannel is not { } last) return;
        var current = _playingChannel;
        _lastPlayingChannel = null;
        PlayChannel(last);
        _lastPlayingChannel = current;
        SelectPlayingChannelInList(last);
    }

    private void SelectPlayingChannelInList(Channel channel)
    {
        var item = (ChannelList.ItemsSource as IEnumerable<MediaBrowserItem>)?
            .FirstOrDefault(candidate => candidate.Channel is { } listed && string.Equals(listed.Url, channel.Url, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        _suppressChannelSelection = true;
        ChannelList.SelectedItem = item;
        _suppressChannelSelection = false;
        ChannelList.ScrollIntoView(item);
    }

    private void PreviousChannel_Click(object? sender, RoutedEventArgs e) => PlayAdjacentChannel(-1);
    private void NextChannel_Click(object? sender, RoutedEventArgs e) => PlayAdjacentChannel(1);
    private void LastChannel_Click(object? sender, RoutedEventArgs e) => ReturnToLastChannel();

    private void UpdateChannelNavigationButtons()
    {
        var isLive = _playingChannel?.Kind == ContentKind.Live;
        PreviousChannelButton.IsVisible = isLive;
        LastChannelButton.IsVisible = isLive;
        NextChannelButton.IsVisible = isLive;
        PreviousChannelButton.IsEnabled = isLive;
        NextChannelButton.IsEnabled = isLive;
        LastChannelButton.IsEnabled = isLive && _lastPlayingChannel is not null;
        if (_fullscreenPreviousChannelButton is not null) _fullscreenPreviousChannelButton.IsVisible = isLive;
        if (_fullscreenLastChannelButton is not null) _fullscreenLastChannelButton.IsVisible = isLive;
        if (_fullscreenNextChannelButton is not null) _fullscreenNextChannelButton.IsVisible = isLive;
        if (_playerOverlayPreviousButton is not null) _playerOverlayPreviousButton.IsVisible = isLive;
        if (_playerOverlayLastButton is not null)
        {
            _playerOverlayLastButton.IsVisible = isLive;
            _playerOverlayLastButton.IsEnabled = isLive && _lastPlayingChannel is not null;
        }
        if (_playerOverlayNextButton is not null) _playerOverlayNextButton.IsVisible = isLive;
    }

    private void FavoriteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingChannel is not { } channel) return;
        var state = GetOrCreateLibraryItem(channel);
        state.IsFavorite = !state.IsFavorite;
        CleanupLibraryItem(channel.Id, state);
        SaveLibraryState();
        UpdateFavoriteButton();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void UpdateFavoriteButton()
    {
        FavoriteButton.IsEnabled = _playingChannel is not null;
        var isFavorite = _playingChannel is not null && FindLibraryItem(_playingChannel)?.IsFavorite == true;
        FavoriteOutlineIcon.IsVisible = !isFavorite;
        FavoriteFilledIcon.IsVisible = isFavorite;
        ToolTip.SetTip(FavoriteButton, L(isFavorite ? "Favorilerden çıkar" : "Favorilere ekle"));
    }

    private void RemoveRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Channel channel }) return;
        var state = FindLibraryItem(channel);
        if (state is null) return;

        if (_selectedGroupKind == LibraryGroupKind.ContinueWatching)
        {
            state.PositionMs = 0;
            state.DurationMs = 0;
        }
        else
        {
            state.LastWatchedAt = null;
        }

        CleanupLibraryItem(channel.Id, state);
        SaveLibraryState();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
        e.Handled = true;
    }

    private void ClearRecentHistory_Click(object? sender, RoutedEventArgs e)
    {
        var clearContinueWatching = _selectedGroupKind == LibraryGroupKind.ContinueWatching;
        foreach (var pair in _libraryState.Items
                     .Where(pair => pair.Value.Kind == _selectedContent &&
                                    (clearContinueWatching ? IsResumeCandidate(pair.Value) : pair.Value.LastWatchedAt.HasValue))
                     .ToList())
        {
            if (clearContinueWatching)
            {
                pair.Value.PositionMs = 0;
                pair.Value.DurationMs = 0;
            }
            else
            {
                pair.Value.LastWatchedAt = null;
            }

            CleanupLibraryItem(pair.Key, pair.Value);
        }

        SaveLibraryState();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void PlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_mediaPlayer.IsPlaying)
        {
            SaveCurrentPlaybackProgress();
            _mediaPlayer.Pause();
            UpdatePlayPauseIcons(false);
        }
        else
        {
            _mediaPlayer.Play();
            UpdatePlayPauseIcons(true);
        }
    }

    private void UpdatePlayPauseIcons(bool isPlaying)
    {
        var geometry = Avalonia.Media.Geometry.Parse(isPlaying
            ? "M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z"
            : "M4,2 L18,10 L4,18 Z");
        PlayPauseIcon.Data = geometry;
        if (_fullscreenPlayPauseIcon is not null)
            _fullscreenPlayPauseIcon.Data = geometry;
        if (_playerOverlayPlayPauseIcon is not null)
            _playerOverlayPlayPauseIcon.Data = geometry;
    }

    private void VolumeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_mediaPlayer is not null) _mediaPlayer.Volume = (int)e.NewValue;
        if (e.NewValue > 0) _lastAudibleVolume = e.NewValue;
        if (VolumeValueText is not null) VolumeValueText.Text = $"%{e.NewValue:0}";
        if (VolumeWaveIcon is not null) VolumeWaveIcon.IsVisible = e.NewValue > 0;
        if (VolumeMutedIcon is not null) VolumeMutedIcon.IsVisible = e.NewValue <= 0;
        if (_fullscreenVolumeWaveIcon is not null) _fullscreenVolumeWaveIcon.IsVisible = e.NewValue > 0;
        if (_fullscreenVolumeMutedIcon is not null) _fullscreenVolumeMutedIcon.IsVisible = e.NewValue <= 0;
        if (_fullscreenVolumeSlider is not null && !_syncingFullscreenVolume &&
            Math.Abs(_fullscreenVolumeSlider.Value - e.NewValue) > 0.01)
        {
            _syncingFullscreenVolume = true;
            _fullscreenVolumeSlider.Value = e.NewValue;
            _syncingFullscreenVolume = false;
        }
        if (_playerOverlayVolumeSlider is not null && !_syncingPlayerOverlayVolume &&
            Math.Abs(_playerOverlayVolumeSlider.Value - e.NewValue) > 0.01)
        {
            _syncingPlayerOverlayVolume = true;
            _playerOverlayVolumeSlider.Value = e.NewValue;
            _syncingPlayerOverlayVolume = false;
        }
    }

    private void VolumeButton_Click(object? sender, RoutedEventArgs e) =>
        VolumeSlider.Value = VolumeSlider.Value > 0 ? 0 : Math.Max(1, _lastAudibleVolume);

    private void Timeline_PointerPressed(object? sender, PointerPressedEventArgs e) => _isSeeking = true;

    private void Timeline_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var slider = sender as Slider ?? Timeline;
        if (slider.IsEnabled && slider.Maximum > 0)
            _mediaPlayer.Position = (float)Math.Clamp(slider.Value / slider.Maximum, 0, 1);
        _isSeeking = false;
    }

    private void Rewind_Click(object? sender, RoutedEventArgs e) => SeekRelative(-10_000);
    private void Forward_Click(object? sender, RoutedEventArgs e) => SeekRelative(10_000);

    private void SeekRelative(long offsetMilliseconds)
    {
        if (_playingContent == ContentKind.Live || !_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0) return;
        _mediaPlayer.Time = Math.Clamp(_mediaPlayer.Time + offsetMilliseconds, 0, _mediaPlayer.Length);
        UpdateTimeline(_mediaPlayer.Time, _mediaPlayer.Length);
    }

    private void UpdateSeekControls(bool canSeek)
    {
        var isVideo = _playingChannel is { Kind: not ContentKind.Live };
        RewindButton.IsVisible = isVideo;
        ForwardButton.IsVisible = isVideo;
        RewindButton.IsEnabled = canSeek;
        ForwardButton.IsEnabled = canSeek;
        if (_fullscreenRewindButton is not null)
        {
            _fullscreenRewindButton.IsVisible = isVideo;
            _fullscreenRewindButton.IsEnabled = canSeek;
        }
        if (_fullscreenForwardButton is not null)
        {
            _fullscreenForwardButton.IsVisible = isVideo;
            _fullscreenForwardButton.IsEnabled = canSeek;
        }
        if (_playerOverlayRewindButton is not null)
        {
            _playerOverlayRewindButton.IsVisible = isVideo;
            _playerOverlayRewindButton.IsEnabled = canSeek;
        }
        if (_playerOverlayForwardButton is not null)
        {
            _playerOverlayForwardButton.IsVisible = isVideo;
            _playerOverlayForwardButton.IsEnabled = canSeek;
        }
    }

    private void UpdateTimeline(long time, long length) => Dispatcher.UIThread.Post(() =>
    {
        if (_playingContent == ContentKind.Live || length <= 0 || !_mediaPlayer.IsSeekable)
        {
            Timeline.IsEnabled = false;
            TimelinePanel.IsVisible = false;
            if (_fullscreenTimelinePanel is not null) _fullscreenTimelinePanel.IsVisible = false;
            if (_fullscreenTimeline is not null) _fullscreenTimeline.IsEnabled = false;
            if (_playerOverlayTimeline is not null) _playerOverlayTimeline.IsVisible = false;
            if (!_isPlayerFullscreen && _playerOverlay?.IsVisible != true) PlayerLayout.RowDefinitions = new RowDefinitions("*,104");
            TimeLabel.Text = "CANLI";
            if (_fullscreenTimeLabel is not null) _fullscreenTimeLabel.Text = "CANLI";
            if (_playerOverlayTimeLabel is not null) _playerOverlayTimeLabel.Text = "CANLI";
            return;
        }

        TimelinePanel.IsVisible = true;
        if (_fullscreenTimelinePanel is not null) _fullscreenTimelinePanel.IsVisible = true;
        if (!_isPlayerFullscreen && _playerOverlay?.IsVisible != true) PlayerLayout.RowDefinitions = new RowDefinitions("*,124");
        Timeline.Maximum = length;
        Timeline.IsEnabled = _mediaPlayer.IsSeekable;
        if (_fullscreenTimeline is not null)
        {
            _fullscreenTimeline.Maximum = length;
            _fullscreenTimeline.IsEnabled = _mediaPlayer.IsSeekable;
        }
        if (_playerOverlayTimeline is not null)
        {
            _playerOverlayTimeline.IsVisible = true;
            _playerOverlayTimeline.Maximum = length;
            _playerOverlayTimeline.IsEnabled = _mediaPlayer.IsSeekable;
        }
        if (!_isSeeking)
        {
            Timeline.Value = Math.Clamp(time, 0, length);
            if (_fullscreenTimeline is not null) _fullscreenTimeline.Value = Math.Clamp(time, 0, length);
            if (_playerOverlayTimeline is not null) _playerOverlayTimeline.Value = Math.Clamp(time, 0, length);
        }
        TimeLabel.Text = $"{FormatTime(time)} / {FormatTime(length)}";
        if (_fullscreenTimeLabel is not null) _fullscreenTimeLabel.Text = TimeLabel.Text;
        if (_playerOverlayTimeLabel is not null) _playerOverlayTimeLabel.Text = TimeLabel.Text;
    });

    private LibraryItemState? FindLibraryItem(Channel channel) =>
        _libraryState.Items.GetValueOrDefault(channel.Id);

    private LibraryItemState GetOrCreateLibraryItem(Channel channel)
    {
        if (_libraryState.Items.TryGetValue(channel.Id, out var state)) return state;
        state = new LibraryItemState { Kind = channel.Kind };
        _libraryState.Items[channel.Id] = state;
        return state;
    }

    private List<Channel> GetLibraryChannels(LibraryGroupKind groupKind, ContentKind contentKind)
    {
        IEnumerable<Channel> channels = _channels.Where(c => c.Kind == contentKind);
        channels = groupKind switch
        {
            LibraryGroupKind.Favorites => channels
                .Where(c => FindLibraryItem(c)?.IsFavorite == true)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            LibraryGroupKind.Recent => channels
                .Where(c => FindLibraryItem(c)?.LastWatchedAt is not null)
                .OrderByDescending(c => FindLibraryItem(c)!.LastWatchedAt)
                .Take(20),
            LibraryGroupKind.ContinueWatching => channels
                .Where(c => IsResumeCandidate(FindLibraryItem(c)))
                .OrderByDescending(c => FindLibraryItem(c)!.LastWatchedAt),
            _ => []
        };
        return channels.ToList();
    }

    private void TouchPlaybackHistory(Channel channel)
    {
        var state = GetOrCreateLibraryItem(channel);
        state.Kind = channel.Kind;
        state.LastWatchedAt = DateTimeOffset.UtcNow;

        if (channel.Kind == ContentKind.Live)
        {
            var overflow = _libraryState.Items
                .Where(pair => pair.Value.Kind == ContentKind.Live && pair.Value.LastWatchedAt.HasValue)
                .OrderByDescending(pair => pair.Value.LastWatchedAt)
                .Skip(30)
                .ToList();
            foreach (var pair in overflow)
            {
                pair.Value.LastWatchedAt = null;
                CleanupLibraryItem(pair.Key, pair.Value);
            }
        }

        SaveLibraryState();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private long? GetResumePosition(Channel channel)
    {
        if (channel.Kind == ContentKind.Live) return null;
        var state = FindLibraryItem(channel);
        return IsResumeCandidate(state) ? state!.PositionMs : null;
    }

    private static bool IsResumeCandidate(LibraryItemState? state) =>
        state is { PositionMs: >= 10_000, DurationMs: > 0 } &&
        state.PositionMs < state.DurationMs - 10_000 &&
        state.PositionMs / (double)state.DurationMs < 0.95;

    private void TryResumePlayback(long length)
    {
        if (_pendingResumePosition is not { } resumeAt || _playingContent == ContentKind.Live ||
            length <= 0 || !_mediaPlayer.IsSeekable) return;

        _pendingResumePosition = null;
        if (resumeAt >= length - 10_000) return;
        _mediaPlayer.Time = Math.Clamp(resumeAt, 0, length);
        PlaybackStatus.Text = $"{L("Kaldığınız yerden devam ediyor")} · {FormatTime(resumeAt)}";
    }

    private void SaveCurrentPlaybackProgress()
    {
        if (_playingChannel is not { Kind: not ContentKind.Live } channel) return;
        if (!_mediaPlayer.IsPlaying) return;
        var duration = _mediaPlayer.Length;
        var position = _mediaPlayer.Time;
        if (duration <= 0 || position < 0) return;

        var state = GetOrCreateLibraryItem(channel);
        var wasResumeCandidate = IsResumeCandidate(state);
        state.Kind = channel.Kind;
        state.LastWatchedAt = DateTimeOffset.UtcNow;
        if (position >= duration - 10_000 || position / (double)duration >= 0.95)
        {
            state.PositionMs = 0;
            state.DurationMs = 0;
        }
        else if (position >= 10_000)
        {
            state.PositionMs = position;
            state.DurationMs = duration;
        }

        CleanupLibraryItem(channel.Id, state);
        SaveLibraryState();
        if (wasResumeCandidate != IsResumeCandidate(state))
            RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void MarkCurrentPlaybackCompleted()
    {
        if (_playingChannel is not { Kind: not ContentKind.Live } channel) return;
        var state = FindLibraryItem(channel);
        if (state is null) return;
        state.PositionMs = 0;
        state.DurationMs = 0;
        _pendingResumePosition = null;
        CleanupLibraryItem(channel.Id, state);
        SaveLibraryState();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void CleanupLibraryItem(string id, LibraryItemState state)
    {
        if (!state.IsFavorite && !state.LastWatchedAt.HasValue && state.PositionMs <= 0)
            _libraryState.Items.Remove(id);
    }

    private static LibraryState LoadLibraryState()
    {
        try
        {
            if (File.Exists(LibraryStateFilePath))
                return JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(LibraryStateFilePath)) ?? new LibraryState();
        }
        catch { }
        return new LibraryState();
    }

    private void SaveLibraryState()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(LibraryStateFilePath, JsonSerializer.Serialize(_libraryState, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // İzleme geçmişi hataları oynatmayı engellememeli.
        }
    }

    private static string FormatRelativeTime(DateTimeOffset? timestamp)
    {
        if (!timestamp.HasValue) return L("az önce");
        var elapsed = DateTimeOffset.UtcNow - timestamp.Value;
        if (elapsed.TotalMinutes < 1) return L("az önce");
        if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} {L("dk önce")}";
        if (elapsed.TotalDays < 1) return $"{Math.Max(1, (int)elapsed.TotalHours)} {L("sa önce")}";
        return $"{Math.Max(1, (int)elapsed.TotalDays)} {L("gün önce")}";
    }

    private static string FormatTime(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }

    private void CreatePlayerOverlay()
    {
        if (_playerOverlay is not null) return;

        _playerOverlayName = new TextBlock
        {
            Text = NowPlaying.Text,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _playerOverlayStatus = new TextBlock
        {
            Text = PlaybackStatus.Text,
            Foreground = new SolidColorBrush(Color.Parse("#A3ABB8")),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Avalonia.Thickness(0, 5, 0, 0)
        };
        _playerOverlayTimeLabel = new TextBlock
        {
            Text = TimeLabel.Text,
            Foreground = new SolidColorBrush(Color.Parse("#A3ABB8")),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 0, 0, 0)
        };
        _playerOverlayTimeline = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(1, Timeline.Maximum),
            Value = Timeline.Value,
            IsEnabled = Timeline.IsEnabled,
            IsVisible = TimelinePanel.IsVisible
        };
        _playerOverlayTimeline.AddHandler(PointerPressedEvent, Timeline_PointerPressed, RoutingStrategies.Tunnel, true);
        _playerOverlayTimeline.AddHandler(PointerReleasedEvent, Timeline_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

        var timelineRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        timelineRow.Children.Add(_playerOverlayTimeline);
        Grid.SetColumn(_playerOverlayTimeLabel, 1);
        timelineRow.Children.Add(_playerOverlayTimeLabel);

        _playerOverlayPreviousButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M5,4 L8,4 L8,20 L5,20 Z M19,4 L10,12 L19,20 Z"));
        _playerOverlayPreviousButton.Click += PreviousChannel_Click;
        _playerOverlayLastButton = CreatePlayerOverlayButton(CreatePlayerOverlayStrokeIcon("M4,4 L4,9 L9,9 M5.5,7 A8,8 0 1 1 4.8,15"));
        _playerOverlayLastButton.Click += LastChannel_Click;
        _playerOverlayNextButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M16,4 L19,4 L19,20 L16,20 Z M5,4 L14,12 L5,20 Z"));
        _playerOverlayNextButton.Click += NextChannel_Click;
        _playerOverlayRewindButton = CreatePlayerOverlayButton(CreateFullscreenSeekIcon("10", true));
        _playerOverlayRewindButton.Click += Rewind_Click;
        _playerOverlayForwardButton = CreatePlayerOverlayButton(CreateFullscreenSeekIcon("10", false));
        _playerOverlayForwardButton.Click += Forward_Click;
        var epgButton = CreatePlayerOverlayButton(CreatePlayerOverlayStrokeIcon("M4,5 L20,5 L20,20 L4,20 Z M8,2 L8,7 M16,2 L16,7 M4,10 L20,10 M8,14 L10,14 M14,14 L16,14 M8,17 L10,17 M14,17 L16,17"));
        epgButton.Click += ToggleEpgPanel_Click;
        var favoriteButton = CreatePlayerOverlayButton(CreatePlayerOverlayStrokeIcon("M12,2.5 L14.9,8.4 L21.4,9.3 L16.7,13.9 L17.8,20.4 L12,17.3 L6.2,20.4 L7.3,13.9 L2.6,9.3 L9.1,8.4 Z"));
        favoriteButton.Click += FavoriteButton_Click;
        var volumeButton = CreatePlayerOverlayButton(CreatePlayerOverlayVolumeIcon());
        volumeButton.Click += VolumeButton_Click;
        _playerOverlayVolumeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = VolumeSlider.Value,
            Width = 70,
            VerticalAlignment = VerticalAlignment.Center
        };
        _playerOverlayVolumeSlider.ValueChanged += (_, e) =>
        {
            if (_syncingPlayerOverlayVolume) return;
            VolumeSlider.Value = e.NewValue;
        };
        _playerOverlayPlayPauseIcon = CreateFullscreenIcon("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z");
        _playerOverlayPlayPauseIcon.Fill = new SolidColorBrush(Color.Parse("#0F1114"));
        var playPauseButton = CreatePlayerOverlayButton(_playerOverlayPlayPauseIcon, primary: true);
        playPauseButton.Click += PlayPause_Click;
        var fullscreenButton = CreatePlayerOverlayButton(CreatePlayerOverlayStrokeIcon("M4,9 L4,4 L9,4 M15,4 L20,4 L20,9 M20,15 L20,20 L15,20 M9,20 L4,20 L4,15"));
        fullscreenButton.Click += Fullscreen_Click;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_playerOverlayPreviousButton);
        actions.Children.Add(_playerOverlayLastButton);
        actions.Children.Add(_playerOverlayNextButton);
        actions.Children.Add(_playerOverlayRewindButton);
        actions.Children.Add(_playerOverlayForwardButton);
        actions.Children.Add(epgButton);
        actions.Children.Add(favoriteButton);
        actions.Children.Add(volumeButton);
        actions.Children.Add(_playerOverlayVolumeSlider);
        actions.Children.Add(playPauseButton);
        actions.Children.Add(fullscreenButton);

        var liveBadge = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#40EA2E4E")),
            CornerRadius = new Avalonia.CornerRadius(100),
            Padding = new Avalonia.Thickness(8, 4),
            Child = new TextBlock { Text = "CANLI", Foreground = new SolidColorBrush(Color.Parse("#FF97A6")), FontSize = 8, FontWeight = FontWeight.Bold },
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 0, 0, 6)
        };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 245 };
        info.Children.Add(liveBadge);
        info.Children.Add(_playerOverlayName);
        info.Children.Add(_playerOverlayStatus);

        var controlRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        controlRow.Children.Add(info);
        Grid.SetColumn(actions, 1);
        controlRow.Children.Add(actions);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Avalonia.Thickness(20, 10, 20, 12) };
        layout.Children.Add(timelineRow);
        Grid.SetRow(controlRow, 1);
        layout.Children.Add(controlRow);
        var surface = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E60F1114")),
            BorderBrush = new SolidColorBrush(Color.Parse("#50262B33")),
            BorderThickness = new Avalonia.Thickness(1, 1, 1, 0),
            Child = layout
        };
        surface.DoubleTapped += (_, _) => SetPlayerFullscreen(true);

        _playerOverlay = new Window
        {
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = surface
        };
        _playerOverlay.KeyDown += (_, e) => HandlePlayerShortcut(e);
        UpdateChannelNavigationButtons();
        UpdateSeekControls(_playingContent != ContentKind.Live && _mediaPlayer.IsSeekable && _mediaPlayer.Length > 0);
    }

    private static Button CreatePlayerOverlayButton(object content, bool primary = false) => new()
    {
        Content = content,
        Width = primary ? 50 : 40,
        Height = primary ? 50 : 40,
        Padding = new Avalonia.Thickness(0),
        CornerRadius = new Avalonia.CornerRadius(primary ? 25 : 20),
        Background = primary ? new SolidColorBrush(Color.Parse("#F2622E")) : Brushes.Transparent,
        BorderBrush = primary ? new SolidColorBrush(Color.Parse("#FF8A5C")) : Brushes.Transparent,
        BorderThickness = new Avalonia.Thickness(primary ? 1 : 0),
        Foreground = Brushes.White,
        FontWeight = FontWeight.SemiBold,
        FontSize = 11,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Avalonia.Controls.Shapes.Path CreatePlayerOverlayStrokeIcon(string data) => new()
    {
        Data = Avalonia.Media.Geometry.Parse(data),
        Stroke = Brushes.White,
        StrokeThickness = 1.7,
        StrokeLineCap = PenLineCap.Round,
        Fill = null,
        Width = 19,
        Height = 19,
        Stretch = Stretch.Uniform
    };

    private static Viewbox CreatePlayerOverlayVolumeIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M3,9 L7,9 L12,5 L12,19 L7,15 L3,15 Z"),
            Fill = Brushes.White
        });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M15,8.5 C16.3,9.4 17,10.6 17,12 C17,13.4 16.3,14.6 15,15.5 M18,5.5 C20,7.2 21,9.4 21,12 C21,14.6 20,16.8 18,18.5"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            Fill = null
        });
        return new Viewbox { Width = 21, Height = 21, Child = canvas };
    }

    private void ShowPlayerOverlay()
    {
        if (_isPlayerFullscreen || !ContentArea.IsVisible || !PlayerView.IsVisible || _playingChannel is null) return;
        // Native VLC uses its own HWND. A transparent owned Window above that
        // HWND can be sized as the whole application by Windows and darken the
        // interface. Normal mode therefore uses the stable in-layout control bar;
        // the separate overlay remains exclusive to true fullscreen mode.
        _playerOverlay?.Hide();
        PlayerControls.IsVisible = true;
        PlayerLayout.RowDefinitions = TimelinePanel.IsVisible
            ? new RowDefinitions("*,124")
            : new RowDefinitions("*,104");
    }

    private void HidePlayerOverlay(bool restoreControls = false)
    {
        _playerOverlay?.Hide();
        if (!restoreControls) return;
        PlayerControls.IsVisible = true;
        PlayerLayout.RowDefinitions = TimelinePanel.IsVisible ? new RowDefinitions("*,124") : new RowDefinitions("*,104");
    }

    private void UpdatePlayerOverlayText()
    {
        if (_playerOverlayName is not null) _playerOverlayName.Text = NowPlaying.Text;
        if (_playerOverlayStatus is not null) _playerOverlayStatus.Text = PlaybackStatus.Text;
    }

    private void UpdatePlayerOverlayBounds()
    {
        if (_playerOverlay?.IsVisible != true || _isPlayerFullscreen || !PlayerView.IsVisible) return;
        const double overlayHeight = 142;
        if (PlayerView.Bounds.Width < 520 || PlayerView.Bounds.Height < overlayHeight) return;
        var origin = PlayerView.PointToScreen(new Avalonia.Point(0, Math.Max(0, PlayerView.Bounds.Height - overlayHeight)));
        _playerOverlay.Width = PlayerView.Bounds.Width;
        _playerOverlay.Height = overlayHeight;
        _playerOverlay.Position = origin;
    }

    private void Fullscreen_Click(object? sender, RoutedEventArgs e) => SetPlayerFullscreen(!_isPlayerFullscreen);

    private void Window_KeyDown(object? sender, KeyEventArgs e) => HandlePlayerShortcut(e);

    private void HandlePlayerShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isPlayerFullscreen)
        {
            SetPlayerFullscreen(false);
            e.Handled = true;
            return;
        }
        if (e.Source is TextBox) return;
        switch (e.Key)
        {
            case Key.PageUp:
                PlayAdjacentChannel(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                PlayAdjacentChannel(1);
                e.Handled = true;
                break;
            case Key.Back:
                ReturnToLastChannel();
                e.Handled = true;
                break;
            case Key.Left:
                SeekRelative(-10_000);
                e.Handled = _playingContent != ContentKind.Live;
                break;
            case Key.Right:
                SeekRelative(10_000);
                e.Handled = _playingContent != ContentKind.Live;
                break;
            case Key.Space:
                PlayPause_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.M:
                VolumeButton_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F:
                SetPlayerFullscreen(!_isPlayerFullscreen);
                e.Handled = true;
                break;
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e) => HandleFullscreenPointerActivity();

    private void HandleFullscreenPointerActivity()
    {
        if (!_isPlayerFullscreen || _fullscreenControlsVisible) return;

        if (!_fullscreenControlsRevealArmed)
        {
            _fullscreenControlsRevealTimer.Stop();
            _fullscreenControlsRevealTimer.Start();
            return;
        }

        ShowFullscreenControls();
    }

    private void ShowFullscreenControls()
    {
        if (!_isPlayerFullscreen || _fullscreenControlsOverlay is null || _fullscreenControlsVisible) return;
        _fullscreenControlsVisible = true;
        _fullscreenControlsRevealArmed = false;
        if (_fullscreenNowPlaying is not null) _fullscreenNowPlaying.Text = NowPlaying.Text;
        if (_fullscreenVolumeSlider is not null) _fullscreenVolumeSlider.Value = VolumeSlider.Value;
        UpdatePlayPauseIcons(_mediaPlayer.IsPlaying);
        UpdateFullscreenControlsOverlayBounds();
        _fullscreenControlsOverlay.Show(this);
        _fullscreenControlsTimer.Start();
    }

    private void HideFullscreenControls()
    {
        _fullscreenControlsTimer.Stop();
        if (!_isPlayerFullscreen || !_fullscreenControlsVisible) return;
        _fullscreenControlsVisible = false;
        _fullscreenControlsOverlay?.Hide();
        _fullscreenControlsRevealArmed = false;
        _fullscreenControlsRevealTimer.Stop();
        _fullscreenControlsRevealTimer.Start();
    }

    private void CreateFullscreenControlsOverlay()
    {
        if (_fullscreenControlsOverlay is not null) return;

        _fullscreenTimeline = new Slider { Minimum = 0, Maximum = 1, IsEnabled = false };
        _fullscreenTimeline.AddHandler(PointerPressedEvent, Timeline_PointerPressed, RoutingStrategies.Tunnel, true);
        _fullscreenTimeline.AddHandler(PointerReleasedEvent, Timeline_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        _fullscreenTimeLabel = new TextBlock
        {
            Text = TimeLabel.Text,
            Foreground = new SolidColorBrush(Color.Parse("#A3ABB8")),
            FontSize = 10,
            Margin = new Avalonia.Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _fullscreenTimelinePanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            IsVisible = TimelinePanel.IsVisible
        };
        _fullscreenTimelinePanel.Children.Add(_fullscreenTimeline);
        Grid.SetColumn(_fullscreenTimeLabel, 1);
        _fullscreenTimelinePanel.Children.Add(_fullscreenTimeLabel);

        _fullscreenNowPlaying = new TextBlock
        {
            Text = NowPlaying.Text,
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var volumeButton = CreateFullscreenVolumeButton();
        volumeButton.Click += (_, _) =>
        {
            if (_fullscreenVolumeSlider is null) return;
            _fullscreenVolumeSlider.Value = _fullscreenVolumeSlider.Value > 0 ? 0 : Math.Max(1, _lastAudibleVolume);
        };
        _fullscreenVolumeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = VolumeSlider.Value,
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center
        };
        _fullscreenVolumeSlider.ValueChanged += (_, e) =>
        {
            if (_syncingFullscreenVolume) return;
            VolumeSlider.Value = e.NewValue;
        };

        _fullscreenPlayPauseIcon = CreateFullscreenIcon("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z");
        _fullscreenPlayPauseIcon.Fill = new SolidColorBrush(Color.Parse("#0F1114"));
        var playPauseButton = CreateFullscreenActionButton(_fullscreenPlayPauseIcon, true);
        playPauseButton.Click += PlayPause_Click;
        _fullscreenPreviousChannelButton = CreateFullscreenActionButton(CreateFullscreenIcon("M17,4 L7,10 L17,16 Z M4,4 L4,16"));
        _fullscreenPreviousChannelButton.Click += PreviousChannel_Click;
        var lastChannelIcon = CreateFullscreenIcon("M6,7 L2,11 L6,15 M3,11 L12,11 C16,11 18,13 18,17");
        lastChannelIcon.Fill = null;
        lastChannelIcon.Stroke = Brushes.White;
        lastChannelIcon.StrokeThickness = 1.8;
        _fullscreenLastChannelButton = CreateFullscreenActionButton(lastChannelIcon);
        _fullscreenLastChannelButton.Click += LastChannel_Click;
        _fullscreenNextChannelButton = CreateFullscreenActionButton(CreateFullscreenIcon("M3,4 L13,10 L3,16 Z M16,4 L16,16"));
        _fullscreenNextChannelButton.Click += NextChannel_Click;
        _fullscreenRewindButton = CreateFullscreenActionButton(CreateFullscreenSeekIcon("10", rewind: true));
        _fullscreenRewindButton.Click += Rewind_Click;
        _fullscreenForwardButton = CreateFullscreenActionButton(CreateFullscreenSeekIcon("10", rewind: false));
        _fullscreenForwardButton.Click += Forward_Click;
        UpdateChannelNavigationButtons();
        UpdateSeekControls(_playingContent != ContentKind.Live && _mediaPlayer.IsSeekable && _mediaPlayer.Length > 0);
        var exitIcon = CreateFullscreenIcon("M7,2 L2,2 L2,7 M18,7 L18,2 L13,2 M13,18 L18,18 L18,13 M2,13 L2,18 L7,18");
        exitIcon.Fill = null;
        exitIcon.Stroke = Brushes.White;
        exitIcon.StrokeThickness = 2;
        var exitButton = CreateFullscreenActionButton(exitIcon);
        exitButton.Click += (_, _) => SetPlayerFullscreen(false);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(_fullscreenPreviousChannelButton);
        actions.Children.Add(_fullscreenLastChannelButton);
        actions.Children.Add(_fullscreenNextChannelButton);
        actions.Children.Add(_fullscreenRewindButton);
        actions.Children.Add(_fullscreenForwardButton);
        actions.Children.Add(volumeButton);
        actions.Children.Add(_fullscreenVolumeSlider);
        actions.Children.Add(playPauseButton);
        actions.Children.Add(exitButton);

        var controlRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };
        controlRow.Children.Add(_fullscreenNowPlaying);
        Grid.SetColumn(actions, 1);
        controlRow.Children.Add(actions);

        var overlayLayout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Avalonia.Thickness(18, 9, 18, 11)
        };
        overlayLayout.Children.Add(_fullscreenTimelinePanel);
        Grid.SetRow(controlRow, 1);
        overlayLayout.Children.Add(controlRow);

        _fullscreenControlsOverlaySurface = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EE0F1114")),
            BorderBrush = new SolidColorBrush(Color.Parse("#343B45")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(18),
            Child = overlayLayout
        };

        _fullscreenControlsOverlay = new Window
        {
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = _fullscreenControlsOverlaySurface
        };
        _fullscreenControlsOverlay.PointerMoved += (_, _) => HandleFullscreenPointerActivity();
        _fullscreenControlsOverlay.KeyDown += (_, e) =>
        {
            HandlePlayerShortcut(e);
        };
    }

    private static Avalonia.Controls.Shapes.Path CreateFullscreenIcon(string data) => new()
    {
        Data = Avalonia.Media.Geometry.Parse(data),
        Fill = Brushes.White,
        Width = 20,
        Height = 20,
        Stretch = Stretch.Uniform
    };

    private static Grid CreateFullscreenSeekIcon(string seconds, bool rewind)
    {
        var grid = new Grid { Width = 24, Height = 24 };
        grid.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse(rewind
                ? "M8,6 L4,6 L4,2 M4,6 C6,3 10,2 13,3 C18,4 20,10 18,15 C16,20 9,21 5,17"
                : "M16,6 L20,6 L20,2 M20,6 C18,3 14,2 11,3 C6,4 4,10 6,15 C8,20 15,21 19,17"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            Fill = null
        });
        grid.Children.Add(new TextBlock
        {
            Text = seconds,
            Foreground = Brushes.White,
            FontSize = 8,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 3, 0, 0)
        });
        return grid;
    }

    private static Button CreateFullscreenActionButton(Control content, bool primary = false) => new()
    {
        Width = 46,
        Height = 46,
        Padding = new Avalonia.Thickness(12),
        CornerRadius = new Avalonia.CornerRadius(23),
        BorderThickness = new Avalonia.Thickness(0),
        Background = new SolidColorBrush(Color.Parse(primary ? "#F2622E" : "#1C2026")),
        Content = content,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private Button CreateFullscreenVolumeButton()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M3,9 L7,9 L12,5 L12,19 L7,15 L3,15 Z"),
            Fill = Brushes.White
        });
        _fullscreenVolumeWaveIcon = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M15,8.5 C16.3,9.4 17,10.6 17,12 C17,13.4 16.3,14.6 15,15.5 M18,5.5 C20,7.2 21,9.4 21,12 C21,14.6 20,16.8 18,18.5"),
            Stroke = Brushes.White,
            StrokeThickness = 1.8,
            IsVisible = VolumeSlider.Value > 0
        };
        _fullscreenVolumeMutedIcon = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M15,9 L21,15 M21,9 L15,15"),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsVisible = VolumeSlider.Value <= 0
        };
        canvas.Children.Add(_fullscreenVolumeWaveIcon);
        canvas.Children.Add(_fullscreenVolumeMutedIcon);
        return CreateFullscreenActionButton(new Viewbox { Width = 22, Height = 22, Child = canvas });
    }

    private void UpdateFullscreenControlsOverlayBounds()
    {
        if (_fullscreenControlsOverlay is null) return;

        var overlayHeight = _fullscreenTimelinePanel?.IsVisible == true ? 112d : 82d;
        var overlayWidth = Math.Min(Math.Max(360d, Bounds.Width - 48d), 1180d);
        var left = Math.Max(0d, (Bounds.Width - overlayWidth) / 2d);
        var top = Math.Max(0d, Bounds.Height - overlayHeight - 24d);

        _fullscreenControlsOverlay.Width = overlayWidth;
        _fullscreenControlsOverlay.Height = overlayHeight;
        var scale = RenderScaling;
        _fullscreenControlsOverlay.Position = new Avalonia.PixelPoint(
            Position.X + (int)Math.Round(left * scale),
            Position.Y + (int)Math.Round(top * scale));
    }

    private void DestroyFullscreenControlsOverlay()
    {
        _fullscreenControlsTimer.Stop();
        _fullscreenControlsRevealTimer.Stop();
        _fullscreenControlsVisible = false;
        _fullscreenControlsRevealArmed = true;

        _fullscreenControlsOverlay?.Hide();
        PlayerControls.IsVisible = true;
    }

    private void SetPlayerFullscreen(bool fullscreen)
    {
        if (_isPlayerFullscreen == fullscreen) return;
        _isPlayerFullscreen = fullscreen;

        if (fullscreen)
        {
            HidePlayerOverlay();
            _epgPanelWasVisibleBeforeFullscreen = EpgPanel.IsVisible;
            SetEpgPanelVisibility(false);
            _previousWindowState = WindowState;
            Sidebar.IsVisible = false;
            HeaderPanel.IsVisible = false;
            ChannelPanel.IsVisible = false;
            RootGrid.ColumnDefinitions = new ColumnDefinitions("0,*");
            ContentArea.Margin = new Avalonia.Thickness(0);
            ContentArea.RowDefinitions = new RowDefinitions("Auto,*");
            ContentBody.ColumnDefinitions = new ColumnDefinitions("0,*");
            ContentBody.ColumnSpacing = 0;
            Grid.SetColumn(PlayerPanel, 1);
            Grid.SetColumnSpan(PlayerPanel, 2);
            PlayerPanel.CornerRadius = new Avalonia.CornerRadius(0);
            PlayerPanel.BorderThickness = new Avalonia.Thickness(0);
            PlayerLayout.RowDefinitions = new RowDefinitions("*,0");
            PlayerControls.IsVisible = false;
            CreateFullscreenControlsOverlay();
            _fullscreenControlsTimer.Stop();
            _fullscreenControlsRevealTimer.Stop();
            _fullscreenControlsVisible = false;
            _fullscreenControlsRevealArmed = true;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            DestroyFullscreenControlsOverlay();
            WindowState = _previousWindowState == WindowState.FullScreen ? WindowState.Normal : _previousWindowState;
            RootGrid.ColumnDefinitions = new ColumnDefinitions("350,*");
            ContentArea.Margin = new Avalonia.Thickness(0);
            ContentArea.RowDefinitions = new RowDefinitions("Auto,*");
            ContentBody.ColumnDefinitions = new ColumnDefinitions("430,*");
            ContentBody.ColumnSpacing = 0;
            Grid.SetColumn(PlayerPanel, 1);
            Grid.SetColumnSpan(PlayerPanel, 1);
            PlayerPanel.CornerRadius = new Avalonia.CornerRadius(0);
            PlayerPanel.BorderThickness = new Avalonia.Thickness(0);
            PlayerLayout.RowDefinitions = TimelinePanel.IsVisible ? new RowDefinitions("*,124") : new RowDefinitions("*,104");
            PlayerControls.IsVisible = true;
            if (_epgPanelWasVisibleBeforeFullscreen && _playingChannel is { Kind: ContentKind.Live } channel)
                UpdateEpgPanel(channel, true);
            _fullscreenControlsTimer.Stop();
            Sidebar.IsVisible = true;
            HeaderPanel.IsVisible = true;
            ChannelPanel.IsVisible = true;
            PlaybackStatus.Text = _mediaPlayer.IsPlaying && _playingChannel is { } playingChannel
                ? (_playingContent == ContentKind.Live ? GetNowPlayingStatus(playingChannel) : L("Oynatılıyor"))
                : L("Hazır");
            if (_playingChannel is not null) Dispatcher.UIThread.Post(ShowPlayerOverlay);
        }
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() =>
    {
        PlaybackStatus.Text = text;
        UpdatePlayerOverlayText();
    });

    private static string AppVersion => (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0)).ToString(3);

    private static string L(string text) => Localization.T(text);

    private void ShowSettings_Click(object? sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettingsFromHome_Click(object? sender, RoutedEventArgs e) => ShowSettings();

    private void ShowAbout_Click(object? sender, RoutedEventArgs e) => ShowSettings("about");

    private void ShowAboutFromHome_Click(object? sender, RoutedEventArgs e) => ShowSettings("about");

    private void HideSettings_Click(object? sender, RoutedEventArgs e) => HideSettings();

    private void ShowSettings(string section = "playlists")
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        StopPlaybackForNavigation();
        RefreshPlaylistSettingsView();
        HomePage.IsVisible = false;
        ContentArea.IsVisible = false;
        Sidebar.IsVisible = false;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("0,*");
        ShowSettingsSection(section);
        SettingsPage.IsVisible = true;
    }

    private void HideSettings()
    {
        SettingsPage.IsVisible = false;
        OpenLibrary(_selectedContent);
    }

    private void SettingsTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section }) ShowSettingsSection(section);
    }

    // Ayarlar bölümleri tek sayfada durur; yalnızca seçili olan görünür.
    private void ShowSettingsSection(string section)
    {
        _settingsSection = section;
        SetSettingsTab(SettingsTabPlaylists, SettingsTabPlaylistsIcon, SettingsSectionPlaylists, section == "playlists");
        SetSettingsTab(SettingsTabGeneral, SettingsTabGeneralIcon, SettingsSectionGeneral, section == "general");
        SetSettingsTab(SettingsTabUpdate, SettingsTabUpdateIcon, SettingsSectionUpdate, section == "update");
        SetSettingsTab(SettingsTabPrivacy, SettingsTabPrivacyIcon, SettingsSectionPrivacy, section == "privacy");
        SetSettingsTab(SettingsTabAbout, SettingsTabAboutIcon, SettingsSectionAbout, section == "about");

        SettingsSubtitleText.Text = section switch
        {
            "general" => L("Genel"),
            "update" => L("Güncelleme"),
            "privacy" => L("Gizlilik"),
            "about" => L("Hakkında"),
            _ => L("Oynatma Listeleri")
        };

        if (section == "update") RefreshUpdateSection();
        if (section != "privacy") return;
        SettingsDataPathText.Text = SettingsDirectory;
        SettingsPrivacyStatus.IsVisible = false;
    }

    private static void SetSettingsTab(Button tab, Avalonia.Controls.Shapes.Path icon, Control section, bool active)
    {
        tab.Classes.Set("active", active);
        icon.Stroke = new SolidColorBrush(Color.Parse(active ? "#FF8A5C" : "#A3ABB8"));
        section.IsVisible = active;
    }

    private void SetLanguage_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string language }) Localization.SetLanguage(language);
    }

    // Dil değişince XAML'deki sabit metinler ve kod içinde üretilen metinler yeniden yazılır.
    private void ApplyLanguage()
    {
        Localization.Apply(this);

        var english = Localization.Language == Localization.English;
        LanguageTurkishButton.Classes.Set("active", !english);
        LanguageEnglishButton.Classes.Set("active", english);
        LanguageTurkishCheck.IsVisible = !english;
        LanguageEnglishCheck.IsVisible = english;

        AboutVersionText.Text = $"{L("Sürüm")} {AppVersion}";
        HomeVersionText.Text = $"© {DateTime.Now.Year} Berk Güçlükol · {L("Sürüm")} {AppVersion}";
        ShowSettingsSection(_settingsSection);

        UpdateHomeDashboard();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void OpenDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            Process.Start(new ProcessStartInfo(SettingsDirectory) { UseShellExecute = true });
        }
        catch
        {
            // Klasör açılamazsa yol ayarlar sayfasında yazılı kalır.
        }
    }

    private void ClearWatchData_Click(object? sender, RoutedEventArgs e)
    {
        _libraryState = new LibraryState();
        SaveLibraryState();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
        UpdateHomeDashboard();
        ShowPrivacyStatus(L("İzleme geçmişi, favoriler ve kaldığınız konumlar silindi."));
    }

    private void ClearCacheData_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(PlaylistCacheDirectory)) Directory.Delete(PlaylistCacheDirectory, true);
        }
        catch
        {
            // Kullanımdaki bir önbellek dosyası silinemezse sonraki açılışta üzerine yazılır.
        }

        ShowPrivacyStatus(L("Önbellek temizlendi. Listeler bir sonraki açılışta yeniden indirilir."));
    }

    private void ShowPrivacyStatus(string text)
    {
        SettingsPrivacyStatus.Text = text;
        SettingsPrivacyStatus.IsVisible = true;
    }

    private void OpenGithub_Click(object? sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://github.com/berkguclukol/bg-iptv-player");

    private void OpenHomepage_Click(object? sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://bgiptvplayer.guclukol.net/");

    private void OpenPrivacy_Click(object? sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://bgiptvplayer.guclukol.net/privacy.html");

    private static void OpenExternalUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private async void ActivatePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: PlaylistEntry entry }) return;
        foreach (var playlist in _playlists) playlist.IsActive = playlist.Id == entry.Id;
        SavePlaylistSettings();
        RefreshPlaylistSettingsView();
        HideSettings();
        await LoadPlaylistEntryAsync(entry);
    }

    private async void RefreshSavedPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: PlaylistEntry entry }) return;
        if (entry.IsActive)
        {
            await LoadPlaylistEntryAsync(entry, forceRefresh: entry.IsRemote);
            HideSettings();
            return;
        }

        try
        {
            await ResolvePlaylistPathAsync(entry, forceRefresh: entry.IsRemote);
            if (!string.IsNullOrWhiteSpace(entry.EpgUrl)) await ResolveEpgPathAsync(entry, forceRefresh: true);
            PlaybackStatus.Text = $"{entry.Name} yenilendi";
        }
        catch (Exception ex) { PlaybackStatus.Text = $"Liste yenilenemedi: {ex.Message}"; }
    }

    private async void RemovePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: PlaylistEntry entry }) return;
        var wasActive = entry.IsActive;
        _playlists.RemoveAll(p => p.Id == entry.Id);
        var cachePath = Path.Combine(PlaylistCacheDirectory, $"{entry.Id}.m3u");
        if (File.Exists(cachePath)) File.Delete(cachePath);
        var epgCachePath = Path.Combine(PlaylistCacheDirectory, $"{entry.Id}.xml");
        if (File.Exists(epgCachePath)) File.Delete(epgCachePath);
        if (wasActive && _playlists.Count > 0) _playlists[0].IsActive = true;
        SavePlaylistSettings();
        RefreshPlaylistSettingsView();

        if (!wasActive) return;
        var next = _playlists.FirstOrDefault(p => p.IsActive);
        if (next is not null) await LoadPlaylistEntryAsync(next);
        else
        {
            _channels = [];
            RefreshGroups();
            PlaybackStatus.Text = "Oynatma listesi ekleyin";
        }
    }

    private PlaylistEntry AddOrActivatePlaylist(
        string path,
        string? name = null,
        string? epgUrl = null,
        PlaylistSourceKind sourceKind = PlaylistSourceKind.Standard,
        string? displayServer = null)
    {
        var isRemote = Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                       (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isRemote) path = Path.GetFullPath(path);
        var entry = _playlists.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        foreach (var playlist in _playlists) playlist.IsActive = false;
        if (entry is null)
        {
            entry = new PlaylistEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = !string.IsNullOrWhiteSpace(name) ? name : Path.GetFileNameWithoutExtension(path),
                Path = path,
                EpgUrl = epgUrl,
                SourceKind = sourceKind,
                DisplayServer = displayServer,
                IsActive = true
            };
            _playlists.Add(entry);
        }
        else
        {
            entry.IsActive = true;
            if (!string.IsNullOrWhiteSpace(name)) entry.Name = name;
            if (!string.IsNullOrWhiteSpace(epgUrl)) entry.EpgUrl = epgUrl;
            if (sourceKind != PlaylistSourceKind.Standard) entry.SourceKind = sourceKind;
            if (!string.IsNullOrWhiteSpace(displayServer)) entry.DisplayServer = displayServer;
        }
        SavePlaylistSettings();
        RefreshPlaylistSettingsView();
        return entry;
    }

    private void RefreshPlaylistSettingsView()
    {
        if (PlaylistSettingsList is null) return;
        PlaylistSettingsList.ItemsSource = null;
        PlaylistSettingsList.ItemsSource = _playlists.ToList();
        PlaylistCountText.Text = $"{_playlists.Count} liste";
    }

    private static List<PlaylistEntry> LoadPlaylistSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
                return JsonSerializer.Deserialize<List<PlaylistEntry>>(File.ReadAllText(SettingsFilePath)) ?? [];

            if (File.Exists(LegacyPlaylistSettingPath))
            {
                var path = File.ReadAllText(LegacyPlaylistSettingPath).Trim();
                if (File.Exists(path))
                    return [new PlaylistEntry { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileNameWithoutExtension(path), Path = path, IsActive = true }];
            }
        }
        catch { }
        return
        [
            new PlaylistEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = DefaultPlaylistName,
                Path = DefaultPlaylistUrl,
                IsActive = true
            }
        ];
    }

    private void SavePlaylistSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(_playlists, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings must never block playback.
        }
    }
}

public enum ContentKind { Live, Movie, Series }
public enum SeriesBrowserLevel { Shows, Seasons, Episodes }
public enum MediaBrowserItemKind { Channel, Series, Season, Episode }
public enum LibraryGroupKind { Regular, Favorites, Recent, ContinueWatching }
public enum PlaylistSourceKind { Standard, Xtream }

public readonly record struct XtreamCredentials(string Server, string Username, string Password);

public sealed record Channel(string Name, string Url, string Group, string? LogoUrl, ContentKind Kind, string? TvgId)
{
    public string Id { get; } = CreateStableId(Url);
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0])).ToUpperInvariant();
    public string Badge => Localization.T(Kind switch { ContentKind.Movie => "FİLM", ContentKind.Series => "DİZİ", _ => "CANLI" });
    public SeriesMetadata Series { get; } = SeriesMetadata.Parse(Name);

    private static string CreateStableId(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash.ToString("X16");
    }
}
public sealed record ChannelGroup(string Name, int Count, LibraryGroupKind Kind);

public sealed record SeriesMetadata(string Title, int? Season, int? Episode)
{
    private static readonly Regex CompactPattern = new(
        @"^(?<title>.*?)(?:\s*[-|:]\s*|\s+)S\s*(?<season>\d{1,3})\s*E\s*(?<episode>\d{1,4})(?:\b|\D.*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex XPattern = new(
        @"^(?<title>.*?)(?:\s*[-|:]\s*|\s+)(?<season>\d{1,3})\s*X\s*(?<episode>\d{1,4})(?:\b|\D.*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TurkishPattern = new(
        @"^(?<title>.*?)(?:\s*[-|:]\s*|\s+)SEZON\s*(?<season>\d{1,3}).*?B[ÖO]L[ÜU]M\s*(?<episode>\d{1,4})(?:\b|\D.*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static SeriesMetadata Parse(string name)
    {
        var match = CompactPattern.Match(name);
        if (!match.Success) match = XPattern.Match(name);
        if (!match.Success) match = TurkishPattern.Match(name);
        if (!match.Success) return new SeriesMetadata(CleanTitle(name), null, null);

        var title = CleanTitle(match.Groups["title"].Value);
        if (title.Length == 0) return new SeriesMetadata(CleanTitle(name), null, null);

        return new SeriesMetadata(
            title,
            int.Parse(match.Groups["season"].Value),
            int.Parse(match.Groups["episode"].Value));
    }

    private static string CleanTitle(string title) =>
        Regex.Replace(title.Replace('_', ' ').Replace('.', ' '), @"\s+", " ").Trim(' ', '-', '|', ':');
}

public sealed class MediaBrowserItem
{
    public MediaBrowserItemKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Badge { get; init; } = "";
    public string? LogoUrl { get; init; }
    public Channel? Channel { get; init; }
    public string? SeriesTitle { get; init; }
    public int? Season { get; init; }
    public bool CanRemoveFromHistory { get; init; }
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0])).ToUpperInvariant();

    public static MediaBrowserItem FromChannel(
        Channel channel,
        string? subtitle = null,
        string? badge = null,
        bool canRemoveFromHistory = false) => new()
    {
        Kind = MediaBrowserItemKind.Channel,
        Name = channel.Name,
        Subtitle = subtitle ?? channel.Group,
        Badge = badge ?? channel.Badge,
        LogoUrl = channel.LogoUrl,
        Channel = channel,
        CanRemoveFromHistory = canRemoveFromHistory
    };

    public static MediaBrowserItem FromEpisode(
        Channel channel,
        string? subtitle = null,
        string? badge = null,
        bool canRemoveFromHistory = false) => new()
    {
        Kind = MediaBrowserItemKind.Episode,
        CanRemoveFromHistory = canRemoveFromHistory,
        Name = channel.Name,
        Subtitle = subtitle ?? (channel.Series.Season.HasValue ? $"{Localization.T("Sezon")} {channel.Series.Season}" : Localization.T("Diğer bölümler")),
        Badge = badge ?? (channel.Series.Episode.HasValue ? $"{Localization.T("BÖLÜM")} {channel.Series.Episode}" : Localization.T("OYNAT")),
        LogoUrl = channel.LogoUrl,
        Channel = channel,
        SeriesTitle = channel.Series.Title,
        Season = channel.Series.Season
    };

    public static MediaBrowserItem FromSeries(IGrouping<string, Channel> group)
    {
        var first = group.First();
        return new MediaBrowserItem
        {
            Kind = MediaBrowserItemKind.Series,
            Name = group.Key,
            Subtitle = $"{group.Count():N0} {Localization.T("bölüm")}",
            Badge = Localization.T("DİZİ  ›"),
            LogoUrl = group.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.LogoUrl))?.LogoUrl ?? first.LogoUrl,
            SeriesTitle = group.Key
        };
    }

    public static MediaBrowserItem FromSeason(string seriesTitle, int? season, int episodeCount, string? logoUrl) => new()
    {
        Kind = MediaBrowserItemKind.Season,
        Name = season.HasValue ? $"{Localization.T("Sezon")} {season}" : Localization.T("Diğer Bölümler"),
        Subtitle = $"{episodeCount:N0} {Localization.T("bölüm")}",
        Badge = Localization.T("AÇ  ›"),
        LogoUrl = logoUrl,
        SeriesTitle = seriesTitle,
        Season = season
    };
}

public sealed class PlaylistEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? EpgUrl { get; set; }
    public PlaylistSourceKind SourceKind { get; set; }
    public string? DisplayServer { get; set; }
    public string? XtreamServer { get; set; }
    public string? XtreamUsername { get; set; }
    public string? XtreamPassword { get; set; }
    public bool IsActive { get; set; }
    public bool IsRemote => Uri.TryCreate(Path, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    public bool IsXtream => SourceKind == PlaylistSourceKind.Xtream;
    public string SourceTypeText => IsXtream ? "XTREAM" : IsRemote ? "URL" : "DOSYA";
    public string DisplayPath => IsXtream ? $"{DisplayServer ?? Localization.T("Xtream sunucusu")} · {Localization.T("kullanıcı bilgileri gizli")}" : Path;
    public string ActiveText => Localization.T(IsActive ? "✓ AKTİF" : "Etkinleştir");
}

public sealed record EpgProgramme(string Title, string? Description, string? Category, DateTimeOffset Start, DateTimeOffset Stop);

public sealed class EpgSchedule
{
    public EpgProgramme? Current { get; set; }
    public EpgProgramme? Next { get; set; }
    public List<EpgProgramme> Programs { get; } = [];
}

public sealed class EpgSnapshot
{
    public Dictionary<string, EpgSchedule> Schedules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ChannelIdByName { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EpgProgrammeItem
{
    public EpgProgrammeItem(EpgProgramme programme, DateTimeOffset now)
    {
        Title = programme.Title;
        Description = programme.Description;
        Category = programme.Category;
        TimeText = $"{programme.Start:HH:mm}\n{programme.Stop:HH:mm}";
        IsCurrent = programme.Start <= now && programme.Stop > now;
    }

    public string Title { get; }
    public string? Description { get; }
    public string? Category { get; }
    public string TimeText { get; }
    public bool IsCurrent { get; }
    public string StatusText => Localization.T("ŞİMDİ YAYINDA");
    public IBrush Background => new SolidColorBrush(Color.Parse(IsCurrent ? "#2D333C" : "#171A1F"));
    public IBrush BorderBrush => new SolidColorBrush(Color.Parse(IsCurrent ? "#F2622E" : "#262B33"));
    public IBrush TimeForeground => new SolidColorBrush(Color.Parse(IsCurrent ? "#FF8A5C" : "#8791A0"));
}

public sealed class LibraryState
{
    public Dictionary<string, LibraryItemState> Items { get; set; } = [];
}

public sealed class LibraryItemState
{
    public ContentKind Kind { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastWatchedAt { get; set; }
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
}
