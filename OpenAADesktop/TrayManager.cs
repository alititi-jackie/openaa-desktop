using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace OpenAADesktop;

/// <summary>HKCU Run-key autostart. Only ever written when the user opts in.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenAADesktop";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* registry unavailable: non-fatal */ }
    }
}

public sealed class TrayManager : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Func<MainWindow?> _getWindow;
    private readonly Settings _settings;
    private readonly Action _showSettings;
    private readonly WinForms.ToolStripMenuItem _fontItem;
    private readonly WinForms.ToolStripMenuItem _autoStartItem;

    public TrayManager(Settings settings, Func<MainWindow?> getWindow, Action showSettings)
    {
        _settings = settings;
        _getWindow = getWindow;
        _showSettings = showSettings;

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "OpenAA Desktop",
            Visible = true
        };
        _icon.DoubleClick += (_, _) => _showSettings();

        var menu = new WinForms.ContextMenuStrip();

        _fontItem = new WinForms.ToolStripMenuItem { Enabled = false };
        menu.Items.Add(_fontItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("切换字体", null, (_, _) => _getWindow()?.CycleFont());
        menu.Items.Add("恢复默认字体", null, (_, _) => { _getWindow()?.ResetFont(); RefreshLabels(); });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("选择自定义壁纸…", null, (_, _) => ChooseWallpaper());
        menu.Items.Add("重新加载默认壁纸", null, (_, _) => _getWindow()?.ReloadDefaultWallpaper());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _autoStartItem = new WinForms.ToolStripMenuItem("开机启动") { Checked = AutoStart.IsEnabled() };
        _autoStartItem.Click += (_, _) =>
        {
            bool target = !_autoStartItem.Checked;
            AutoStart.Set(target);
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _settings.AutoStart = _autoStartItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(_autoStartItem);

        menu.Items.Add("打开设置文件夹", null, (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Settings.SettingsDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", Settings.SettingsDirectory) { UseShellExecute = true });
            }
            catch { }
        });
        menu.Items.Add("设置…", null, (_, _) => _showSettings());
        menu.Items.Add($"版本 v{typeof(TrayManager).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}",
            null, (_, _) => _showSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出 OpenAA Desktop", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon.ContextMenuStrip = menu;
        RefreshLabels();
    }

    public void RefreshLabels()
    {
        var w = _getWindow();
        _fontItem.Text = w != null ? $"当前字体：{w.CurrentFontName}" : "当前字体：-";
    }

    private void ChooseWallpaper()
    {
        using var dlg = new WinForms.OpenFileDialog
        {
            Title = "选择壁纸图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*"
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            _settings.CustomWallpaperPath = dlg.FileName;
            _settings.Save();
            _getWindow()?.LoadWallpaper();
        }
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(path)) return new Drawing.Icon(path);
        }
        catch { }
        try
        {
            // Single-file build: the icon is embedded in the exe itself.
            var exeIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            if (exeIcon != null) return exeIcon;
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
