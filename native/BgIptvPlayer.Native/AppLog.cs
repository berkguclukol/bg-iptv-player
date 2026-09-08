using System;
using System.IO;

namespace BgIptvPlayer.Native;

// Sorun bildirimlerinde işe yarayan basit günlük. Dosya belirli boyutu geçince
// bir önceki kopya .1 uzantısıyla saklanır, böylece sınırsız büyümez.
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer", "logs");

    public static string FilePath { get; } = Path.Combine(DirectoryPath, "app.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                Roll();
                var line = exception is null
                    ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}"
                    : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}: {exception.GetType().Name} - {exception.Message}";
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Günlük yazılamaması uygulamayı hiçbir şekilde etkilememeli.
        }
    }

    private static void Roll()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes) return;

        var previous = FilePath + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(FilePath, previous);
    }
}
