using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BgIptvPlayer.Native;

// Hangi içeriğin listeye ne zaman girdiğini tutar; "Son Eklenenler" bunu kullanır.
// Yüz binlerce kayıt olabildiği için JSON yerine satır tabanlı düz metin kullanılır.
public static class Catalog
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer", "catalog.txt");

    private static Dictionary<string, long>? _firstSeen;
    private static long _initialImport;

    private static Dictionary<string, long> Items => _firstSeen ??= Load();

    private static Dictionary<string, long> Load()
    {
        var items = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(FilePath)) return items;

            using var reader = new StreamReader(FilePath);
            _initialImport = long.TryParse(reader.ReadLine(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stamp)
                ? stamp
                : 0;

            while (reader.ReadLine() is { } line)
            {
                var separator = line.IndexOf('\t');
                if (separator <= 0) continue;
                if (long.TryParse(line.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seen))
                    items[line[..separator]] = seen;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Katalog okunamadı", ex);
        }

        return items;
    }

    // Listeye yeni giren içerikler işaretlenir. İlk kurulumda her şey yeni
    // görüneceği için o an eklenenler "yeni" sayılmaz.
    public static void Track(IEnumerable<Channel> channels)
    {
        var items = Items;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isFirstImport = items.Count == 0;
        if (isFirstImport) _initialImport = now;

        var added = false;
        foreach (var channel in channels)
        {
            if (items.ContainsKey(channel.Id)) continue;
            items[channel.Id] = now;
            added = true;
        }

        if (added) Save();
    }

    public static DateTimeOffset? FirstSeen(string id) =>
        Items.TryGetValue(id, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    // Sonradan eklenen ve son 30 gün içinde görülen içerikler.
    public static bool IsRecentlyAdded(string id)
    {
        if (!Items.TryGetValue(id, out var seconds)) return false;
        if (seconds <= _initialImport) return false;
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds <= 30L * 24 * 60 * 60;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var builder = new StringBuilder();
            builder.AppendLine(_initialImport.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in Items)
                builder.Append(pair.Key).Append('\t').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
            File.WriteAllText(FilePath, builder.ToString());
        }
        catch (Exception ex)
        {
            AppLog.Write("Katalog kaydedilemedi", ex);
        }
    }
}
