using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BgIptvPlayer.Native;

/// <summary>
/// Geçmiş yayınların adresini kurar. Sağlayıcılar aynı işi üç ayrı biçimde
/// sunduğu için sırayla denenir: listede hazır şablon varsa o, yoksa Xtream
/// timeshift ucu, o da yoksa canlı adrese zaman parametresi eklenir.
/// </summary>
public static class Catchup
{
    /// <summary>Sağlayıcı arşivi çok kısa programlarda saniyeye kadar bölmez; alt sınır konur.</summary>
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(1);

    public static bool IsSupported(Channel channel, PlaylistEntry? playlist) =>
        channel.HasCatchup &&
        (channel.CatchupSource is { Length: > 0 } ||
         (playlist is { HasXtreamCredentials: true } && channel.StreamId is { Length: > 0 }) ||
         channel.Url.Length > 0);

    /// <summary>
    /// Verilen an için oynatılabilir bir adres üretir. <paramref name="alternate"/>
    /// true ise Xtream sunucularının ikinci adres biçimi denenir; ilk biçim
    /// hata verdiğinde yeniden deneme bunu kullanır.
    /// </summary>
    public static string? BuildUrl(
        Channel channel,
        PlaylistEntry? playlist,
        DateTimeOffset start,
        TimeSpan duration,
        bool alternate = false)
    {
        if (!channel.HasCatchup) return null;
        if (duration < MinimumDuration) duration = MinimumDuration;

        var earliest = channel.CatchupStart;
        if (start < earliest) start = earliest;
        if (start >= DateTimeOffset.Now) return null;

        if (channel.CatchupSource is { Length: > 0 } template)
            return FillTemplate(template, channel, start, duration);

        if (playlist is { HasXtreamCredentials: true } &&
            playlist.EffectiveXtreamServer is { Length: > 0 } server &&
            playlist.EffectiveXtreamUsername is { Length: > 0 } user &&
            playlist.EffectiveXtreamPassword is { Length: > 0 } password &&
            channel.StreamId is { Length: > 0 } streamId)
            return BuildXtreamUrl(server, user, password, streamId, start, duration, alternate);

        return AppendShiftQuery(channel.Url, start);
    }

    // XUI/Xtream panelleri iki adres biçimi sunar; hangisinin açık olduğu
    // sunucu ayarına bağlı olduğu için ikisi de denenebilir olmalı.
    private static string BuildXtreamUrl(
        string server,
        string user,
        string password,
        string streamId,
        DateTimeOffset start,
        TimeSpan duration,
        bool alternate)
    {
        var baseUrl = server.TrimEnd('/');
        var minutes = Math.Max(1, (int)Math.Round(duration.TotalMinutes));
        var startText = start.LocalDateTime.ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);

        return alternate
            ? $"{baseUrl}/timeshift/{Uri.EscapeDataString(user)}/{Uri.EscapeDataString(password)}/{minutes}/{startText}/{streamId}.ts"
            : $"{baseUrl}/streaming/timeshift.php" +
              $"?username={Uri.EscapeDataString(user)}" +
              $"&password={Uri.EscapeDataString(password)}" +
              $"&stream={Uri.EscapeDataString(streamId)}" +
              $"&start={Uri.EscapeDataString(startText)}" +
              $"&duration={minutes}";
    }

    private static string AppendShiftQuery(string url, DateTimeOffset start)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}utc={start.ToUnixTimeSeconds()}&lutc={DateTimeOffset.Now.ToUnixTimeSeconds()}";
    }

    // catchup-source hem ${...} hem {...} yazımını kullanır; ikisi de karşılanır.
    private static string FillTemplate(string template, Channel channel, DateTimeOffset start, TimeSpan duration)
    {
        var url = template;
        if (!url.Contains("://"))
        {
            var separator = channel.Url.Contains('?') || url.StartsWith('&') ? "" : url.StartsWith('?') ? "" : "?";
            url = channel.Url + separator + url;
        }

        var end = start + duration;
        var now = DateTimeOffset.Now;
        var local = start.LocalDateTime;

        var values = new (string Key, string Value)[]
        {
            ("start", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("utc", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("timestamp", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("end", end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("utcend", end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("lutc", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("now", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("offset", ((long)(now - start).TotalSeconds).ToString(CultureInfo.InvariantCulture)),
            ("duration", ((long)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)),
            ("durmin", ((long)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture)),
            ("Y", local.ToString("yyyy", CultureInfo.InvariantCulture)),
            ("m", local.ToString("MM", CultureInfo.InvariantCulture)),
            ("d", local.ToString("dd", CultureInfo.InvariantCulture)),
            ("H", local.ToString("HH", CultureInfo.InvariantCulture)),
            ("M", local.ToString("mm", CultureInfo.InvariantCulture)),
            ("S", local.ToString("ss", CultureInfo.InvariantCulture)),
        };

        foreach (var (key, value) in values)
        {
            url = url.Replace("${" + key + "}", value, StringComparison.Ordinal);
            url = url.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        // Karşılığı olmayan yer tutucular adreste kalırsa yayın hiç açılmaz.
        return Regex.Replace(url, @"\$?\{[A-Za-z_][A-Za-z0-9_]*\}", "");
    }
}
