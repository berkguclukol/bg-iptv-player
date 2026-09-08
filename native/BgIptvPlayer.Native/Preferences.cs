using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BgIptvPlayer.Native;

// Kullanıcı tercihlerinin tek kaynağı. Oynatma listeleri ve izleme geçmişi
// kendi dosyalarında tutulur; burada yalnızca uygulama ayarları vardır.
public sealed class AppPreferences
{
    public string Language { get; set; } = Localization.Turkish;
    public bool ResumeLastChannel { get; set; }
    public string? LastChannelId { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
    public string AspectRatio { get; set; } = "";
    public string AccentColor { get; set; } = "#F2622E";
    public string Theme { get; set; } = "dark";
    public string? TmdbApiKey { get; set; }
    public string? DisplayName { get; set; }
    public int SubtitleFontSize { get; set; } = 16;
    public bool AutoPlayNextEpisode { get; set; } = true;
    public bool BackgroundRefresh { get; set; } = true;
    public string SortMode { get; set; } = "default";
    public string QualityFilter { get; set; } = "";
    public List<string> HiddenGroups { get; set; } = [];
    public List<string> LockedGroups { get; set; } = [];
    public string? ParentalPinHash { get; set; }
}

public static class Preferences
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "preferences.json");

    private static AppPreferences? _current;

    public static AppPreferences Current => _current ??= Load();

    private static AppPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath)) ?? new AppPreferences();
        }
        catch (Exception ex)
        {
            AppLog.Write("Tercihler okunamadı", ex);
        }

        return new AppPreferences();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Write("Tercihler kaydedilemedi", ex);
        }
    }
}
