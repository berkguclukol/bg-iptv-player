using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace BgIptvPlayer.Native;

public sealed class ChannelLogo : Grid
{
    public static readonly StyledProperty<string?> LogoUrlProperty =
        AvaloniaProperty.Register<ChannelLogo, string?>(nameof(LogoUrl));

    public static readonly StyledProperty<string?> InitialsProperty =
        AvaloniaProperty.Register<ChannelLogo, string?>(nameof(Initials));

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Image _image;
    private readonly TextBlock _initials;
    private int _loadVersion;

    public ChannelLogo()
    {
        ClipToBounds = true;
        Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.Parse("#332C70")),
            BorderBrush = new SolidColorBrush(Color.Parse("#5145A8")),
            BorderThickness = new Thickness(1)
        });

        _initials = new TextBlock
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#D9D5FF"))
        };
        Children.Add(_initials);

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(4),
            IsVisible = false
        };
        Children.Add(_image);
    }

    public string? LogoUrl
    {
        get => GetValue(LogoUrlProperty);
        set => SetValue(LogoUrlProperty, value);
    }

    public string? Initials
    {
        get => GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == InitialsProperty)
            _initials.Text = Initials;
        else if (change.Property == LogoUrlProperty)
            _ = LoadLogoAsync(LogoUrl, ++_loadVersion);
    }

    private async Task LoadLogoAsync(string? url, int version)
    {
        _image.IsVisible = false;
        _image.Source = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;

        try
        {
            var bitmap = await Cache.GetOrAdd(uri.AbsoluteUri, DownloadAsync);
            if (bitmap is null || version != _loadVersion) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _loadVersion) return;
                _image.Source = bitmap;
                _image.IsVisible = true;
            });
        }
        catch
        {
            // The styled initials tile intentionally remains visible as fallback.
        }
    }

    private static async Task<Bitmap?> DownloadAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) return null;
            return new Bitmap(new MemoryStream(bytes));
        }
        catch { return null; }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BG-IPTV-Player/1.0");
        return client;
    }
}
