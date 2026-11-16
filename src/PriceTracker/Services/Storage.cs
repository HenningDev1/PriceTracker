using System.Text.Json;
using PriceTracker.Models;

namespace PriceTracker.Services;

/// Reads and writes data.json / settings.json in %APPDATA%\PriceTracker
/// (or PRICETRACKER_DATA_DIR, used by the screenshot script). Writes are atomic.
public static class Storage
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("PRICETRACKER_DATA_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PriceTracker");

    public static string CacheDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceTracker");

    private static string DataFile => Path.Combine(DataDir, "data.json");
    private static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static AppData LoadData() => Load<AppData>(DataFile) ?? new AppData();
    public static AppSettings LoadSettings() => Load<AppSettings>(SettingsFile) ?? new AppSettings();

    public static void SaveData(AppData data) => Save(DataFile, data);
    public static void SaveSettings(AppSettings settings) => Save(SettingsFile, settings);

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch
        {
            // Keep the broken file for inspection instead of silently overwriting it.
            try { File.Copy(path, path + ".broken", overwrite: true); } catch { }
            return null;
        }
    }

    private static void Save<T>(string path, T value)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DataDir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
