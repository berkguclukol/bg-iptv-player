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
using Avalonia.Styling;
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
    private Border? _fullscreenVolumeFill;
    private Avalonia.Controls.Shapes.Ellipse? _fullscreenVolumeThumb;
    private TextBlock? _fullscreenVolumeText;
    private TextBlock? _subtitleDelayText;
    private StackPanel? _fullscreenTrackPanel;
    private const double FullscreenVolumeTrackWidth = 104;
    private const double FullscreenVolumeThumbSize = 13;
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
    private int _loadingDepth;
    private bool _isGlobalSearch;
    private string _settingsSection = "playlists";
    private int _playbackRetryCount;
    private bool _epgSearchAllChannels;
    private bool _parentalUnlocked;
    private string _selectedCollection = "";
    private bool _isMiniPlayer;
    private PixelPoint _miniRestorePosition;
    private Size _miniRestoreSize;
    private WindowState _miniRestoreState;
    private string? _pendingSubtitleFile;
    private readonly DispatcherTimer _backgroundRefreshTimer;
    private bool _isBackgroundRefreshing;

    public MainWindow()
    {
        Localization.Initialize();
        InitializeComponent();
        ApplyTheme();
        UpdateHomeDashboard();
        Localization.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
        ApplyAccentColor();
        UpdateSortMenu();
        TmdbKeyBox.Text = Preferences.Current.TmdbApiKey ?? "";
        RefreshParentalView();
        RefreshCollectionsView();
        ResumeLastChannelSwitch.IsChecked = Preferences.Current.ResumeLastChannel;
        AutoNextEpisodeSwitch.IsChecked = Preferences.Current.AutoPlayNextEpisode;
        BackgroundRefreshSwitch.IsChecked = Preferences.Current.BackgroundRefresh;
        UpdatePlaybackOptionChips();
        RefreshHiddenGroupsView();
        if (TrackButton.Flyout is Flyout trackFlyout) trackFlyout.Opening += (_, _) => BuildTrackMenu(TrackFlyoutPanel);
        ChannelList.AddHandler(PointerPressedEvent, ChannelList_PointerPressed, RoutingStrategies.Tunnel, true);
        EpgProgrammeList.AddHandler(PointerPressedEvent, EpgProgrammeList_PointerPressed, RoutingStrategies.Tunnel, true);
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
        _backgroundRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _backgroundRefreshTimer.Tick += (_, _) => RefreshPlaylistInBackground();
        _backgroundRefreshTimer.Start();
        _playbackProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _playbackProgressTimer.Tick += (_, _) =>
        {
            SaveCurrentPlaybackProgress();
            AccumulateWatchTime();
            UpdateMediaInfoBadge();
        };
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
            _playbackRetryCount = 0;
            TrackButton.IsEnabled = true;
            ApplyPendingSubtitleFile();
            if (!_historyRecordedForCurrentPlayback && _playingChannel is { } playingChannel)
            {
                _historyRecordedForCurrentPlayback = true;
                TouchPlaybackHistory(playingChannel);
            }
            TryResumePlayback(_mediaPlayer.Length);
            UpdateMediaInfoBadge();
        });
        _mediaPlayer.Paused += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayPauseIcons(false));
        _mediaPlayer.Stopped += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayPauseIcons(false));
        _mediaPlayer.EncounteredError += (_, _) => Dispatcher.UIThread.Post(HandlePlaybackError);
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
            Catalog.Track(_channels);
            TryResumeLastChannel();
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
            .Where(g => !IsGroupHidden(g.Key) && !IsGroupLocked(g.Key))
            .Select(g => new ChannelGroup(g.Key, g.Count(), LibraryGroupKind.Regular))
            .OrderBy(g => ContainsAdult(g.Name) ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var groups = new List<ChannelGroup>();
        var favoriteCount = GetLibraryChannels(LibraryGroupKind.Favorites, _selectedContent).Count;
        if (favoriteCount > 0) groups.Add(new ChannelGroup("★ Favoriler", favoriteCount, LibraryGroupKind.Favorites));

        var addedChannels = GetLibraryChannels(LibraryGroupKind.RecentlyAdded, _selectedContent);
        if (addedChannels.Count > 0)
            groups.Add(new ChannelGroup(L("✚ Son Eklenenler"), addedChannels.Count, LibraryGroupKind.RecentlyAdded));

        var recentChannels = GetLibraryChannels(LibraryGroupKind.Recent, _selectedContent);
        if (recentChannels.Count > 0)
            groups.Add(new ChannelGroup(L("◷ Son İzlenenler"), recentChannels.Count, LibraryGroupKind.Recent));

        if (_selectedContent != ContentKind.Live)
        {
            var continueWatchingChannels = GetLibraryChannels(LibraryGroupKind.ContinueWatching, _selectedContent);
            if (continueWatchingChannels.Count > 0)
                groups.Add(new ChannelGroup(L("▶ İzlemeye Devam Et"), continueWatchingChannels.Count, LibraryGroupKind.ContinueWatching));
        }

        foreach (var name in Collections.Names)
        {
            var ids = Collections.Ids(name);
            var count = sectionChannels.Count(ch => ids.Contains(ch.Id, StringComparer.Ordinal));
            if (count > 0) groups.Add(new ChannelGroup($"◆ {name}", count, LibraryGroupKind.Collection, name));
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

    // Klavye ile gezerken secim degismesi oynatmayi baslatmaz; Enter gerekir.
    private void ChannelList_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (ChannelList.SelectedItem is MediaBrowserItem selected) OpenBrowserItem(selected);
                e.Handled = true;
                break;
            case Key.Left:
                GroupList.Focus();
                e.Handled = true;
                break;
        }
    }

    private void GroupList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Right && e.Key != Key.Enter) return;
        ChannelList.Focus();
        e.Handled = true;
    }

    private void GroupList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressGroupSelection) return;
        if (GroupList.SelectedItem is not ChannelGroup group) return;
        // Gruplar arasında gezinmek oynatmayı kesmez; yalnızca bir içerik seçilince geçiş yapılır.
        _isGlobalSearch = false;
        _selectedGroup = group.Name;
        _selectedGroupKind = group.Kind;
        _selectedCollection = group.Key ?? "";
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
        if (_isBackgroundRefreshing) return;
        _loadingDepth++;
        LoadingStatusText.Text = message;
        LoadingOverlay.IsVisible = true;
    }

    private void EndLoading()
    {
        if (_isBackgroundRefreshing) return;
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

        var channels = SortAndFilter(_channels
                .Where(c => c.Kind == _selectedContent && c.Group == _selectedGroup &&
                            (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
            .ToList();
        ChannelList.ItemsSource = channels.Select(channel => MediaBrowserItem.FromChannel(channel, GetChannelSubtitle(channel))).ToList();
        BrowserTitle.Text = _selectedContent == ContentKind.Movie ? L("FİLMLER") : L("KANALLAR");
        SeriesBackButton.IsVisible = false;
        ChannelCount.Text = _selectedContent == ContentKind.Movie ? $"{channels.Count:N0} film" : $"{channels.Count:N0} kanal";
    }

    private void ApplyLibraryFilter(string query)
    {
        var channels = SortAndFilter(GetLibraryChannels(_selectedGroupKind, _selectedContent)
                .Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)),
                keepOrder: _selectedGroupKind is LibraryGroupKind.Recent or LibraryGroupKind.ContinueWatching or LibraryGroupKind.RecentlyAdded)
            .ToList();

        ChannelList.ItemsSource = channels.Select(channel =>
        {
            var state = FindLibraryItem(channel);
            var progressBadge = _selectedGroupKind == LibraryGroupKind.ContinueWatching && state is not null
                ? $"%{Math.Clamp((int)Math.Round(state.PositionMs * 100d / Math.Max(1, state.DurationMs)), 1, 99)}"
                : null;
            var subtitle = _selectedGroupKind switch
            {
                LibraryGroupKind.Recent => $"{L("Son izlendi")} · {FormatRelativeTime(state?.LastWatchedAt)}",
                LibraryGroupKind.RecentlyAdded => $"{L("Eklendi")} · {FormatRelativeTime(Catalog.FirstSeen(channel.Id))}",
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
            LibraryGroupKind.RecentlyAdded => L("SON EKLENENLER"),
            LibraryGroupKind.Collection => L("KOLEKSİYON"),
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

    // Siralama ve kalite filtresi tum listelerde ayni kurallarla uygulanir.
    private IEnumerable<Channel> SortAndFilter(IEnumerable<Channel> channels, bool keepOrder = false)
    {
        channels = Preferences.Current.QualityFilter switch
        {
            "hd" => channels.Where(c => MatchesQuality(c.Name, ["HD", "FHD", "1080"])),
            "4k" => channels.Where(c => MatchesQuality(c.Name, ["4K", "UHD", "2160"])),
            "fav" => channels.Where(c => FindLibraryItem(c)?.IsFavorite == true),
            _ => channels
        };

        if (keepOrder && Preferences.Current.SortMode == "default") return channels;

        return Preferences.Current.SortMode switch
        {
            "az" => channels.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            "za" => channels.OrderByDescending(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            "new" => channels.OrderByDescending(c => Catalog.FirstSeen(c.Id) ?? DateTimeOffset.MinValue),
            "watched" => channels.OrderByDescending(c => FindLibraryItem(c)?.WatchedSeconds ?? 0),
            _ => channels
        };
    }

    private static bool MatchesQuality(string name, string[] markers) =>
        markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));

    // Menu her acilisinda mevcut koleksiyonlarla doldurulur; secili olanlar isaretli gelir.
    private void ChannelContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: MediaBrowserItem { Channel: { } channel } } menu) return;

        var addTo = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "AddToCollectionMenu");
        if (addTo is null) return;

        var entries = new List<MenuItem>();
        foreach (var name in Collections.Names)
        {
            var inCollection = Collections.Contains(name, channel.Id);
            var entry = new MenuItem { Header = inCollection ? $"✓ {name}" : name };
            entry.Click += (_, _) =>
            {
                Collections.Toggle(name, channel.Id);
                RefreshCollectionsView();
                RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
            };
            entries.Add(entry);
        }

        addTo.ItemsSource = entries;
        addTo.IsEnabled = entries.Count > 0;
    }

    private void CreateCollectionWithChannel_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MediaBrowserItem { Channel: { } channel } }) return;

        var name = Collections.CreateUnique(L("Koleksiyon"));
        Collections.Toggle(name, channel.Id);
        RefreshCollectionsView();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
        SetStatus($"{L("Koleksiyona eklendi")}: {name}");
    }

    private void CreateCollection_Click(object? sender, RoutedEventArgs e)
    {
        Collections.CreateUnique(L("Koleksiyon"));
        RefreshCollectionsView();
    }

    private void DeleteCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string name }) return;
        Collections.Delete(name);
        RefreshCollectionsView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void RenameCollection_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: string oldName } box) return;
        var newName = box.Text?.Trim() ?? "";
        if (newName == oldName) return;
        if (!Collections.Rename(oldName, newName)) box.Text = oldName;

        RefreshCollectionsView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void RefreshCollectionsView()
    {
        var items = Collections.Names
            .Select(name => new CollectionItem(name, Collections.Ids(name).Count))
            .ToList();
        CollectionsList.ItemsSource = items;
        NoCollectionsText.IsVisible = items.Count == 0;
    }

    private void SetSortMode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode }) return;
        Preferences.Current.SortMode = mode;
        Preferences.Save();
        UpdateSortMenu();
        ApplyFilter();
    }

    private void SetQualityFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter }) return;
        Preferences.Current.QualityFilter = filter;
        Preferences.Save();
        UpdateSortMenu();
        ApplyFilter();
    }

    private void UpdateSortMenu()
    {
        foreach (var option in SortModeList.Children.OfType<Button>())
            option.Classes.Set("active", option.Tag as string == Preferences.Current.SortMode);
        foreach (var chip in QualityFilterChips.Children.OfType<Button>())
            chip.Classes.Set("active", (chip.Tag as string ?? "") == Preferences.Current.QualityFilter);

        var filtered = Preferences.Current.SortMode != "default" || Preferences.Current.QualityFilter.Length > 0;
        SortButton.Classes.Set("active", filtered);
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

    // Tek tiklama yalnizca secer. Icerik sol cift tik ya da Enter ile acilir;
    // boylece sag tik menusu acmak oynatmayi baslatmaz.
    private void ChannelList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (!e.GetCurrentPoint(ChannelList).Properties.IsLeftButtonPressed) return;
        if (FindBrowserItem(e.Source as StyledElement) is not { } item) return;

        e.Handled = true;
        OpenBrowserItem(item);
    }

    private static MediaBrowserItem? FindBrowserItem(StyledElement? element)
    {
        for (var node = element; node is not null; node = node.Parent)
            if (node is ListBoxItem { DataContext: MediaBrowserItem item }) return item;
        return null;
    }

    private void OpenBrowserItem(MediaBrowserItem item)
    {
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

        if (item.Channel is { } channel) PlayChannel(channel);
    }

    // Kaynak ilk denemede acilmazsa kisa araliklarla iki kez daha denenir;
    // gecici ag hatalarinda kullanicinin elle yeniden tiklamasi gerekmez.
    private void HandlePlaybackError()
    {
        if (_playingChannel is not { } channel)
        {
            SetStatus(L("Yayın açılamadı; kaynak çevrimdışı olabilir."));
            return;
        }

        AppLog.Write($"Yayın açılamadı: {channel.Name} ({channel.Url})");
        if (_playbackRetryCount >= 2)
        {
            SetStatus(L("Yayın açılamadı; kaynak çevrimdışı olabilir."));
            return;
        }

        _playbackRetryCount++;
        SetStatus($"{L("Yayın açılamadı, yeniden deneniyor")} ({_playbackRetryCount}/2)");
        DispatcherTimer.RunOnce(() =>
        {
            if (_playingChannel == channel) PlayChannel(channel, isRetry: true);
        }, TimeSpan.FromSeconds(2));
    }

    private void PlayChannel(Channel channel, bool isRetry = false)
    {
        if (!isRetry) _playbackRetryCount = 0;
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
        _media.AddOption($":freetype-rel-fontsize={Preferences.Current.SubtitleFontSize}");
        NowPlaying.Text = channel.Name;
        MediaInfoBadge.IsVisible = false;
        NowPlayingLogo.LogoUrl = channel.LogoUrl;
        NowPlayingLogo.Initials = channel.Initials;
        NowPlayingLogo.IsVisible = true;
        PlaybackKindBadge.Text = channel.Badge;
        _playingContent = channel.Kind;
        _playingChannel = channel;
        if (Preferences.Current.LastChannelId != channel.Id)
        {
            Preferences.Current.LastChannelId = channel.Id;
            Preferences.Save();
        }
        _selectedEpgDate = DateTime.Today;
        // EPG verisini hazırla, ancak oynatma alanını kullanıcı istemeden daraltma.
        UpdateEpgPanel(channel, false);
        _historyRecordedForCurrentPlayback = false;
        _pendingResumePosition = GetResumePosition(channel);
        UpdateFavoriteButton();
        PlaybackStatus.Text = L("Yayına bağlanılıyor...");
        PlayPauseButton.IsEnabled = true;
        TrackButton.IsEnabled = false;
        UpdateChannelNavigationButtons();
        PlayPauseIcon.Data = Avalonia.Media.Geometry.Parse("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z");
        Timeline.Value = 0;
        Timeline.IsEnabled = false;
        UpdateSeekControls(false);
        TimelinePanel.IsVisible = false;
        if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
        TimeLabel.Text = "CANLI";
        _mediaPlayer.Play(_media);
        _mediaPlayer.SetRate((float)Preferences.Current.PlaybackRate);
        _mediaPlayer.AspectRatio = Preferences.Current.AspectRatio.Length == 0 ? null : Preferences.Current.AspectRatio;
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
        MediaInfoBadge.IsVisible = false;
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

        ChannelList.SelectedIndex = -1;
    }

    private void ToggleEpgPanel_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingChannel is not { Kind: ContentKind.Live } channel) return;
        if (EpgPanel.IsVisible) SetEpgPanelVisibility(false);
        else UpdateEpgPanel(channel, true);
    }

    // TMDB paneli EPG paneliyle ayni sutunu paylasir; ayni anda biri gorunur.
    private async void ShowDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MediaBrowserItem item }) return;

        var channel = item.Channel;
        var title = channel?.Kind == ContentKind.Series && !string.IsNullOrWhiteSpace(channel.Series.Title)
            ? channel.Series.Title
            : item.Name;
        var isSeries = channel?.Kind == ContentKind.Series || item.Kind != MediaBrowserItemKind.Channel;

        EpgPanel.IsVisible = false;
        DetailsPanel.IsVisible = true;
        PlayerSurfaceLayout.ColumnDefinitions = new ColumnDefinitions("*,330");
        Dispatcher.UIThread.Post(UpdatePlayerOverlayBounds);

        DetailsTitle.Text = Tmdb.CleanTitle(title);
        DetailsOverview.Text = "";
        DetailsPoster.Source = null;
        DetailsRatingBadge.IsVisible = false;
        DetailsYearBadge.IsVisible = false;

        if (!Tmdb.IsConfigured)
        {
            DetailsStatus.Text = L("İçerik bilgisi için ayarlardan TMDB anahtarı girin.");
            return;
        }

        DetailsStatus.Text = L("Bilgi alınıyor...");
        var details = await Tmdb.SearchAsync(title, isSeries);
        if (details is null)
        {
            DetailsStatus.Text = L("Bu içerik için bilgi bulunamadı.");
            return;
        }

        DetailsStatus.Text = "";
        DetailsTitle.Text = details.Title;
        DetailsOverview.Text = details.Overview ?? "";
        DetailsRating.Text = Tmdb.FormatRating(details.Rating);
        DetailsRatingBadge.IsVisible = DetailsRating.Text.Length > 0;
        DetailsYear.Text = details.Year ?? "";
        DetailsYearBadge.IsVisible = DetailsYear.Text.Length > 0;
        if (details.PosterUrl is { Length: > 0 } poster) DetailsPoster.Source = await LoadPosterAsync(poster);
    }

    private static async Task<Avalonia.Media.Imaging.Bitmap?> LoadPosterAsync(string url)
    {
        try
        {
            var bytes = await PlaylistClient.GetByteArrayAsync(url);
            return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            AppLog.Write("Afiş indirilemedi", ex);
            return null;
        }
    }

    private void CloseDetails_Click(object? sender, RoutedEventArgs e)
    {
        DetailsPanel.IsVisible = false;
        PlayerSurfaceLayout.ColumnDefinitions = new ColumnDefinitions("*,0");
        Dispatcher.UIThread.Post(UpdatePlayerOverlayBounds);
    }

    private void SaveTmdbKey_Click(object? sender, RoutedEventArgs e)
    {
        var key = TmdbKeyBox.Text?.Trim() ?? "";
        Preferences.Current.TmdbApiKey = key.Length == 0 ? null : key;
        Preferences.Save();
        TmdbStatusText.Text = key.Length == 0 ? L("Anahtar kaldırıldı.") : L("Anahtar kaydedildi.");
        TmdbStatusText.IsVisible = true;
    }

    private void OpenTmdbSite_Click(object? sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://www.themoviedb.org/settings/api");

    private void CloseEpgPanel_Click(object? sender, RoutedEventArgs e) => SetEpgPanelVisibility(false);

    private void SetEpgPanelVisibility(bool visible)
    {
        var shouldShow = visible && !_isPlayerFullscreen;
        if (shouldShow) DetailsPanel.IsVisible = false;
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
        var searchQuery = EpgSearchBox.Text?.Trim() ?? "";
        if (_epgSearchAllChannels && searchQuery.Length >= 2)
        {
            ShowEpgSearchResults(searchQuery);
            return;
        }

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

    private void SetEpgSearchScope_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string scope }) return;
        var all = scope == "all";
        if (_epgSearchAllChannels == all) return;

        _epgSearchAllChannels = all;
        EpgScopeChannelButton.Classes.Set("active", !all);
        EpgScopeAllButton.Classes.Set("active", all);
        RefreshEpgProgrammeList();
    }

    // Tum kanallarda arama: yayin akisi olan her kanalin programlari taranir,
    // sonuc listesinde kanal adi da gorunur ve tiklayinca o kanala gecilir.
    private void ShowEpgSearchResults(string query)
    {
        var now = DateTimeOffset.Now;
        var results = new List<EpgProgrammeItem>();
        foreach (var channel in _channels.Where(ch => ch.Kind == ContentKind.Live))
        {
            var schedule = FindEpgSchedule(channel);
            if (schedule is null) continue;

            foreach (var programme in schedule.Programs)
            {
                if (programme.Stop < now) continue;
                if (!programme.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;

                results.Add(new EpgProgrammeItem(programme, now, channel, showDate: true));
                if (results.Count >= 300) break;
            }

            if (results.Count >= 300) break;
        }

        EpgDateText.Text = $"{results.Count:N0} {L("sonuç")}";
        EpgPreviousDayButton.IsEnabled = false;
        EpgNextDayButton.IsEnabled = false;
        EpgProgrammeList.ItemsSource = results;
        EpgProgrammeList.IsVisible = results.Count > 0;
        EpgEmptyText.IsVisible = results.Count == 0;
        EpgEmptyText.Text = L("Aramanızla eşleşen program bulunamadı.");
    }

    private void EpgProgrammeList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (!e.GetCurrentPoint(EpgProgrammeList).Properties.IsLeftButtonPressed) return;

        for (var node = e.Source as StyledElement; node is not null; node = node.Parent)
        {
            if (node is not ListBoxItem { DataContext: EpgProgrammeItem { Channel: { } channel } }) continue;
            e.Handled = true;
            PlayChannel(channel);
            return;
        }
    }

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
        ChannelList.SelectedItem = item;
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
        PlayPauseIcon.Margin = isPlaying ? default : new Avalonia.Thickness(3, 0, 0, 0);
        if (_fullscreenPlayPauseIcon is not null)
            _fullscreenPlayPauseIcon.Margin = isPlaying ? default : new Avalonia.Thickness(3, 0, 0, 0);
        if (_playerOverlayPlayPauseIcon is not null)
            _playerOverlayPlayPauseIcon.Margin = isPlaying ? default : new Avalonia.Thickness(3, 0, 0, 0);
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
        UpdateFullscreenVolumeBar(e.NewValue);
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
            if (!_isPlayerFullscreen && _playerOverlay?.IsVisible != true) PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
            TimeLabel.Text = "CANLI";
            if (_fullscreenTimeLabel is not null) _fullscreenTimeLabel.Text = "CANLI";
            if (_playerOverlayTimeLabel is not null) _playerOverlayTimeLabel.Text = "CANLI";
            return;
        }

        TimelinePanel.IsVisible = true;
        if (_fullscreenTimelinePanel is not null) _fullscreenTimelinePanel.IsVisible = true;
        if (!_isPlayerFullscreen && _playerOverlay?.IsVisible != true) PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
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
            LibraryGroupKind.RecentlyAdded => channels
                .Where(c => Catalog.IsRecentlyAdded(c.Id))
                .OrderByDescending(c => Catalog.FirstSeen(c.Id))
                .Take(200),
            LibraryGroupKind.Collection => channels
                .Where(c => Collections.Contains(_selectedCollection, c.Id))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => []
        };
        return channels.ToList();
    }

    private void TouchPlaybackHistory(Channel channel)
    {
        var state = GetOrCreateLibraryItem(channel);
        state.Kind = channel.Kind;
        state.LastWatchedAt = DateTimeOffset.UtcNow;
        state.PlayCount++;

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
        var finished = channel;
        var state = FindLibraryItem(channel);
        if (state is null) return;
        state.PositionMs = 0;
        state.DurationMs = 0;
        _pendingResumePosition = null;
        CleanupLibraryItem(channel.Id, state);
        SaveLibraryState();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
        TryPlayNextEpisode(finished);
    }

    // Bolum bitince ayni dizinin sonraki bolumu sezon ve bolum sirasina gore bulunur.
    private void TryPlayNextEpisode(Channel finished)
    {
        if (!Preferences.Current.AutoPlayNextEpisode) return;
        if (finished.Kind != ContentKind.Series || string.IsNullOrWhiteSpace(finished.Series.Title)) return;

        var current = (finished.Series.Season ?? 0, finished.Series.Episode ?? 0);
        var next = _channels
            .Where(ch => ch.Kind == ContentKind.Series &&
                         string.Equals(ch.Series.Title, finished.Series.Title, StringComparison.CurrentCultureIgnoreCase))
            .Select(ch => (Channel: ch, Key: (ch.Series.Season ?? 0, ch.Series.Episode ?? 0)))
            .Where(x => x.Key.CompareTo(current) > 0)
            .OrderBy(x => x.Key.Item1)
            .ThenBy(x => x.Key.Item2)
            .Select(x => x.Channel)
            .FirstOrDefault();

        if (next is null) return;

        SetStatus($"{L("Sonraki bölüm")}: {next.Name}");
        PlayChannel(next);
    }

    // Uzak listeler acilisi bekletmeden, arka planda sessizce tazelenir.
    private async void RefreshPlaylistInBackground()
    {
        if (!Preferences.Current.BackgroundRefresh || _isBackgroundRefreshing || _loadingDepth > 0) return;
        if (_playlists.FirstOrDefault(entry => entry.IsActive) is not { IsRemote: true } active) return;

        _isBackgroundRefreshing = true;
        try
        {
            await LoadPlaylistEntryAsync(active, forceRefresh: true);
            SetStatus($"{L("Liste arka planda güncellendi")} · {_channels.Count:N0} {L("içerik")}");
        }
        catch (Exception ex)
        {
            AppLog.Write("Arka plan yenilemesi başarısız", ex);
        }
        finally
        {
            _isBackgroundRefreshing = false;
        }
    }

    private void AutoNextEpisode_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        var enabled = toggle.IsChecked == true;
        if (Preferences.Current.AutoPlayNextEpisode == enabled) return;
        Preferences.Current.AutoPlayNextEpisode = enabled;
        Preferences.Save();
    }

    private void BackgroundRefresh_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        var enabled = toggle.IsChecked == true;
        if (Preferences.Current.BackgroundRefresh == enabled) return;
        Preferences.Current.BackgroundRefresh = enabled;
        Preferences.Save();
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

        _playerOverlayPreviousButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M6,5 L6,19 M19,5 L9,12 L19,19 Z", 19, 1.8));
        _playerOverlayPreviousButton.Click += PreviousChannel_Click;
        _playerOverlayLastButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M4,4 L4,9 L9,9 M5.5,7 A8,8 0 1 1 4.8,15", 19));
        _playerOverlayLastButton.Click += LastChannel_Click;
        _playerOverlayNextButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M18,5 L18,19 M5,5 L15,12 L5,19 Z", 19, 1.8));
        _playerOverlayNextButton.Click += NextChannel_Click;
        _playerOverlayRewindButton = CreatePlayerOverlayButton(CreateFullscreenSeekIcon("10", true));
        _playerOverlayRewindButton.Click += Rewind_Click;
        _playerOverlayForwardButton = CreatePlayerOverlayButton(CreateFullscreenSeekIcon("10", false));
        _playerOverlayForwardButton.Click += Forward_Click;
        var epgButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M4,5 L20,5 L20,20 L4,20 Z M8,2 L8,7 M16,2 L16,7 M4,10 L20,10 M8,14 L10,14 M14,14 L16,14 M8,17 L10,17 M14,17 L16,17", 19));
        epgButton.Click += ToggleEpgPanel_Click;
        var favoriteButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M12,2.5 L14.9,8.4 L21.4,9.3 L16.7,13.9 L17.8,20.4 L12,17.3 L6.2,20.4 L7.3,13.9 L2.6,9.3 L9.1,8.4 Z", 19));
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
        _playerOverlayPlayPauseIcon = CreatePlayPauseIcon();
        var playPauseButton = CreatePlayerOverlayButton(_playerOverlayPlayPauseIcon, primary: true);
        playPauseButton.Click += PlayPause_Click;
        var fullscreenButton = CreatePlayerOverlayButton(CreateFullscreenIcon("M4,9 L4,4 L9,4 M15,4 L20,4 L20,9 M20,15 L20,20 L15,20 M9,20 L4,20 L4,15", 19));
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

    private static Control CreatePlayerOverlayVolumeIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M3,9 L7,9 L12,5 L12,17 L7,13 L3,13 Z"),
            Stroke = new SolidColorBrush(Color.Parse("#F2F4F7")),
            StrokeThickness = 1.7,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M15,8 C17,10 17,12 15,14 M18,5 C22,9 22,13 18,17"),
            Stroke = new SolidColorBrush(Color.Parse("#F2F4F7")),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            Fill = null
        });
        return new Viewbox { Width = 20, Height = 20, Child = canvas };
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
        PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
    }

    private void HidePlayerOverlay(bool restoreControls = false)
    {
        _playerOverlay?.Hide();
        if (!restoreControls) return;
        PlayerControls.IsVisible = true;
        PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
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
        if (e.Key == Key.Escape && _isMiniPlayer)
        {
            ExitMiniPlayer();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isPlayerFullscreen)
        {
            SetPlayerFullscreen(false);
            e.Handled = true;
            return;
        }
        if (e.Source is TextBox) return;
        if (e.Source is Control source && IsInsideList(source) &&
            e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter) return;
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
            case Key.Up:
                VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + 5, 0, 100);
                e.Handled = true;
                break;
            case Key.Down:
                VolumeSlider.Value = Math.Clamp(VolumeSlider.Value - 5, 0, 100);
                e.Handled = true;
                break;
            case Key.S:
                TakeSnapshot_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.P:
                ToggleMiniPlayer_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    // Liste icindeki ok tuslari ve Enter listeye ait kalir; ses ve sarma kisayollari devreye girmez.
    private static bool IsInsideList(StyledElement? element)
    {
        for (var node = element; node is not null; node = node.Parent)
            if (node is ListBox) return true;
        return false;
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
        UpdateFullscreenVolumeBar(VolumeSlider.Value);
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
        volumeButton.Click += VolumeButton_Click;
        _fullscreenVolumeText = new TextBlock
        {
            Text = VolumeValueText.Text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#A3ABB8")),
            Width = 32,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var volumeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        volumeRow.Children.Add(CreateFullscreenVolumeBar());
        volumeRow.Children.Add(_fullscreenVolumeText);
        var volumeGroove = new Border
        {
            CornerRadius = new Avalonia.CornerRadius(100),
            Background = new SolidColorBrush(Color.Parse("#14171B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#262B33")),
            BorderThickness = new Avalonia.Thickness(1),
            Height = 34,
            Padding = new Avalonia.Thickness(14, 0),
            Margin = new Avalonia.Thickness(3, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = volumeRow
        };

        _fullscreenPlayPauseIcon = CreatePlayPauseIcon();
        var playPauseButton = CreateFullscreenActionButton(_fullscreenPlayPauseIcon, true);
        playPauseButton.Click += PlayPause_Click;
        _fullscreenPreviousChannelButton = CreateFullscreenActionButton(CreateFullscreenIcon("M6,5 L6,19 M19,5 L9,12 L19,19 Z", 20, 1.8));
        _fullscreenPreviousChannelButton.Click += PreviousChannel_Click;
        _fullscreenLastChannelButton = CreateFullscreenActionButton(CreateFullscreenIcon("M4,4 L4,9 L9,9 M5.5,7 A8,8 0 1 1 4.8,15", 21));
        _fullscreenLastChannelButton.Click += LastChannel_Click;
        _fullscreenNextChannelButton = CreateFullscreenActionButton(CreateFullscreenIcon("M18,5 L18,19 M5,5 L15,12 L5,19 Z", 20, 1.8));
        _fullscreenNextChannelButton.Click += NextChannel_Click;
        _fullscreenRewindButton = CreateFullscreenActionButton(CreateFullscreenSeekIcon("10", rewind: true));
        _fullscreenRewindButton.Click += Rewind_Click;
        _fullscreenForwardButton = CreateFullscreenActionButton(CreateFullscreenSeekIcon("10", rewind: false));
        _fullscreenForwardButton.Click += Forward_Click;
        UpdateChannelNavigationButtons();
        UpdateSeekControls(_playingContent != ContentKind.Live && _mediaPlayer.IsSeekable && _mediaPlayer.Length > 0);
        var trackButton = CreateFullscreenMenuButton(
            "M3,5 L21,5 L21,19 L3,19 Z M6,14 L11,14 M14,14 L18,14 M6,10.5 L9,10.5 M12,10.5 L18,10.5",
            out var trackPanel, BuildTrackMenu);
        _fullscreenTrackPanel = trackPanel;
        var optionsButton = CreateFullscreenMenuButton(
            "M5,12 A1.6,1.6 0 1 1 5.01,12 M12,12 A1.6,1.6 0 1 1 12.01,12 M19,12 A1.6,1.6 0 1 1 19.01,12",
            out _, BuildPlaybackOptionsMenu);
        var exitButton = CreateFullscreenActionButton(
            CreateFullscreenIcon("M9,4 L4,4 L4,9 M15,4 L20,4 L20,9 M20,15 L20,20 L15,20 M9,20 L4,20 L4,15", 21, 1.8));
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
        actions.Children.Add(volumeGroove);
        actions.Children.Add(playPauseButton);
        actions.Children.Add(trackButton);
        actions.Children.Add(optionsButton);
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
        _fullscreenControlsOverlay.RequestedThemeVariant = RequestedThemeVariant;
        _fullscreenControlsOverlay.PointerMoved += (_, _) => HandleFullscreenPointerActivity();
        _fullscreenControlsOverlay.KeyDown += (_, e) =>
        {
            HandlePlayerShortcut(e);
        };
    }

    // Tum ikonlar 24x24 tuval icinde cizilir; Viewbox olcekledigi icin hepsi
    // ayni optik agirlikta gorunur. Alt bardaki ikonlarla birebir ayni yontem.
    private static Control CreateFullscreenIcon(string data, double size = 21, double thickness = 1.7)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse(data),
            Stroke = new SolidColorBrush(Color.Parse("#F2F4F7")),
            StrokeThickness = thickness,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static Avalonia.Controls.Shapes.Path CreatePlayPauseIcon() => new()
    {
        Data = Avalonia.Media.Geometry.Parse("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z"),
        Fill = new SolidColorBrush(Color.Parse("#140F0D")),
        Width = 18,
        Height = 18,
        Stretch = Stretch.Uniform
    };

    private static Grid CreateFullscreenSeekIcon(string seconds, bool rewind)
    {
        var grid = new Grid();
        grid.Children.Add(CreateFullscreenIcon(rewind
            ? "M5,5 L5,10 L10,10 M6,8 A8,8 0 1 1 5,16"
            : "M19,5 L19,10 L14,10 M18,8 A8,8 0 1 0 19,16", 24, 1.6));
        grid.Children.Add(new TextBlock
        {
            Text = seconds,
            Foreground = new SolidColorBrush(Color.Parse("#F2F4F7")),
            FontSize = 8,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return grid;
    }

    private static Button CreateFullscreenActionButton(Control content, bool primary = false) => new()
    {
        Width = primary ? 52 : 44,
        Height = primary ? 52 : 44,
        Padding = new Avalonia.Thickness(0),
        CornerRadius = new Avalonia.CornerRadius(primary ? 26 : 22),
        BorderThickness = new Avalonia.Thickness(0),
        Background = new SolidColorBrush(Color.Parse(primary ? "#F2622E" : "#1C2026")),
        Content = content,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    // Tam ekran ayri bir pencere oldugu icin tema sablonlari oraya ulasmiyordu ve
    // Fluent'in tutamagi ize gore kayik duruyordu. Cubuk burada elle ciziliyor:
    // iz, dolu kisim ve tutamak ayni dikey merkezde.
    private Control CreateFullscreenVolumeBar()
    {
        var track = new Border
        {
            Height = 4,
            CornerRadius = new Avalonia.CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse("#525B69")),
            VerticalAlignment = VerticalAlignment.Center
        };
        _fullscreenVolumeFill = new Border
        {
            Height = 4,
            Width = 0,
            CornerRadius = new Avalonia.CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse("#F2622E")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        _fullscreenVolumeThumb = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = FullscreenVolumeThumbSize,
            Height = FullscreenVolumeThumbSize,
            Fill = new SolidColorBrush(Color.Parse("#F2F4F7")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var host = new Grid
        {
            Width = FullscreenVolumeTrackWidth,
            Height = 20,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        host.Children.Add(track);
        host.Children.Add(_fullscreenVolumeFill);
        host.Children.Add(_fullscreenVolumeThumb);
        host.PointerPressed += (_, e) =>
        {
            SetVolumeFromFullscreenBar(e.GetPosition(host).X);
            e.Pointer.Capture(host);
        };
        host.PointerMoved += (_, e) =>
        {
            if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
                SetVolumeFromFullscreenBar(e.GetPosition(host).X);
        };
        host.PointerReleased += (_, e) => e.Pointer.Capture(null);

        UpdateFullscreenVolumeBar(VolumeSlider.Value);
        return host;
    }

    private void SetVolumeFromFullscreenBar(double x) =>
        VolumeSlider.Value = Math.Clamp(x / FullscreenVolumeTrackWidth * 100, 0, 100);

    private void UpdateFullscreenVolumeBar(double value)
    {
        var ratio = Math.Clamp(value / 100, 0, 1);
        if (_fullscreenVolumeFill is not null)
            _fullscreenVolumeFill.Width = FullscreenVolumeTrackWidth * ratio;
        if (_fullscreenVolumeThumb is not null)
            _fullscreenVolumeThumb.Margin =
                new Avalonia.Thickness(ratio * (FullscreenVolumeTrackWidth - FullscreenVolumeThumbSize), 0, 0, 0);
        if (_fullscreenVolumeText is not null)
            _fullscreenVolumeText.Text = $"%{value:0}";
    }

    private Button CreateFullscreenVolumeButton()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M3,9 L7,9 L12,5 L12,17 L7,13 L3,13 Z"),
            Stroke = new SolidColorBrush(Color.Parse("#F2F4F7")),
            StrokeThickness = 1.7,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        });
        _fullscreenVolumeWaveIcon = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M15,8 C17,10 17,12 15,14 M18,5 C22,9 22,13 18,17"),
            Stroke = new SolidColorBrush(Color.Parse("#F2F4F7")),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            IsVisible = VolumeSlider.Value > 0
        };
        _fullscreenVolumeMutedIcon = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M15,9 L20,14 M20,9 L15,14"),
            Stroke = new SolidColorBrush(Color.Parse("#EA2E4E")),
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            IsVisible = VolumeSlider.Value <= 0
        };
        canvas.Children.Add(_fullscreenVolumeWaveIcon);
        canvas.Children.Add(_fullscreenVolumeMutedIcon);
        return CreateFullscreenActionButton(new Viewbox { Width = 21, Height = 21, Child = canvas });
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
            ContentArea.RowDefinitions = new RowDefinitions("76,*");
            ContentBody.ColumnDefinitions = new ColumnDefinitions("486,*");
            ContentBody.ColumnSpacing = 0;
            Grid.SetColumn(PlayerPanel, 1);
            Grid.SetColumnSpan(PlayerPanel, 1);
            PlayerPanel.CornerRadius = new Avalonia.CornerRadius(0);
            PlayerPanel.BorderThickness = new Avalonia.Thickness(0);
            PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
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

    // Oynatilan yayinin gercek cozunurlugu ve kare hizi; VLC bunlari ancak
    // goruntu akmaya basladiktan sonra bildirdigi icin duzenli araliklarla okunur.
    private void UpdateMediaInfoBadge()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_playingChannel is null || !_mediaPlayer.IsPlaying)
                {
                    MediaInfoBadge.IsVisible = false;
                    return;
                }

                uint width = 0;
                uint height = 0;
                if (!_mediaPlayer.Size(0, ref width, ref height) || width == 0 || height == 0)
                {
                    var video = _mediaPlayer.Media?.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
                    if (video is { } track)
                    {
                        width = track.Data.Video.Width;
                        height = track.Data.Video.Height;
                    }
                }

                if (width == 0 || height == 0)
                {
                    MediaInfoBadge.IsVisible = false;
                    return;
                }

                var fps = _mediaPlayer.Fps;
                MediaInfoText.Text = fps > 0.1f
                    ? $"{width}×{height} · {fps:0.#} FPS"
                    : $"{width}×{height}";
                MediaInfoBadge.IsVisible = true;
            }
            catch
            {
                // Bilgi okunamazsa rozet gizli kalir; oynatma etkilenmez.
                MediaInfoBadge.IsVisible = false;
            }
        });
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
        SetSettingsTab(SettingsTabStats, SettingsTabStatsIcon, SettingsSectionStats, section == "stats");
        SetSettingsTab(SettingsTabPrivacy, SettingsTabPrivacyIcon, SettingsSectionPrivacy, section == "privacy");
        SetSettingsTab(SettingsTabAbout, SettingsTabAboutIcon, SettingsSectionAbout, section == "about");

        SettingsSubtitleText.Text = section switch
        {
            "general" => L("Genel"),
            "update" => L("Güncelleme"),
            "stats" => L("İstatistikler"),
            "privacy" => L("Gizlilik"),
            "about" => L("Hakkında"),
            _ => L("Oynatma Listeleri")
        };

        if (section == "update") RefreshUpdateSection();
        if (section == "stats") RefreshStatsSection();
        if (section != "privacy") return;
        SettingsDataPathText.Text = SettingsDirectory;
        SettingsPrivacyStatus.IsVisible = false;
        ParentalStatusText.IsVisible = false;
        RefreshParentalView();
    }

    private static void SetSettingsTab(Button tab, Avalonia.Controls.Shapes.Path icon, Control section, bool active)
    {
        tab.Classes.Set("active", active);
        icon.Stroke = active ? new SolidColorBrush(Color.Parse(ChannelLogo.AccentColor)) : AppTheme.Brush("TextMutedBg");
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

    // Oynatma surdukce her sayac tikinda izleme suresi birikir.
    private void AccumulateWatchTime()
    {
        if (_playingChannel is not { } channel || !_mediaPlayer.IsPlaying) return;

        var state = GetOrCreateLibraryItem(channel);
        state.Kind = channel.Kind;
        state.WatchedSeconds += 5;
        SaveLibraryState();
    }

    private void RefreshStatsSection()
    {
        var items = _libraryState.Items.Values.Where(item => item.WatchedSeconds > 0).ToList();
        var total = items.Sum(item => item.WatchedSeconds);

        StatsTotalText.Text = FormatWatchDuration(total);
        StatsItemsText.Text = $"{items.Count:N0}";
        StatsPlaysText.Text = $"{_libraryState.Items.Values.Sum(item => item.PlayCount):N0}";

        StatsKindList.Children.Clear();
        foreach (var kind in new[] { ContentKind.Live, ContentKind.Movie, ContentKind.Series })
        {
            var seconds = items.Where(item => item.Kind == kind).Sum(item => item.WatchedSeconds);
            var label = kind switch
            {
                ContentKind.Movie => L("Filmler"),
                ContentKind.Series => L("Diziler"),
                _ => L("Canlı TV")
            };
            StatsKindList.Children.Add(CreateStatRow(label, FormatWatchDuration(seconds),
                total > 0 ? seconds / (double)total : 0));
        }

        var top = _libraryState.Items
            .Where(pair => pair.Value.WatchedSeconds > 0)
            .OrderByDescending(pair => pair.Value.WatchedSeconds)
            .Take(10)
            .Select(pair => (
                Name: _channels.FirstOrDefault(ch => ch.Id == pair.Key)?.Name ?? L("Listede yok"),
                pair.Value.WatchedSeconds))
            .ToList();

        StatsTopList.Children.Clear();
        StatsEmptyText.IsVisible = top.Count == 0;
        var best = top.Count > 0 ? top[0].WatchedSeconds : 0;
        foreach (var entry in top)
            StatsTopList.Children.Add(CreateStatRow(entry.Name, FormatWatchDuration(entry.WatchedSeconds),
                best > 0 ? entry.WatchedSeconds / (double)best : 0));
    }

    // Ad, sure ve oransal cubuktan olusan tek satir.
    private static Control CreateStatRow(string label, string value, double ratio)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var name = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 14, 0)
        };
        var amount = new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = AppTheme.Brush("TextMutedBg"),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(name);
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(ratio * 100, 0, 100),
            Height = 4,
            Margin = new Avalonia.Thickness(0, 7, 0, 0)
        };

        return new StackPanel { Children = { grid, bar } };
    }

    private static string FormatWatchDuration(long seconds)
    {
        if (seconds < 60) return $"{seconds} sn";
        if (seconds < 3600) return $"{seconds / 60} dk";
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        return minutes == 0 ? $"{hours} sa" : $"{hours} sa {minutes} dk";
    }

    private void ResetStats_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in _libraryState.Items.Values)
        {
            item.WatchedSeconds = 0;
            item.PlayCount = 0;
        }

        SaveLibraryState();
        RefreshStatsSection();
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

    // Liste yuklendikten sonra en son izlenen yayin, kullanici istemisse acilir.
    private void TryResumeLastChannel()
    {
        if (!Preferences.Current.ResumeLastChannel || _playingChannel is not null) return;
        if (Preferences.Current.LastChannelId is not { Length: > 0 } id) return;

        var channel = _channels.FirstOrDefault(c => c.Id == id);
        if (channel is null) return;

        OpenLibrary(channel.Kind);
        PlayChannel(channel);
    }

    private bool IsGroupLocked(string group) =>
        !_parentalUnlocked &&
        Preferences.Current.ParentalPinHash is { Length: > 0 } &&
        Preferences.Current.LockedGroups.Contains(group, StringComparer.CurrentCultureIgnoreCase);

    private static string HashPin(string pin) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("bgiptv:" + pin)));

    private void LockGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChannelGroup group }) return;
        if (group.Kind != LibraryGroupKind.Regular) return;

        if (Preferences.Current.ParentalPinHash is not { Length: > 0 })
        {
            ShowSettings("privacy");
            ShowParentalStatus(L("Önce bir PIN belirleyin."));
            return;
        }

        if (Preferences.Current.LockedGroups.Contains(group.Name, StringComparer.CurrentCultureIgnoreCase)) return;

        Preferences.Current.LockedGroups.Add(group.Name);
        Preferences.Save();
        RefreshParentalView();
        RefreshGroups();
    }

    private void UnlockGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string group }) return;
        Preferences.Current.LockedGroups.RemoveAll(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
        Preferences.Save();
        RefreshParentalView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void SetParentalPin_Click(object? sender, RoutedEventArgs e)
    {
        var pin = ParentalPinBox.Text?.Trim() ?? "";
        if (pin.Length is < 4 or > 8 || !pin.All(char.IsDigit))
        {
            ShowParentalStatus(L("PIN 4-8 haneli sayı olmalı."));
            return;
        }

        Preferences.Current.ParentalPinHash = HashPin(pin);
        Preferences.Save();
        ParentalPinBox.Text = "";
        _parentalUnlocked = true;
        ShowParentalStatus(L("PIN kaydedildi."));
        RefreshParentalView();
    }

    private void UnlockParental_Click(object? sender, RoutedEventArgs e)
    {
        var pin = ParentalPinBox.Text?.Trim() ?? "";
        if (Preferences.Current.ParentalPinHash != HashPin(pin))
        {
            ShowParentalStatus(L("PIN hatalı."));
            return;
        }

        ParentalPinBox.Text = "";
        _parentalUnlocked = true;
        ShowParentalStatus(L("Kilit bu oturum için açıldı."));
        RefreshParentalView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void RemoveParentalPin_Click(object? sender, RoutedEventArgs e)
    {
        var pin = ParentalPinBox.Text?.Trim() ?? "";
        if (Preferences.Current.ParentalPinHash != HashPin(pin))
        {
            ShowParentalStatus(L("PIN hatalı."));
            return;
        }

        Preferences.Current.ParentalPinHash = null;
        Preferences.Current.LockedGroups.Clear();
        Preferences.Save();
        ParentalPinBox.Text = "";
        _parentalUnlocked = false;
        ShowParentalStatus(L("PIN kaldırıldı, kilitler açıldı."));
        RefreshParentalView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void ShowParentalStatus(string text)
    {
        ParentalStatusText.Text = text;
        ParentalStatusText.IsVisible = true;
    }

    private void RefreshParentalView()
    {
        var hasPin = Preferences.Current.ParentalPinHash is { Length: > 0 };
        ParentalPinButton.Content = hasPin ? L("PIN\'i değiştir") : L("PIN belirle");
        ParentalUnlockButton.IsVisible = hasPin && !_parentalUnlocked;
        ParentalRemoveButton.IsVisible = hasPin;

        var locked = Preferences.Current.LockedGroups
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        LockedGroupsList.ItemsSource = locked;
        NoLockedGroupsText.IsVisible = locked.Count == 0;
    }

    private static bool IsGroupHidden(string group) =>
        Preferences.Current.HiddenGroups.Contains(group, StringComparer.CurrentCultureIgnoreCase);

    // Gruplar sag tik menusunden gizlenir; listeden cikar ama liste yeniden
    // yuklendiginde tercih korunur.
    private void HideGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChannelGroup group }) return;
        if (group.Kind != LibraryGroupKind.Regular || IsGroupHidden(group.Name)) return;

        Preferences.Current.HiddenGroups.Add(group.Name);
        Preferences.Save();
        RefreshGroups();
        RefreshHiddenGroupsView();
    }

    private void UnhideGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string group }) return;
        Preferences.Current.HiddenGroups.RemoveAll(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
        Preferences.Save();
        RefreshHiddenGroupsView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void ShowAllGroups_Click(object? sender, RoutedEventArgs e)
    {
        if (Preferences.Current.HiddenGroups.Count == 0) return;
        Preferences.Current.HiddenGroups.Clear();
        Preferences.Save();
        RefreshHiddenGroupsView();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void RefreshHiddenGroupsView()
    {
        var hidden = Preferences.Current.HiddenGroups
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        HiddenGroupsList.ItemsSource = hidden;
        NoHiddenGroupsText.IsVisible = hidden.Count == 0;
        ShowAllGroupsButton.IsEnabled = hidden.Count > 0;
    }

    private static readonly (string Accent, string Soft)[] AccentPalette =
    [
        ("#F2622E", "#FF8A5C"),
        ("#2E7CF2", "#5C9DFF"),
        ("#8B5CF6", "#A78BFA"),
        ("#1FB981", "#4ED9A0"),
        ("#E8394B", "#FF6B7A")
    ];

    private void SetTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string theme } || Preferences.Current.Theme == theme) return;
        Preferences.Current.Theme = theme;
        Preferences.Save();
        ApplyTheme();
        ApplyAccentColor();
        if (_channels.Count > 0) RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
    }

    private void ApplyTheme()
    {
        AppTheme.Apply(this, Preferences.Current.Theme);
        var light = AppTheme.Name == AppTheme.Light;
        ThemeDarkButton.Classes.Set("active", !light);
        ThemeLightButton.Classes.Set("active", light);
        ThemeDarkCheck.IsVisible = !light;
        ThemeLightCheck.IsVisible = light;
        ChannelLogo.PlaceholderIsLight = light;
    }

    private void SetAccentColor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string accent }) return;
        Preferences.Current.AccentColor = accent;
        Preferences.Save();
        ApplyAccentColor();
    }

    // Vurgu rengi tema kaynaklarina yazilir; kodla boyanan yerler de burada tazelenir.
    private void ApplyAccentColor()
    {
        var accent = Preferences.Current.AccentColor;
        var soft = AccentPalette.FirstOrDefault(p => p.Accent == accent).Soft ?? "#FF8A5C";
        var color = Color.Parse(accent);

        Resources["AccentBrush"] = new SolidColorBrush(color);
        Resources["AccentSoftBrush"] = new SolidColorBrush(Color.Parse(soft));
        Resources["AccentTintBrush"] = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
        Resources["AccentTintStrongBrush"] = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));

        foreach (var glow in new[] { HomeLiveGlow, HomeMovieGlow, HomeSeriesGlow })
            glow.Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x1F, color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1)
                }
            };

        foreach (var swatch in AccentSwatches.Children.OfType<Button>())
            swatch.Classes.Set("active", swatch.Tag as string == accent);

        ChannelLogo.AccentColor = soft;
        ShowSettingsSection(_settingsSection);
    }

    private void ResumeLastChannel_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        var enabled = toggle.IsChecked == true;
        if (Preferences.Current.ResumeLastChannel == enabled) return;
        Preferences.Current.ResumeLastChannel = enabled;
        Preferences.Save();
    }

    // Ses ve altyazi izleri ancak oynatma basladiktan sonra bilindigi icin
    // menu her acilisinda yeniden kurulur.
    private void BuildTrackMenu(StackPanel panel)
    {
        panel.Children.Clear();

        panel.Children.Add(CreateTrackSection(L("SES İZİ")));
        var audioTracks = SafeTrackDescriptions(() => _mediaPlayer.AudioTrackDescription);
        if (audioTracks.Length == 0)
            panel.Children.Add(CreateTrackHint(L("Ses izi bulunamadı.")));
        else
            foreach (var track in audioTracks)
                panel.Children.Add(CreateTrackButton(panel,
                    track.Name, track.Id == _mediaPlayer.AudioTrack, () => _mediaPlayer.SetAudioTrack(track.Id)));

        panel.Children.Add(CreateTrackSection(L("ALTYAZI")));
        var subtitleTracks = SafeTrackDescriptions(() => _mediaPlayer.SpuDescription);
        panel.Children.Add(CreateTrackButton(panel,
            L("Kapalı"), _mediaPlayer.Spu <= 0, () => _mediaPlayer.SetSpu(-1)));
        foreach (var track in subtitleTracks.Where(t => t.Id > 0))
            panel.Children.Add(CreateTrackButton(panel,
                track.Name, track.Id == _mediaPlayer.Spu, () => _mediaPlayer.SetSpu(track.Id)));

        var loadButton = new Button
        {
            Content = L("Dosyadan altyazı yükle..."),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };
        loadButton.Click += LoadSubtitleFile_Click;
        panel.Children.Add(loadButton);

        panel.Children.Add(CreateTrackSection(L("ALTYAZI GECİKMESİ")));
        var delayRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var minus = CreateDelayButton("−0,5 sn", -500_000);
        var plus = CreateDelayButton("+0,5 sn", 500_000);
        _subtitleDelayText = new TextBlock
        {
            Text = FormatSubtitleDelay(),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        delayRow.Children.Add(minus);
        Grid.SetColumn(_subtitleDelayText, 1);
        delayRow.Children.Add(_subtitleDelayText);
        Grid.SetColumn(plus, 2);
        delayRow.Children.Add(plus);
        panel.Children.Add(delayRow);

        panel.Children.Add(CreateTrackSection(L("ALTYAZI BOYUTU")));
        var sizes = new WrapPanel();
        foreach (var (label, value) in new[] { (L("Küçük"), 20), (L("Normal"), 16), (L("Büyük"), 12) })
        {
            var button = new Button { Content = label, Tag = value, Classes = { "chip" } };
            button.Classes.Set("active", Preferences.Current.SubtitleFontSize == value);
            button.Click += SetSubtitleSize_Click;
            sizes.Children.Add(button);
        }

        panel.Children.Add(sizes);
        panel.Children.Add(CreateTrackHint(L("Boyut değişikliği sonraki oynatmada uygulanır.")));
    }

    private static LibVLCSharp.Shared.Structures.TrackDescription[] SafeTrackDescriptions(
        Func<LibVLCSharp.Shared.Structures.TrackDescription[]> read)
    {
        try { return read() ?? []; }
        catch (Exception ex)
        {
            AppLog.Write("İz listesi okunamadı", ex);
            return [];
        }
    }

    private static TextBlock CreateTrackSection(string title) => new()
    {
        Text = title,
        FontSize = 10.5,
        FontWeight = FontWeight.Bold,
        Foreground = AppTheme.Brush("TextFaintBg")
    };

    private static TextBlock CreateTrackHint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = AppTheme.Brush("TextFaintBg"),
        TextWrapping = TextWrapping.Wrap
    };

    private Button CreateTrackButton(StackPanel panel, string label, bool active, Action apply)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Avalonia.Thickness(13, 8),
            Margin = new Avalonia.Thickness(0, 0, 0, 5)
        };
        button.Classes.Set("trackOption", true);
        button.Classes.Set("active", active);
        button.Click += (_, _) =>
        {
            apply();
            BuildTrackMenu(panel);
        };
        return button;
    }

    private Button CreateDelayButton(string label, long deltaMicroseconds)
    {
        var button = new Button { Content = label, Classes = { "chip" }, Margin = new Avalonia.Thickness(0) };
        button.Click += (_, _) =>
        {
            _mediaPlayer.SetSpuDelay(_mediaPlayer.SpuDelay + deltaMicroseconds);
            if (_subtitleDelayText is not null) _subtitleDelayText.Text = FormatSubtitleDelay();
        };
        return button;
    }

    private string FormatSubtitleDelay() => $"{_mediaPlayer.SpuDelay / 1_000_000.0:+0.0;-0.0;0,0} sn";

    private void SetSubtitleSize_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int size }) return;
        Preferences.Current.SubtitleFontSize = size;
        Preferences.Save();
        BuildTrackMenu(TrackFlyoutPanel);
        if (_fullscreenTrackPanel is not null) BuildTrackMenu(_fullscreenTrackPanel);
    }

    // Harici altyazi dosyasi secildiginde hemen yuklenir; oynatma yeni
    // basliyorsa dosya beklemeye alinir ve Playing olayinda uygulanir.
    private async void LoadSubtitleFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Altyazı dosyası seç"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Altyazı") { Patterns = ["*.srt", "*.ass", "*.ssa", "*.sub", "*.vtt"] }
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path)) return;

        _pendingSubtitleFile = path;
        if (_mediaPlayer.IsPlaying) ApplyPendingSubtitleFile();
    }

    private void ApplyPendingSubtitleFile()
    {
        if (_pendingSubtitleFile is not { Length: > 0 } path) return;
        _pendingSubtitleFile = null;

        try
        {
            _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true);
            SetStatus($"{L("Altyazı yüklendi")}: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppLog.Write("Altyazı yüklenemedi", ex);
            SetStatus(L("Altyazı yüklenemedi."));
        }
    }

    // Tam ekran ayri pencere oldugu icin hiz/oran/ekran goruntusu menusu orada kodla kurulur.
    private void BuildPlaybackOptionsMenu(StackPanel panel)
    {
        panel.Children.Clear();

        panel.Children.Add(CreateTrackSection(L("OYNATMA HIZI")));
        var rates = new WrapPanel();
        foreach (var rate in new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 })
        {
            var tag = rate.ToString("0.##", CultureInfo.InvariantCulture);
            var button = new Button { Content = $"{rate:0.##}×", Tag = tag, Classes = { "chip" } };
            button.Classes.Set("active", Math.Abs(Preferences.Current.PlaybackRate - rate) < 0.001);
            button.Click += (_, _) =>
            {
                SetPlaybackRate_Click(button, new RoutedEventArgs());
                BuildPlaybackOptionsMenu(panel);
            };
            rates.Children.Add(button);
        }

        panel.Children.Add(rates);

        panel.Children.Add(CreateTrackSection(L("GÖRÜNTÜ ORANI")));
        var aspects = new WrapPanel();
        foreach (var (value, label) in new[] { ("", L("Otomatik")), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9", "21:9") })
        {
            var button = new Button { Content = label, Tag = value, Classes = { "chip" } };
            button.Classes.Set("active", Preferences.Current.AspectRatio == value);
            button.Click += (_, _) =>
            {
                SetAspectRatio_Click(button, new RoutedEventArgs());
                BuildPlaybackOptionsMenu(panel);
            };
            aspects.Children.Add(button);
        }

        panel.Children.Add(aspects);

        var snapshot = new Button
        {
            Content = L("Ekran görüntüsü al"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        snapshot.Click += TakeSnapshot_Click;
        panel.Children.Add(snapshot);
    }

    private Button CreateFullscreenMenuButton(string iconData, out StackPanel panel, Action<StackPanel> build)
    {
        panel = new StackPanel { Width = 272, Spacing = 16 };
        var content = new ScrollViewer
        {
            MaxHeight = 420,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = panel
        };
        var target = panel;
        var flyout = new Flyout { Placement = PlacementMode.Top, Content = content };
        flyout.Opening += (_, _) => build(target);
        var button = CreateFullscreenActionButton(CreateFullscreenIcon(iconData, 21));
        button.Flyout = flyout;
        return button;
    }

    private void SetPlaybackRate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)) return;

        _mediaPlayer.SetRate((float)rate);
        Preferences.Current.PlaybackRate = rate;
        Preferences.Save();
        UpdatePlaybackOptionChips();
    }

    private void SetAspectRatio_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;

        _mediaPlayer.AspectRatio = tag.Length == 0 ? null : tag;
        Preferences.Current.AspectRatio = tag;
        Preferences.Save();
        UpdatePlaybackOptionChips();
    }

    // Secili hiz ve oran, listedeki ilgili dugmede vurgulanir.
    private void UpdatePlaybackOptionChips()
    {
        var rate = Preferences.Current.PlaybackRate.ToString("0.##", CultureInfo.InvariantCulture);
        foreach (var chip in PlaybackRateChips.Children.OfType<Button>())
            chip.Classes.Set("active", chip.Tag as string == rate);
        foreach (var chip in AspectRatioChips.Children.OfType<Button>())
            chip.Classes.Set("active", (chip.Tag as string ?? "") == Preferences.Current.AspectRatio);
    }

    // Mini oynatici: pencere kucultulup ustte tutulur, yalnizca goruntu kalir.
    private void ToggleMiniPlayer_Click(object? sender, RoutedEventArgs e)
    {
        if (_isMiniPlayer)
        {
            ExitMiniPlayer();
            return;
        }

        if (_playingChannel is null || !PlayerView.IsVisible) return;
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);

        _miniRestoreState = WindowState;
        _miniRestorePosition = Position;
        _miniRestoreSize = new Size(Width, Height);
        _isMiniPlayer = true;

        HidePlayerOverlay();
        Sidebar.IsVisible = false;
        HeaderPanel.IsVisible = false;
        ChannelPanel.IsVisible = false;
        PlayerControls.IsVisible = false;
        MiniExitButton.IsVisible = true;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("0,*");
        ContentBody.ColumnDefinitions = new ColumnDefinitions("0,*");
        PlayerLayout.RowDefinitions = new RowDefinitions("*,0");
        SetEpgPanelVisibility(false);

        WindowState = WindowState.Normal;
        Topmost = true;
        MinWidth = 320;
        MinHeight = 200;
        Width = 480;
        Height = 300;
        var screen = Screens.Primary?.WorkingArea;
        if (screen is { } area)
            Position = new PixelPoint(area.X + area.Width - 500, area.Y + area.Height - 340);
    }

    private void ExitMiniPlayer()
    {
        if (!_isMiniPlayer) return;
        _isMiniPlayer = false;

        Topmost = false;
        MinWidth = 1120;
        MinHeight = 700;
        Width = _miniRestoreSize.Width;
        Height = _miniRestoreSize.Height;
        Position = _miniRestorePosition;
        WindowState = _miniRestoreState;

        MiniExitButton.IsVisible = false;
        Sidebar.IsVisible = true;
        HeaderPanel.IsVisible = true;
        ChannelPanel.IsVisible = true;
        PlayerControls.IsVisible = true;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("350,*");
        ContentArea.RowDefinitions = new RowDefinitions("76,*");
        ContentBody.ColumnDefinitions = new ColumnDefinitions("486,*");
        PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
    }

    private void TakeSnapshot_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingChannel is not { } channel || !PlayerView.IsVisible) return;

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BG IPTV Player");
            Directory.CreateDirectory(directory);
            var name = SanitizeFileName(channel.Name);
            var path = Path.Combine(directory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            if (_mediaPlayer.TakeSnapshot(0, path, 0, 0))
                SetStatus($"{L("Ekran görüntüsü kaydedildi")}: {path}");
            else
                SetStatus(L("Ekran görüntüsü alınamadı."));
        }
        catch (Exception ex)
        {
            AppLog.Write("Ekran görüntüsü alınamadı", ex);
            SetStatus(L("Ekran görüntüsü alınamadı."));
        }
    }

    private static string SanitizeFileName(string name)
    {
        var safe = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        return safe.Length > 60 ? safe[..60].TrimEnd() : safe;
    }

    private void OpenLogFile_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.DirectoryPath);
            if (!File.Exists(AppLog.FilePath)) AppLog.Write("Günlük dosyası oluşturuldu");
            Process.Start(new ProcessStartInfo(AppLog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write("Günlük dosyası açılamadı", ex);
        }
    }

    private void ReportIssue_Click(object? sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://github.com/berkguclukol/bg-iptv-player/issues/new");

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
public enum LibraryGroupKind { Regular, Favorites, Recent, ContinueWatching, RecentlyAdded, Collection }
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
public sealed record ChannelGroup(string Name, int Count, LibraryGroupKind Kind, string? Key = null);

public sealed record CollectionItem(string Name, int Count)
{
    public string CountText => $"{Count:N0} {Localization.T("içerik")}";
}

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
    public EpgProgrammeItem(EpgProgramme programme, DateTimeOffset now, Channel? channel = null, bool showDate = false)
    {
        Title = programme.Title;
        Description = programme.Description;
        Category = programme.Category;
        TimeText = showDate
            ? $"{programme.Start:dd.MM}\n{programme.Start:HH:mm}"
            : $"{programme.Start:HH:mm}\n{programme.Stop:HH:mm}";
        IsCurrent = programme.Start <= now && programme.Stop > now;
        Channel = channel;
        ChannelName = channel?.Name ?? "";
    }

    public Channel? Channel { get; }
    public string ChannelName { get; }
    public bool HasChannel => Channel is not null;

    public string Title { get; }
    public string? Description { get; }
    public string? Category { get; }
    public string TimeText { get; }
    public bool IsCurrent { get; }
    public string StatusText => Localization.T("ŞİMDİ YAYINDA");
    public IBrush Background => AppTheme.Brush(IsCurrent ? "Surface3Bg" : "CardBg");
    public IBrush BorderBrush => IsCurrent
        ? new SolidColorBrush(Color.Parse(Preferences.Current.AccentColor))
        : AppTheme.Brush("BorderBg");
    public IBrush TimeForeground => IsCurrent
        ? new SolidColorBrush(Color.Parse(ChannelLogo.AccentColor))
        : AppTheme.Brush("TextFaint2Bg");
}

public sealed class LibraryState
{
    public Dictionary<string, LibraryItemState> Items { get; set; } = [];
}

public sealed class LibraryItemState
{
    public ContentKind Kind { get; set; }
    public long WatchedSeconds { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastWatchedAt { get; set; }
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
}
