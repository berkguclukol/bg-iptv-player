using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BgIptvPlayer.Native;

// Kullanıcının kendi oluşturduğu koleksiyonlar: ad → içerik kimlikleri.
public static class Collections
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer", "collections.json");

    private static Dictionary<string, List<string>>? _items;

    private static Dictionary<string, List<string>> Items => _items ??= Load();

    public static IReadOnlyList<string> Names =>
        Items.Keys.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static IReadOnlyList<string> Ids(string name) =>
        Items.TryGetValue(name, out var ids) ? ids : [];

    public static bool Contains(string name, string id) =>
        Items.TryGetValue(name, out var ids) && ids.Contains(id, StringComparer.Ordinal);

    public static string CreateUnique(string baseName)
    {
        var name = baseName;
        var index = 2;
        while (Items.ContainsKey(name)) name = $"{baseName} {index++}";
        Items[name] = [];
        Save();
        return name;
    }

    public static void Toggle(string name, string id)
    {
        if (!Items.TryGetValue(name, out var ids)) return;
        if (!ids.Remove(id)) ids.Add(id);
        Save();
    }

    public static void Delete(string name)
    {
        if (Items.Remove(name)) Save();
    }

    public static bool Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || Items.ContainsKey(newName) || !Items.Remove(oldName, out var ids)) return false;

        Items[newName] = ids;
        Save();
        return true;
    }

    private static Dictionary<string, List<string>> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath))
                       ?? new Dictionary<string, List<string>>();
        }
        catch (Exception ex)
        {
            AppLog.Write("Koleksiyonlar okunamadı", ex);
        }

        return new Dictionary<string, List<string>>();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Write("Koleksiyonlar kaydedilemedi", ex);
        }
    }
}
