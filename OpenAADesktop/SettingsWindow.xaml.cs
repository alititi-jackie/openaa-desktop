using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OpenAADesktop;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Func<MainWindow?> _getWindow;
    private bool _initializing = true;

    public SettingsWindow(Settings settings, Func<MainWindow?> getWindow)
    {
        InitializeComponent();
        _settings = settings;
        _getWindow = getWindow;

        var v = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = $"版本 v{v?.ToString(3) ?? "1.0.0"} · Windows 11 桌面壁纸";
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        RefreshFontLabel();
        _initializing = false;
    }

    private void RefreshFontLabel()
    {
        var w = _getWindow();
        FontText.Text = w != null ? $"当前字体：{w.CurrentFontName}（点击桌面文字也可切换）" : "当前字体：-";
    }

    private void OnCycleFont(object sender, RoutedEventArgs e)
    {
        _getWindow()?.CycleFont();
        RefreshFontLabel();
    }

    private void OnResetFont(object sender, RoutedEventArgs e)
    {
        _getWindow()?.ResetFont();
        RefreshFontLabel();
    }

    private void OnChooseWallpaper(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = "选择壁纸图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _settings.CustomWallpaperPath = dlg.FileName;
            _settings.Save();
            _getWindow()?.LoadWallpaper();
        }
    }

    private void OnReloadWallpaper(object sender, RoutedEventArgs e) =>
        _getWindow()?.ReloadDefaultWallpaper();

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        bool target = AutoStartCheck.IsChecked == true;
        AutoStart.Set(target);
        _settings.AutoStart = AutoStart.IsEnabled();
        _settings.Save();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Settings.SettingsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", Settings.SettingsDirectory) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
