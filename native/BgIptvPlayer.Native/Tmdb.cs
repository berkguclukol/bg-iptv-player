using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BgIptvPlayer.Native;

public sealed record TmdbDetails(string Title, string? Year, string? Overview, double Rating, string? PosterUrl);

// TMDB'den film ve dizi bilgisi çeker. Anahtar kullanıcıya ait olduğu için
// ayarlardan girilir; anahtar yoksa özellik tamamen kapalıdır.
public static class Tmdb
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex NoisePattern = new(
        @"\b(4K|UHD|FHD|HD|SD|TR|EN|DUAL|ALTYAZILI|DUBLAJ|IMDB|VIP|RAW)\b|\(\d{4}\)|\[[^\]]*\]|\d{3,4}p",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BG-IPTV-Player/1.0");
        return client;
    }

    public static bool IsConfigured => Preferences.Current.TmdbApiKey is { Length: > 8 };

    // Liste adlarında kalite ve dil etiketleri bulunduğu için arama öncesi temizlenir.
    public static string CleanTitle(string name)
    {
        var cleaned = NoisePattern.Replace(name, " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '·', '|', ':');
        return cleaned.Length == 0 ? name.Trim() : cleaned;
    }

    public static async Task<TmdbDetails?> SearchAsync(string name, bool isSeries)
    {
        if (!IsConfigured) return null;

        var query = CleanTitle(name);
        var kind = isSeries ? "tv" : "movie";
        var url = $"https://api.themoviedb.org/3/search/{kind}" +
                  $"?api_key={Uri.EscapeDataString(Preferences.Current.TmdbApiKey!)}" +
                  $"&language={(Localization.Language == Localization.English ? "en-US" : "tr-TR")}" +
                  $"&query={Uri.EscapeDataString(query)}";

        try
        {
            using var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Write($"TMDB yanıtı başarısız: {(int)response.StatusCode}");
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!json.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
                return null;

            var first = results[0];
            var title = ReadString(first, isSeries ? "name" : "title") ?? query;
            var date = ReadString(first, isSeries ? "first_air_date" : "release_date");
            var poster = ReadString(first, "poster_path");
            var rating = first.TryGetProperty("vote_average", out var vote) && vote.TryGetDouble(out var value) ? value : 0;

            return new TmdbDetails(
                title,
                date is { Length: >= 4 } ? date[..4] : null,
                ReadString(first, "overview"),
                rating,
                poster is { Length: > 1 } ? $"https://image.tmdb.org/t/p/w342{poster}" : null);
        }
        catch (Exception ex)
        {
            AppLog.Write("TMDB araması başarısız", ex);
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string FormatRating(double rating) =>
        rating <= 0 ? "" : rating.ToString("0.0", CultureInfo.InvariantCulture);
}
