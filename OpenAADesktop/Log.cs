using System.IO;

namespace OpenAADesktop;

/// <summary>
/// Minimal append-only diagnostic log at %AppData%\OpenAADesktop\debug.log.
/// Silent on failure by design: logging must never break the wallpaper.
/// </summary>
public static class Log
{
    private static readonly string Path_ = System.IO.Path.Combine(Settings.SettingsDirectory, "debug.log");
    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Settings.SettingsDirectory);
                File.AppendAllText(Path_,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { /* never let logging break the app */ }
    }
}
