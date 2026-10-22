using System.Threading;
using System.Windows;

namespace OpenAADesktop;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private Settings _settings = new();
    private MainWindow? _wallpaperWindow;
    private TrayManager? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch exits quietly instead of stacking
        // two wallpaper windows and two mouse hooks.
        _mutex = new Mutex(initiallyOwned: true, "OpenAADesktop.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // Never let a UI exception tear down the desktop: log-free, silent degrade.
        DispatcherUnhandledException += (_, args) => { args.Handled = true; };

        _settings = Settings.Load();

        // Keep the recorded autostart flag honest with the registry.
        _settings.AutoStart = AutoStart.IsEnabled();

        _tray = new TrayManager(_settings, () => _wallpaperWindow, ShowSettings);
        ShowWallpaperWindow();
    }

    private void ShowWallpaperWindow()
    {
        _wallpaperWindow = new MainWindow(_settings);
        _wallpaperWindow.WallpaperWindowDestroyed += OnWallpaperDestroyed;
        _wallpaperWindow.Show();
        _tray?.RefreshLabels();
    }

    private void OnWallpaperDestroyed()
    {
        // Explorer restart destroyed our child window: recreate it on the UI thread.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { _wallpaperWindow?.Close(); } catch { }
            ShowWallpaperWindow();
        }));
    }

    private void ShowSettings()
    {
        var win = new SettingsWindow(_settings, () => _wallpaperWindow);
        win.Show();
        win.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
