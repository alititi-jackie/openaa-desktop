using System.IO;
using System.Text.Json;

namespace OpenAADesktop;

/// <summary>
/// Local user settings, stored as JSON under %AppData%\OpenAADesktop.
/// Any read/write failure degrades to defaults; the app must never crash
/// because of a missing or corrupt config file.
/// </summary>
public sealed class Settings
{
    public int FontIndex { get; set; }
    public bool AutoStart { get; set; }
    /// <summary>Optional user-chosen wallpaper (absolute path). Null = built-in default.</summary>
    public string? CustomWallpaperPath { get; set; }

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenAADesktop");

    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var s = JsonSerializer.Deserialize<Settings>(json);
                if (s != null)
                {
                    if (s.FontIndex < 0) s.FontIndex = 0;
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt config: fall through to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Non-fatal: settings just won't persist this time.
        }
    }
}
