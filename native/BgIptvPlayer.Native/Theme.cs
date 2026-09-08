using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;

namespace BgIptvPlayer.Native;

// Arayüz renkleri tek yerden yönetilir. Pencere kaynaklarına yazıldığı için
// tema değişince tüm ekranlar anında güncellenir; kodla çizilen parçalar da
// buradaki tabloyu okur.
public static class AppTheme
{
    public const string Dark = "dark";
    public const string Light = "light";

    private static readonly Dictionary<string, string> DarkPalette = new(StringComparer.Ordinal)
    {
        ["WindowBg"] = "#0F1114",
        ["PanelBg"] = "#14171B",
        ["CardBg"] = "#171A1F",
        ["SurfaceBg"] = "#1C2026",
        ["Surface2Bg"] = "#242931",
        ["Surface3Bg"] = "#2D333C",
        ["HoverBg"] = "#191D22",
        ["BorderBg"] = "#262B33",
        ["Border2Bg"] = "#343B45",
        ["DividerBg"] = "#1F242B",
        ["TextBg"] = "#F2F4F7",
        ["TextMutedBg"] = "#A3ABB8",
        ["TextFaintBg"] = "#6B7482",
        ["TextFaint2Bg"] = "#8791A0",
        ["TrackBg"] = "#525B69",
        ["TrackHoverBg"] = "#59636F"
    };

    private static readonly Dictionary<string, string> LightPalette = new(StringComparer.Ordinal)
    {
        ["WindowBg"] = "#F4F6F9",
        ["PanelBg"] = "#FFFFFF",
        ["CardBg"] = "#FFFFFF",
        ["SurfaceBg"] = "#F0F2F6",
        ["Surface2Bg"] = "#E5E9EF",
        ["Surface3Bg"] = "#D8DDE6",
        ["HoverBg"] = "#EAEDF2",
        ["BorderBg"] = "#DFE3EA",
        ["Border2Bg"] = "#C6CDD8",
        ["DividerBg"] = "#E7EAF0",
        ["TextBg"] = "#141820",
        ["TextMutedBg"] = "#525A69",
        ["TextFaintBg"] = "#78808F",
        ["TextFaint2Bg"] = "#6C7483",
        ["TrackBg"] = "#C2C9D4",
        ["TrackHoverBg"] = "#AEB6C3"
    };

    public static string Name { get; private set; } = Dark;

    private static Dictionary<string, string> Palette => Name == Light ? LightPalette : DarkPalette;

    public static string Hex(string key) => Palette.TryGetValue(key, out var value) ? value : "#FF00FF";

    public static Color Color(string key) => Avalonia.Media.Color.Parse(Hex(key));

    public static IBrush Brush(string key) => new SolidColorBrush(Color(key));

    // Video alanı ve tam ekran kontrolleri her temada koyu kalır; görüntünün
    // yanında açık gri paneller rahatsız ediyor.
    public static string VideoHex => "#08090B";

    public static void Apply(Window window, string name)
    {
        Name = name == Light ? Light : Dark;
        foreach (var pair in Palette)
            window.Resources[pair.Key] = new SolidColorBrush(Avalonia.Media.Color.Parse(pair.Value));

        window.Background = new SolidColorBrush(Color("WindowBg"));
        window.Foreground = new SolidColorBrush(Color("TextBg"));
        window.RequestedThemeVariant = Name == Light
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
    }
}
