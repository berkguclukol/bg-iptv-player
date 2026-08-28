using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
    private static readonly HttpClient PlaylistClient = CreatePlaylistClient();
    private static readonly HttpClient UpdateClient = CreateUpdateClient();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _fullscreenControlsTimer;
    private Media? _media;
    private List<Channel> _channels = [];
    private List<PlaylistEntry> _playlists = [];
    private string _selectedGroup = "Tümü";
    private ContentKind _selectedContent = ContentKind.Live;
    private ContentKind _playingContent = ContentKind.Live;
    private bool _isPlayerFullscreen;
    private bool _isSeeking;
    private double _lastAudibleVolume = 80;
    private string? _availableUpdateUrl;
    private WindowState _previousWindowState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        AboutVersionText.Text = $"Sürüm {(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 1)).ToString(3)}";
        Timeline.AddHandler(PointerPressedEvent, Timeline_PointerPressed, RoutingStrategies.Tunnel, true);
        Timeline.AddHandler(PointerReleasedEvent, Timeline_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        Core.Initialize();
        _libVlc = new LibVLC("--network-caching=1800", "--http-reconnect", "--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.Volume = 80;
        PlayerView.MediaPlayer = _mediaPlayer;
        PlayerView.VideoDoubleClicked += (_, _) => SetPlayerFullscreen(!_isPlayerFullscreen);
        PlayerView.EscapePressed += (_, _) => SetPlayerFullscreen(false);
        PlayerView.VideoMouseMoved += (_, _) => ShowFullscreenControls();
        _fullscreenControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _fullscreenControlsTimer.Tick += (_, _) => HideFullscreenControls();
        _mediaPlayer.Opening += (_, _) => SetStatus("Yayına bağlanılıyor...");
        _mediaPlayer.Buffering += (_, e) => SetStatus($"Yükleniyor %{e.Cache:0}");
        _mediaPlayer.Playing += (_, _) => SetStatus("Canlı yayın oynatılıyor");
        _mediaPlayer.EncounteredError += (_, _) => SetStatus("Yayın açılamadı; kaynak çevrimdışı olabilir.");
        _mediaPlayer.TimeChanged += (_, e) => UpdateTimeline(e.Time, _mediaPlayer.Length);
        _mediaPlayer.LengthChanged += (_, e) => UpdateTimeline(_mediaPlayer.Time, e.Length);
        _mediaPlayer.SeekableChanged += (_, e) => Dispatcher.UIThread.Post(() => Timeline.IsEnabled = _playingContent != ContentKind.Live && e.Seekable != 0 && _mediaPlayer.Length > 0);
        Closed += (_, _) => { _media?.Dispose(); _mediaPlayer.Dispose(); _libVlc.Dispose(); };

        _playlists = LoadPlaylistSettings();
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

    private async Task LoadPlaylistEntryAsync(PlaylistEntry entry, bool forceRefresh = false)
    {
        try
        {
            var sourcePath = await ResolvePlaylistPathAsync(entry, forceRefresh);
            await LoadPlaylistAsync(sourcePath, entry.Name);
        }
        catch (Exception ex)
        {
            PageTitle.Text = "Liste yüklenemedi";
            PlaybackStatus.Text = $"Liste açılamadı: {ex.Message}";
        }
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
            result.Add(new Channel(name, url, group, ReadAttribute(info, "tvg-logo"), ClassifyContent(url, group, name)));
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

    private static ContentKind ClassifyContent(string url, string group, string name)
    {
        var source = $"{group} {name}".ToUpperInvariant();
        var address = url.ToLowerInvariant();
        if (address.Contains("/series/") || ContainsAny(source, "DİZİ", "DIZI", "SERIES", "TV SHOW", "SEZON", "SEASON")) return ContentKind.Series;
        if (address.Contains("/movie/") || ContainsAny(source, "FİLM", "FILM", "MOVIE", "SİNEMA", "SINEMA", "VOD")) return ContentKind.Movie;
        return ContentKind.Live;
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);

    private void RefreshGroups()
    {
        _selectedGroup = "Tümü";
        var sectionChannels = _channels.Where(c => c.Kind == _selectedContent).ToList();
        var groups = sectionChannels.GroupBy(c => c.Group).Select(g => new ChannelGroup(g.Key, g.Count())).OrderBy(g => g.Name).ToList();
        groups.Insert(0, new ChannelGroup("Tümü", sectionChannels.Count));
        GroupList.ItemsSource = groups;
        GroupList.SelectedIndex = 0;
        PageTitle.Text = ContentTitle(_selectedContent);
        ApplyFilter();
    }

    private void GroupList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GroupList.SelectedItem is not ChannelGroup group) return;
        _selectedGroup = group.Name;
        PageTitle.Text = group.Name == "Tümü" ? ContentTitle(_selectedContent) : group.Name;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void LiveSection_Click(object? sender, RoutedEventArgs e) => SetContentSection(ContentKind.Live);
    private void MovieSection_Click(object? sender, RoutedEventArgs e) => SetContentSection(ContentKind.Movie);
    private void SeriesSection_Click(object? sender, RoutedEventArgs e) => SetContentSection(ContentKind.Series);

    private void SetContentSection(ContentKind kind)
    {
        if (_selectedContent == kind) return;
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
        var visible = _channels.Where(c => c.Kind == _selectedContent && (_selectedGroup == "Tümü" || c.Group == _selectedGroup) && (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
        ChannelList.ItemsSource = visible;
        ChannelCount.Text = $"{visible.Count:N0} kanal";
    }

    private void ChannelList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not Channel channel) return;
        _mediaPlayer.Stop();
        _media?.Dispose();
        _media = new Media(_libVlc, new Uri(channel.Url));
        _media.AddOption(":network-caching=1800");
        _media.AddOption(":http-reconnect");
        NowPlaying.Text = channel.Name;
        _playingContent = channel.Kind;
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

    private void PlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            PlayPauseIcon.Data = Avalonia.Media.Geometry.Parse("M4,2 L18,10 L4,18 Z");
        }
        else
        {
            _mediaPlayer.Play();
            PlayPauseIcon.Data = Avalonia.Media.Geometry.Parse("M3,2 L7,2 L7,18 L3,18 Z M13,2 L17,2 L17,18 L13,18 Z");
        }
    }

    private void VolumeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_mediaPlayer is not null) _mediaPlayer.Volume = (int)e.NewValue;
        if (e.NewValue > 0) _lastAudibleVolume = e.NewValue;
        if (VolumeWaveIcon is not null) VolumeWaveIcon.IsVisible = e.NewValue > 0;
        if (VolumeMutedIcon is not null) VolumeMutedIcon.IsVisible = e.NewValue <= 0;
    }

    private void VolumeButton_Click(object? sender, RoutedEventArgs e) =>
        VolumeSlider.Value = VolumeSlider.Value > 0 ? 0 : Math.Max(1, _lastAudibleVolume);

    private void Timeline_PointerPressed(object? sender, PointerPressedEventArgs e) => _isSeeking = true;

    private void Timeline_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Timeline.IsEnabled && Timeline.Maximum > 0)
            _mediaPlayer.Position = (float)Math.Clamp(Timeline.Value / Timeline.Maximum, 0, 1);
        _isSeeking = false;
    }

    private void UpdateTimeline(long time, long length) => Dispatcher.UIThread.Post(() =>
    {
        if (_playingContent == ContentKind.Live || length <= 0 || !_mediaPlayer.IsSeekable)
        {
            Timeline.IsEnabled = false;
            TimelinePanel.IsVisible = false;
            if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,82");
            TimeLabel.Text = "CANLI";
            return;
        }

        TimelinePanel.IsVisible = true;
        if (!_isPlayerFullscreen) PlayerLayout.RowDefinitions = new RowDefinitions("*,112");
        Timeline.Maximum = length;
        Timeline.IsEnabled = _mediaPlayer.IsSeekable;
        if (!_isSeeking) Timeline.Value = Math.Clamp(time, 0, length);
        TimeLabel.Text = $"{FormatTime(time)} / {FormatTime(length)}";
    });

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

    private void Window_PointerMoved(object? sender, PointerEventArgs e) => ShowFullscreenControls();

    private void ShowFullscreenControls()
    {
        if (!_isPlayerFullscreen) return;
        PlayerControls.IsVisible = true;
        PlayerLayout.RowDefinitions = TimelinePanel.IsVisible ? new RowDefinitions("*,112") : new RowDefinitions("*,82");
        _fullscreenControlsTimer.Stop();
        _fullscreenControlsTimer.Start();
    }

    private void HideFullscreenControls()
    {
        _fullscreenControlsTimer.Stop();
        if (!_isPlayerFullscreen) return;
        PlayerControls.IsVisible = false;
        PlayerLayout.RowDefinitions = new RowDefinitions("*,0");
    }

    private void SetPlayerFullscreen(bool fullscreen)
    {
        if (_isPlayerFullscreen == fullscreen) return;
        _isPlayerFullscreen = fullscreen;

        if (fullscreen)
        {
            _previousWindowState = WindowState;
            Sidebar.IsVisible = false;
            HeaderPanel.IsVisible = false;
            ChannelPanel.IsVisible = false;
            RootGrid.ColumnDefinitions = new ColumnDefinitions("0,*");
            ContentArea.Margin = new Avalonia.Thickness(0);
            ContentBody.ColumnDefinitions = new ColumnDefinitions("0,*");
            ContentBody.ColumnSpacing = 0;
            Grid.SetColumn(PlayerPanel, 0);
            Grid.SetColumnSpan(PlayerPanel, 2);
            PlayerPanel.CornerRadius = new Avalonia.CornerRadius(0);
            PlayerPanel.BorderThickness = new Avalonia.Thickness(0);
            PlayerControls.IsVisible = false;
            PlayerLayout.RowDefinitions = new RowDefinitions("*,0");
            _fullscreenControlsTimer.Stop();
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowState = _previousWindowState == WindowState.FullScreen ? WindowState.Normal : _previousWindowState;
            RootGrid.ColumnDefinitions = new ColumnDefinitions("248,*");
            ContentArea.Margin = new Avalonia.Thickness(38, 30);
            ContentBody.ColumnDefinitions = new ColumnDefinitions("380,*");
            ContentBody.ColumnSpacing = 20;
            Grid.SetColumn(PlayerPanel, 1);
            Grid.SetColumnSpan(PlayerPanel, 1);
            PlayerPanel.CornerRadius = new Avalonia.CornerRadius(15);
            PlayerPanel.BorderThickness = new Avalonia.Thickness(1);
            PlayerLayout.RowDefinitions = TimelinePanel.IsVisible ? new RowDefinitions("*,112") : new RowDefinitions("*,82");
            PlayerControls.IsVisible = true;
            _fullscreenControlsTimer.Stop();
            Sidebar.IsVisible = true;
            HeaderPanel.IsVisible = true;
            ChannelPanel.IsVisible = true;
            PlaybackStatus.Text = _mediaPlayer.IsPlaying ? "Canlı yayın oynatılıyor" : "Hazır";
        }
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => PlaybackStatus.Text = text);

    private void ShowSettings_Click(object? sender, RoutedEventArgs e) => ShowSettings();
    private void HideSettings_Click(object? sender, RoutedEventArgs e) => HideSettings();

    private void ShowSettings()
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        RefreshPlaylistSettingsView();
        ContentArea.IsVisible = false;
        AboutPage.IsVisible = false;
        SettingsPage.IsVisible = true;
    }

    private void HideSettings()
    {
        SettingsPage.IsVisible = false;
        AboutPage.IsVisible = false;
        ContentArea.IsVisible = true;
    }

    private void ShowAbout_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPlayerFullscreen) SetPlayerFullscreen(false);
        ContentArea.IsVisible = false;
        SettingsPage.IsVisible = false;
        AboutPage.IsVisible = true;
    }

    private void HideAbout_Click(object? sender, RoutedEventArgs e)
    {
        AboutPage.IsVisible = false;
        ContentArea.IsVisible = true;
    }

    private void OpenGithub_Click(object? sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://github.com/berkguclukol/bg-iptv-player");

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

    private PlaylistEntry AddOrActivatePlaylist(string path, string? name = null)
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
                IsActive = true
            };
            _playlists.Add(entry);
        }
        else entry.IsActive = true;
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
        return [];
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

public sealed record Channel(string Name, string Url, string Group, string? LogoUrl, ContentKind Kind)
{
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0])).ToUpperInvariant();
    public string Badge => Kind switch { ContentKind.Movie => "FİLM", ContentKind.Series => "DİZİ", _ => "CANLI" };
}
public sealed record ChannelGroup(string Name, int Count);

public sealed class PlaylistEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsRemote => Uri.TryCreate(Path, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    public string SourceTypeText => IsRemote ? "URL" : "DOSYA";
    public string ActiveText => IsActive ? "✓ AKTİF" : "Etkinleştir";
}
