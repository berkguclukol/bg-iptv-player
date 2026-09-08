using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

    // Yer tutucudaki bas harf ve filigran rengi, uygulamanin vurgu rengini takip eder.
    public static string AccentColor { get; set; } = "#FF8A5C";

    // Acik temada yer tutucu kartinin zemini de acilir.
    public static bool PlaceholderIsLight { get; set; }
    public static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer", "logos");
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Image _image;
    private readonly TextBlock _initials;
    private readonly Border _placeholder;
    private readonly Viewbox _watermark;
    private int _loadVersion;

    public ChannelLogo()
    {
        ClipToBounds = true;

        _initials = new TextBlock
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse(AccentColor))
        };

        // Logosu olmayan kanallar icin cizilen yer tutucu: koseden tasan
        // yayin dalgasi filigrani ve ortada kanalin bas harfleri.
        _watermark = new Viewbox
        {
            Width = 42,
            Height = 42,
            Opacity = 0.5,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -10, -10),
            Child = CreateWatermark()
        };

        _placeholder = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.Parse(PlaceholderIsLight ? "#C6CDD8" : "#343B45")),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(PlaceholderIsLight ? "#EEF1F5" : "#252B34"), 0),
                    new GradientStop(Color.Parse(PlaceholderIsLight ? "#DFE4EB" : "#171B21"), 1)
                }
            },
            Child = new Grid { Children = { _watermark, _initials } }
        };
        Children.Add(_placeholder);

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            IsVisible = false
        };
        Children.Add(_image);
    }

    private static Canvas CreateWatermark()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(Color.Parse(AccentColor));
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M4,9 L20,9 L20,20 L4,20 Z"),
            Stroke = stroke,
            StrokeThickness = 1.5,
            StrokeJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Opacity = 0.55
        });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M8,5.5 L12,9 L16,5.5"),
            Stroke = stroke,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Opacity = 0.55
        });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M8,14.5 C9.4,13.1 11.6,13.1 13,14.5 M6,12.5 C8.5,10 12.5,10 15,12.5"),
            Stroke = stroke,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            Fill = null,
            Opacity = 0.35
        });
        return canvas;
    }

    // Yer tutucu her boyutta ayni oranda gorunsun diye olculer denetimin
    // boyutuna gore hesaplanir.
    protected override Size ArrangeOverride(Size finalSize)
    {
        var shortest = Math.Min(finalSize.Width, finalSize.Height);
        if (shortest > 0)
        {
            _placeholder.CornerRadius = new CornerRadius(Math.Clamp(shortest * 0.22, 8, 16));
            _initials.FontSize = Math.Clamp(shortest * 0.33, 10, 20);
            var watermark = Math.Clamp(shortest * 0.78, 22, 58);
            _watermark.Width = watermark;
            _watermark.Height = watermark;
            _watermark.Margin = new Thickness(0, 0, -watermark * 0.22, -watermark * 0.22);
        }

        return base.ArrangeOverride(finalSize);
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
        _placeholder.IsVisible = true;
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
                _placeholder.IsVisible = false;
            });
        }
        catch
        {
            // The styled initials tile intentionally remains visible as fallback.
        }
    }

    // Logolar diske de yazilir; boylece her acilista yeniden indirilmez.
    private static async Task<Bitmap?> DownloadAsync(string url)
    {
        var cachePath = GetCachePath(url);
        try
        {
            if (File.Exists(cachePath))
                return new Bitmap(cachePath);
        }
        catch
        {
            try { File.Delete(cachePath); } catch { }
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) return null;

            var bitmap = new Bitmap(new MemoryStream(bytes));
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                await File.WriteAllBytesAsync(cachePath, bytes);
            }
            catch (Exception ex)
            {
                AppLog.Write("Logo önbelleğe yazılamadı", ex);
            }

            return bitmap;
        }
        catch { return null; }
    }

    private static string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDirectory, hash + ".img");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BG-IPTV-Player/1.0");
        return client;
    }
}
