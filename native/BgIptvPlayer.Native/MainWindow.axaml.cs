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
    private bool _syncingFullscreenVolume;

    public MainWindow()
    {
        InitializeComponent();
        UpdateHomeDashboard();
        AboutVersionText.Text = $"Sürüm {(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 2, 0)).ToString(3)}";
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
        _mediaPlayer.Opening += (_, _) => SetStatus("Yayına bağlanılıyor...");
        _mediaPlayer.Buffering += (_, e) => SetStatus($"Yükleniyor %{e.Cache:0}");
        _mediaPlayer.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            PlaybackStatus.Text = _playingContent == ContentKind.Live && _playingChannel is { } liveChannel
                ? GetNowPlayingStatus(liveChannel)
                : "Oynatılıyor";
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
        _mediaPlayer.EncounteredError += (_, _) => SetStatus("Yayın açılamadı; kaynak çevrimdışı olabilir.");
        _mediaPlayer.TimeChanged += (_, e) => UpdateTimeline(e.Time, _mediaPlayer.Length);
        _mediaPlayer.LengthChanged += (_, e) =>
        {
            UpdateTimeline(_mediaPlayer.Time, e.Length);
            Dispatcher.UIThread.Post(() => TryResumePlayback(e.Length));
        };
        _mediaPlayer.SeekableChanged += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            Timeline.IsEnabled = _playingContent != ContentKind.Live && e.Seekable != 0 && _mediaPlayer.Length > 0;
            if (e.Seekable != 0) TryResumePlayback(_mediaPlayer.Length);
        });
        _mediaPlayer.EndReached += (_, _) => Dispatcher.UIThread.Post(MarkCurrentPlaybackCompleted);
        Closed += (_, _) =>
        {
            SaveCurrentPlaybackProgress();
            _playbackProgressTimer.Stop();
            _fullscreenControlsOverlay?.Close();
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
    }

    private static HttpClient CreateUpdateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BG-IPTV-Player/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
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

    private async void CheckForUpdatesAsync()
    {
        try
        {
            using var response = await UpdateClient.GetAsync("https://api.github.com/repos/berkguclukol/bg-iptv-player/releases/latest");
            if (!response.IsSuccessStatusCode) return;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = json.RootElement.GetProperty("tag_name").GetString();
            var url = json.RootElement.GetProperty("html_url").GetString();
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url)) return;
            if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', 2)[0], out var latest)) return;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (latest <= current) return;

            _availableUpdateUrl = url;
            UpdateTitle.Text = $"BG IPTV Player {tag} hazır";
            UpdateBanner.IsVisible = true;
        }
        catch
        {
            // Güncelleme kontrolü uygulamanın açılışını ve oynatmayı etkilemez.
        }
    }

    private void OpenUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_availableUpdateUrl)) return;
        OpenExternalUrl(_availableUpdateUrl);
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
            Title = "M3U oynatma listesi seç", AllowMultiple = false,
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
            PlaylistUrlBox.Watermark = "Geçerli bir http veya https adresi girin";
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
        if (!TryBuildXtreamUrls(server, username, password, out var playlistUrl, out var epgUrl, out var displayServer))
        {
            XtreamServerBox.Text = "";
            XtreamServerBox.Watermark = "Sunucu, kullanıcı adı ve şifreyi kontrol edin";
            return;
        }

        var name = XtreamNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = displayServer;
        var entry = AddOrActivatePlaylist(playlistUrl, name, epgUrl, PlaylistSourceKind.Xtream, displayServer);
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
        out string displayServer)
    {
        playlistUrl = "";
        epgUrl = "";
        displayServer = "";
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        if (!server.Contains("://", StringComparison.Ordinal)) server = "http://" + server;
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;

        var basePath = uri.AbsolutePath.TrimEnd('/');
        var baseUrl = uri.GetLeftPart(UriPartial.Authority) + (basePath == "/" ? "" : basePath);
        var credentials = $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";
        playlistUrl = $"{baseUrl}/get.php?{credentials}&type=m3u_plus&output=ts";
        epgUrl = $"{baseUrl}/xmltv.php?{credentials}";
        displayServer = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return true;
    }

    private async Task LoadPlaylistEntryAsync(PlaylistEntry entry, bool forceRefresh = false)
    {
        try
        {
            var sourcePath = await ResolvePlaylistPathAsync(entry, forceRefresh);
            await LoadPlaylistAsync(sourcePath, entry.Name);
            if (!string.IsNullOrWhiteSpace(entry.EpgUrl))
            {
                try
                {
                    PlaybackStatus.Text = "EPG bilgileri yükleniyor...";
                    var epgPath = await ResolveEpgPathAsync(entry, forceRefresh);
                    _epg = await Task.Run(() => ParseXmlTv(epgPath));
                    RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
                    if (_playingChannel is { } playingChannel) UpdateEpgPanel(playingChannel, EpgPanel.IsVisible);
                    PlaybackStatus.Text = $"{_channels.Count:N0} içerik · EPG hazır";
                }
                catch (Exception ex)
                {
                    _epg = new EpgSnapshot();
                    PlaybackStatus.Text = $"Liste hazır · EPG alınamadı: {ex.Message}";
                }
            }
            else
            {
                _epg = new EpgSnapshot();
            }
        }
        catch (Exception ex)
        {
            PageTitle.Text = "Liste yüklenemedi";
            PlaybackStatus.Text = $"Liste açılamadı: {ex.Message}";
        }
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
            if (!reader.Name.Equals("tv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Sunucu geçerli XMLTV verisi döndürmedi.");
        }
        return cachePath;
    }

    private async Task<string> ResolvePlaylistPathAsync(PlaylistEntry entry, bool forceRefresh)
    {
        if (!entry.IsRemote)
        {
            if (!File.Exists(entry.Path)) throw new FileNotFoundException("Oynatma listesi dosyası bulunamadı.");
            return entry.Path;
        }

        Directory.CreateDirectory(PlaylistCacheDirectory);
        var cachePath = Path.Combine(PlaylistCacheDirectory, $"{entry.Id}.m3u");
        if (!forceRefresh && IsM3uFile(cachePath)) return cachePath;

        PageTitle.Text = "Liste indiriliyor...";
        PlaybackStatus.Text = entry.Name;
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
                PlaybackStatus.Text = total > 0
                    ? $"İndiriliyor %{received * 100 / total.Value}"
                    : $"İndiriliyor {received / 1024d / 1024d:0.0} MB";
            }
        }
        if (!IsM3uFile(tempPath))
        {
            File.Delete(tempPath);
            throw new InvalidDataException("Sunucu geçerli bir M3U listesi döndürmedi.");
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
        PageTitle.Text = "Liste yükleniyor...";
        PlaybackStatus.Text = displayName ?? Path.GetFileName(path);
        try
        {
            _channels = await Task.Run(() => ParseM3u(path));
            RefreshGroups();
            var liveCount = _channels.Count(c => c.Kind == ContentKind.Live);
            var movieCount = _channels.Count(c => c.Kind == ContentKind.Movie);
            var seriesCount = _channels.Count(c => c.Kind == ContentKind.Series);
            PlaybackStatus.Text = $"{liveCount:N0} canlı · {movieCount:N0} film · {seriesCount:N0} dizi";
        }
        catch (Exception ex) { PlaybackStatus.Text = $"Liste açılamadı: {ex.Message}"; }
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
        return "İsimsiz kanal";
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
        var today = DateTime.Today;
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
            if (localStart.Date != today && localStop.Date != today) continue;

            string title = "Program bilgisi";
            using (var subtree = reader.ReadSubtree())
            {
                while (subtree.Read())
                {
                    if (subtree.NodeType != XmlNodeType.Element || !subtree.Name.Equals("title", StringComparison.OrdinalIgnoreCase)) continue;
                    title = subtree.ReadElementContentAsString().Trim();
                    break;
                }
            }

            var programme = new EpgProgramme(title, start, stop);
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
            var next = schedule.Next is { } upcoming ? $"  •  Sırada: {upcoming.Title}" : "";
            return $"Şimdi: {current.Title}{next}";
        }
        if (schedule?.Next is { } nextProgramme) return $"{nextProgramme.Start:HH:mm} · {nextProgramme.Title}";
        return channel.Group;
    }

    private string GetNowPlayingStatus(Channel channel)
    {
        var current = FindEpgSchedule(channel)?.Current;
        return current is null
            ? "Canlı yayın oynatılıyor"
            : $"Şimdi · {current.Title} · {current.Start:HH:mm}–{current.Stop:HH:mm}";
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

        var historyKind = _selectedContent == ContentKind.Live
            ? LibraryGroupKind.Recent
            : LibraryGroupKind.ContinueWatching;
        var historyChannels = GetLibraryChannels(historyKind, _selectedContent);
        if (historyChannels.Count > 0)
            groups.Add(new ChannelGroup(
                historyKind == LibraryGroupKind.Recent ? "◷ Son İzlenenler" : "▶ İzlemeye Devam Et",
                historyChannels.Count,
                historyKind));

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
        AboutPage.IsVisible = false;
        ContentArea.IsVisible = true;
        Sidebar.IsVisible = true;
        HeaderPanel.IsVisible = true;
        RootGrid.ColumnDefinitions = new ColumnDefinitions("248,*");
        LibrarySidebarTitle.Text = kind switch
        {
            ContentKind.Movie => "FİLMLER",
            ContentKind.Series => "DİZİLER",
            _ => "CANLI TV"
        };
        SetContentSection(kind);
    }

    private void BackHome_Click(object? sender, RoutedEventArgs e) => ShowHomePage();

    private void ShowHomePage()
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        OpenLibrary(_selectedContent);
    }

    private void OpenHomeSearch_Click(object? sender, RoutedEventArgs e)
    {
        OpenLibrary(ContentKind.Live);
        Dispatcher.UIThread.Post(() => SearchBox.Focus());
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

    private void SetContentSection(ContentKind kind)
    {
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
        _ => "Canlı TV"
    };

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
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
        BrowserTitle.Text = _selectedContent == ContentKind.Movie ? "FİLMLER" : "KANALLAR";
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
                LibraryGroupKind.ContinueWatching => $"Kaldığın yer · {FormatTime(state?.PositionMs ?? 0)}",
                _ => channel.Group
            };

            return channel.Kind == ContentKind.Series
                ? MediaBrowserItem.FromEpisode(channel, subtitle, progressBadge)
                : MediaBrowserItem.FromChannel(
                    channel,
                    subtitle,
                    progressBadge,
                    _selectedGroupKind == LibraryGroupKind.Recent);
        }).ToList();

        BrowserTitle.Text = _selectedGroupKind switch
        {
            LibraryGroupKind.Favorites => "FAVORİLER",
            LibraryGroupKind.Recent => "SON İZLENENLER",
            _ => "İZLEMEYE DEVAM ET"
        };
        SeriesBackButton.IsVisible = false;
        ClearHistoryButton.IsVisible = _selectedGroupKind == LibraryGroupKind.Recent && channels.Count > 0;
        ChannelCount.Text = $"{channels.Count:N0} içerik";
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
            BrowserTitle.Text = "DİZİLER";
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
        BrowserTitle.Text = _selectedSeriesSeason.HasValue ? $"SEZON {_selectedSeriesSeason}" : "DİĞER BÖLÜMLER";
        SeriesBackButton.IsVisible = true;
        ChannelCount.Text = $"{episodes.Count:N0} bölüm";
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
        _mediaPlayer.Stop();
        PlayerPlaceholder.IsVisible = false;
        PlayerView.IsVisible = true;
        _media?.Dispose();
        _media = new Media(_libVlc, new Uri(channel.Url));
        _media.AddOption(":network-caching=1800");
        _media.AddOption(":http-reconnect");
        NowPlaying.Text = channel.Name;
        PlaybackKindBadge.Text = channel.Badge;
        _playingContent = channel.Kind;
        _playingChannel = channel;
        UpdateEpgPanel(channel, channel.Kind == ContentKind.Live);
        _historyRecordedForCurrentPlayback = false;
        _pendingResumePosition = GetResumePosition(channel);
        UpdateFavoriteButton();
        PlaybackStatus.Text = "Yayına bağlanılıyor...";
        PlayPauseButton.IsEnabled = true;
        PlayPauseIcon.Data = Avalonia.Media.Geometry.Parse("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z");
        Timeline.Value = 0;
        Timeline.IsEnabled = false;
        TimelinePanel.IsVisible = false;
        if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,82");
        TimeLabel.Text = "CANLI";
        _mediaPlayer.Play(_media);
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
        var programmes = FindEpgSchedule(channel)?.Programs ?? [];
        var items = programmes.Select(programme => new EpgProgrammeItem(programme, DateTimeOffset.Now)).ToList();
        EpgProgrammeList.ItemsSource = items;
        EpgProgrammeList.IsVisible = items.Count > 0;
        EpgEmptyText.IsVisible = items.Count == 0;
        if (showPanel) SetEpgPanelVisibility(true);

        if (items.FindIndex(item => item.IsCurrent) is var currentIndex && currentIndex >= 0)
            Dispatcher.UIThread.Post(() => EpgProgrammeList.ScrollIntoView(currentIndex));
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
        ToolTip.SetTip(FavoriteButton, isFavorite ? "Favorilerden çıkar" : "Favorilere ekle");
    }

    private void RemoveRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Channel channel }) return;
        var state = FindLibraryItem(channel);
        if (state is null) return;

        state.LastWatchedAt = null;
        CleanupLibraryItem(channel.Id, state);
        SaveLibraryState();
        RefreshGroups(preserveSelection: true, resetSeriesBrowser: false);
        e.Handled = true;
    }

    private void ClearRecentHistory_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var pair in _libraryState.Items
                     .Where(pair => pair.Value.Kind == ContentKind.Live && pair.Value.LastWatchedAt.HasValue)
                     .ToList())
        {
            pair.Value.LastWatchedAt = null;
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
    }

    private void VolumeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_mediaPlayer is not null) _mediaPlayer.Volume = (int)e.NewValue;
        if (e.NewValue > 0) _lastAudibleVolume = e.NewValue;
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

    private void UpdateTimeline(long time, long length) => Dispatcher.UIThread.Post(() =>
    {
        if (_playingContent == ContentKind.Live || length <= 0 || !_mediaPlayer.IsSeekable)
        {
            Timeline.IsEnabled = false;
            TimelinePanel.IsVisible = false;
            if (_fullscreenTimelinePanel is not null) _fullscreenTimelinePanel.IsVisible = false;
            if (_fullscreenTimeline is not null) _fullscreenTimeline.IsEnabled = false;
            if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,82");
            TimeLabel.Text = "CANLI";
            if (_fullscreenTimeLabel is not null) _fullscreenTimeLabel.Text = "CANLI";
            return;
        }

        TimelinePanel.IsVisible = true;
        if (_fullscreenTimelinePanel is not null) _fullscreenTimelinePanel.IsVisible = true;
        if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,112");
        Timeline.Maximum = length;
        Timeline.IsEnabled = _mediaPlayer.IsSeekable;
        if (_fullscreenTimeline is not null)
        {
            _fullscreenTimeline.Maximum = length;
            _fullscreenTimeline.IsEnabled = _mediaPlayer.IsSeekable;
        }
        if (!_isSeeking)
        {
            Timeline.Value = Math.Clamp(time, 0, length);
            if (_fullscreenTimeline is not null) _fullscreenTimeline.Value = Math.Clamp(time, 0, length);
        }
        TimeLabel.Text = $"{FormatTime(time)} / {FormatTime(length)}";
        if (_fullscreenTimeLabel is not null) _fullscreenTimeLabel.Text = TimeLabel.Text;
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
        if (channel.Kind == ContentKind.Live)
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
        PlaybackStatus.Text = $"Kaldığınız yerden devam ediyor · {FormatTime(resumeAt)}";
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
        if (!timestamp.HasValue) return "az önce";
        var elapsed = DateTimeOffset.UtcNow - timestamp.Value;
        if (elapsed.TotalMinutes < 1) return "az önce";
        if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} dk önce";
        if (elapsed.TotalDays < 1) return $"{Math.Max(1, (int)elapsed.TotalHours)} sa önce";
        return $"{Math.Max(1, (int)elapsed.TotalDays)} gün önce";
    }

    private static string FormatTime(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }

    private void Fullscreen_Click(object? sender, RoutedEventArgs e) => SetPlayerFullscreen(!_isPlayerFullscreen);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_isPlayerFullscreen) return;
        SetPlayerFullscreen(false);
        e.Handled = true;
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
            Foreground = new SolidColorBrush(Color.Parse("#AAA2B2")),
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
        var playPauseButton = CreateFullscreenActionButton(_fullscreenPlayPauseIcon, true);
        playPauseButton.Click += PlayPause_Click;
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
            Background = new SolidColorBrush(Color.Parse("#EE14111E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#5A4D70")),
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
            if (e.Key != Key.Escape) return;
            SetPlayerFullscreen(false);
            e.Handled = true;
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

    private static Button CreateFullscreenActionButton(Control content, bool primary = false) => new()
    {
        Width = 46,
        Height = 46,
        Padding = new Avalonia.Thickness(12),
        CornerRadius = new Avalonia.CornerRadius(23),
        BorderThickness = new Avalonia.Thickness(0),
        Background = new SolidColorBrush(Color.Parse(primary ? "#7B61FF" : "#2A2536")),
        Content = content
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
            RootGrid.ColumnDefinitions = new ColumnDefinitions("248,*");
            ContentArea.Margin = new Avalonia.Thickness(24, 20, 24, 24);
            ContentArea.RowDefinitions = new RowDefinitions("Auto,*");
            ContentBody.ColumnDefinitions = new ColumnDefinitions("330,*");
            ContentBody.ColumnSpacing = 16;
            Grid.SetColumn(PlayerPanel, 1);
            Grid.SetColumnSpan(PlayerPanel, 1);
            PlayerPanel.CornerRadius = new Avalonia.CornerRadius(14);
            PlayerPanel.BorderThickness = new Avalonia.Thickness(1);
            PlayerLayout.RowDefinitions = TimelinePanel.IsVisible ? new RowDefinitions("*,112") : new RowDefinitions("*,82");
            PlayerControls.IsVisible = true;
            if (_epgPanelWasVisibleBeforeFullscreen && _playingChannel is { Kind: ContentKind.Live } channel)
                UpdateEpgPanel(channel, true);
            _fullscreenControlsTimer.Stop();
            Sidebar.IsVisible = true;
            HeaderPanel.IsVisible = true;
            ChannelPanel.IsVisible = true;
            PlaybackStatus.Text = _mediaPlayer.IsPlaying && _playingChannel is { } playingChannel
                ? (_playingContent == ContentKind.Live ? GetNowPlayingStatus(playingChannel) : "Oynatılıyor")
                : "Hazır";
        }
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => PlaybackStatus.Text = text);

    private void ShowSettings_Click(object? sender, RoutedEventArgs e)
    {
        ShowSettings();
    }
    private void ShowSettingsFromHome_Click(object? sender, RoutedEventArgs e)
    {
        ShowSettings();
    }
    private void HideSettings_Click(object? sender, RoutedEventArgs e) => HideSettings();

    private void ShowSettings()
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        RefreshPlaylistSettingsView();
        HomePage.IsVisible = false;
        ContentArea.IsVisible = false;
        AboutPage.IsVisible = false;
        SettingsPage.IsVisible = true;
    }

    private void HideSettings()
    {
        SettingsPage.IsVisible = false;
        AboutPage.IsVisible = false;
        OpenLibrary(_selectedContent);
    }

    private void ShowAbout_Click(object? sender, RoutedEventArgs e)
    {
        ShowAbout();
    }

    private void ShowAboutFromHome_Click(object? sender, RoutedEventArgs e)
    {
        ShowAbout();
    }

    private void ShowAbout()
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        HomePage.IsVisible = false;
        ContentArea.IsVisible = false;
        SettingsPage.IsVisible = false;
        AboutPage.IsVisible = true;
    }

    private void HideAbout_Click(object? sender, RoutedEventArgs e)
    {
        AboutPage.IsVisible = false;
        OpenLibrary(_selectedContent);
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

public sealed record Channel(string Name, string Url, string Group, string? LogoUrl, ContentKind Kind, string? TvgId)
{
    public string Id { get; } = CreateStableId(Url);
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0])).ToUpperInvariant();
    public string Badge => Kind switch { ContentKind.Movie => "FİLM", ContentKind.Series => "DİZİ", _ => "CANLI" };
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

    public static MediaBrowserItem FromEpisode(Channel channel, string? subtitle = null, string? badge = null) => new()
    {
        Kind = MediaBrowserItemKind.Episode,
        Name = channel.Name,
        Subtitle = subtitle ?? (channel.Series.Season.HasValue ? $"Sezon {channel.Series.Season}" : "Diğer bölümler"),
        Badge = badge ?? (channel.Series.Episode.HasValue ? $"BÖLÜM {channel.Series.Episode}" : "OYNAT"),
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
            Subtitle = $"{group.Count():N0} bölüm",
            Badge = "DİZİ  ›",
            LogoUrl = group.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.LogoUrl))?.LogoUrl ?? first.LogoUrl,
            SeriesTitle = group.Key
        };
    }

    public static MediaBrowserItem FromSeason(string seriesTitle, int? season, int episodeCount, string? logoUrl) => new()
    {
        Kind = MediaBrowserItemKind.Season,
        Name = season.HasValue ? $"Sezon {season}" : "Diğer Bölümler",
        Subtitle = $"{episodeCount:N0} bölüm",
        Badge = "AÇ  ›",
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
    public bool IsActive { get; set; }
    public bool IsRemote => Uri.TryCreate(Path, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    public bool IsXtream => SourceKind == PlaylistSourceKind.Xtream;
    public string SourceTypeText => IsXtream ? "XTREAM" : IsRemote ? "URL" : "DOSYA";
    public string DisplayPath => IsXtream ? $"{DisplayServer ?? "Xtream sunucusu"} · kullanıcı bilgileri gizli" : Path;
    public string ActiveText => IsActive ? "✓ AKTİF" : "Etkinleştir";
}

public sealed record EpgProgramme(string Title, DateTimeOffset Start, DateTimeOffset Stop);

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
        TimeText = $"{programme.Start:HH:mm}\n{programme.Stop:HH:mm}";
        IsCurrent = programme.Start <= now && programme.Stop > now;
    }

    public string Title { get; }
    public string TimeText { get; }
    public bool IsCurrent { get; }
    public string StatusText => "ŞİMDİ YAYINDA";
    public IBrush Background => new SolidColorBrush(Color.Parse(IsCurrent ? "#38243A" : "#1B1723"));
    public IBrush BorderBrush => new SolidColorBrush(Color.Parse(IsCurrent ? "#FF805F" : "#302A3B"));
    public IBrush TimeForeground => new SolidColorBrush(Color.Parse(IsCurrent ? "#FF9A7B" : "#9D94A7"));
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
