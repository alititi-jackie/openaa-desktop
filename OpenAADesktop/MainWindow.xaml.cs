using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace OpenAADesktop;

public partial class MainWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    private readonly Settings _settings;
    private readonly List<FontFamily> _fonts;
    private readonly MouseHook _hook;
    private readonly uint _taskbarCreatedMsg;
    private IntPtr _hwnd;
    private Rect _brandRectPx;      // brand text hit-rect in physical screen pixels
    private bool _hovering;
    private bool _destroyed;

    public event Action? WallpaperWindowDestroyed;

    public MainWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _fonts = ResolveFonts();
        _hook = new MouseHook();
        _taskbarCreatedMsg = DesktopLayer.RegisterWindowMessage("TaskbarCreated");

        Loaded += OnLoaded;
        LayoutUpdated += (_, _) => UpdateBrandRect();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    // ---------- fonts ----------

    private static readonly string[] CandidateFonts =
    {
        "Segoe UI Light", "Arial", "Calibri Light", "Century Gothic", "Georgia",
        "Trebuchet MS", "Verdana", "Bahnschrift Light", "Candara Light", "Segoe UI"
    };

    /// <summary>Filter the candidate list to fonts actually installed; always keep a fallback.</summary>
    private static List<FontFamily> ResolveFonts()
    {
        var installed = new HashSet<string>(
            Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
        var list = new List<FontFamily>();
        foreach (var name in CandidateFonts)
            if (installed.Contains(name))
                list.Add(new FontFamily(name));
        if (list.Count == 0)
            list.Add(new FontFamily("Segoe UI"));
        return list;
    }

    public string CurrentFontName =>
        _fonts[Math.Clamp(_settings.FontIndex, 0, _fonts.Count - 1)].Source;

    private void ApplyFont()
    {
        if (_settings.FontIndex >= _fonts.Count) _settings.FontIndex = 0;
        BrandText.FontFamily = _fonts[_settings.FontIndex];
        UpdateBrandRect();
    }

    public void CycleFont()
    {
        _settings.FontIndex = (_settings.FontIndex + 1) % _fonts.Count;
        ApplyFont();
        _settings.Save();
    }

    public void ResetFont()
    {
        _settings.FontIndex = 0;
        ApplyFont();
        _settings.Save();
    }

    // ---------- wallpaper ----------

    public void LoadWallpaper()
    {
        try
        {
            var custom = _settings.CustomWallpaperPath;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(custom, UriKind.Absolute);
                bmp.EndInit();
                BgImage.Source = bmp;
                return;
            }
        }
        catch { /* fall through to built-in */ }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri("pack://application:,,,/Assets/wallpaper.jpg", UriKind.Absolute);
            bmp.EndInit();
            BgImage.Source = bmp;
        }
        catch { /* leave black background rather than crash */ }
    }

    public void ReloadDefaultWallpaper()
    {
        _settings.CustomWallpaperPath = null;
        _settings.Save();
        LoadWallpaper();
    }

    // ---------- window / desktop layer ----------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        LoadWallpaper();
        ApplyFont();

        _hook.LeftClicked += OnGlobalClick;
        _hook.MouseMoved += OnGlobalMove;
        _hook.Start();

        StartWatchdog();

        // The WorkerW wallpaper layer can take a moment to spawn after the
        // 0x052C request; retry briefly. Until attached, the window stays
        // hidden so it can NEVER float above the desktop icons.
        _ = AttachWithRetryAsync();
    }

    private async Task AttachWithRetryAsync()
    {
        for (int i = 0; i < 10 && !_destroyed; i++)
        {
            if (AttachAndLayout())
            {
                Log.Write($"AttachWithRetry: attached on attempt {i + 1}");
                return;
            }
            await Task.Delay(700);
        }
        Log.Write("AttachWithRetry: all attempts failed, staying hidden (watchdog keeps retrying)");
    }

    /// <summary>Returns true when the window is parented into the wallpaper layer.</summary>
    private bool AttachAndLayout()
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (!DesktopLayer.AttachToFirstCandidate(_hwnd))
        {
            Visibility = Visibility.Hidden;
            return false;
        }
        Visibility = Visibility.Visible;

        var mon = DesktopLayer.PrimaryMonitorRect();
        var dpi = VisualTreeHelper.GetDpi(this);
        double wDip = (mon.Right - mon.Left) / dpi.DpiScaleX;
        double hDip = (mon.Bottom - mon.Top) / dpi.DpiScaleY;
        Width = wDip;
        Height = hDip;

        // Brand text placement mirrors the reference artwork: top-right,
        // ~13% down from the top edge, ~4% in from the right edge.
        BrandText.FontSize = Math.Max(14, hDip * 0.048);
        BrandText.Margin = new Thickness(0, hDip * 0.13, wDip * 0.04, 0);
        UpdateBrandRect();
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMsg)
        {
            // Explorer restarted: the WorkerW we were parented to is gone.
            // Re-attach shortly after the new shell settles.
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(800);
                if (!_destroyed) AttachAndLayout();
            }));
        }
        else if (msg == 0x0010) // WM_CLOSE: the shell may close child windows on restart
        {
            handled = true; // never let an external close kill the app silently
        }
        else if (msg == 0x0002) // WM_DESTROY
        {
            if (!_destroyed)
            {
                _destroyed = true;
                WallpaperWindowDestroyed?.Invoke();
            }
        }
        return IntPtr.Zero;
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_destroyed) AttachAndLayout();
        });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_destroyed) AttachAndLayout();
            });
        }
    }

    private System.Windows.Threading.DispatcherTimer? _watchdog;

    private void StartWatchdog()
    {
        _watchdog = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _watchdog.Tick += (_, _) =>
        {
            if (_destroyed || _hwnd == IntPtr.Zero) return;
            // If we lost our parent (Explorer rebuilt the desktop but this
            // window survived), re-attach. A live WorkerW parent means we are
            // in a wallpaper layer; which candidate it is does not matter.
            IntPtr parent = GetParent(_hwnd);
            if (parent == IntPtr.Zero)
            {
                Log.Write("Watchdog: no parent, re-attaching");
                AttachAndLayout();
            }
        };
        _watchdog.Start();
    }

    // ---------- mouse interaction (via global hook; window itself is input-transparent) ----------

    private void UpdateBrandRect()
    {
        try
        {
            if (!IsLoaded || BrandText.ActualWidth <= 0) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            var mon = DesktopLayer.PrimaryMonitorRect();
            var pos = BrandText.TranslatePoint(new Point(0, 0), this);
            double x = mon.Left + pos.X * dpi.DpiScaleX;
            double y = mon.Top + pos.Y * dpi.DpiScaleY;
            _brandRectPx = new Rect(x - 8, y - 8,
                BrandText.ActualWidth * dpi.DpiScaleX + 16,
                BrandText.ActualHeight * dpi.DpiScaleY + 16);
        }
        catch { /* layout not ready yet */ }
    }

    private void OnGlobalClick(int x, int y)
    {
        if (_brandRectPx.Contains(x, y))
            Dispatcher.Invoke(CycleFont);
    }

    private void OnGlobalMove(int x, int y)
    {
        bool inside = _brandRectPx.Contains(x, y);
        if (inside != _hovering)
        {
            _hovering = inside;
            Dispatcher.Invoke(() =>
            {
                // Subtle feedback only: full opacity + slightly deeper shadow.
                BrandText.Opacity = inside ? 1.0 : 0.94;
                if (BrandText.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
                    shadow.Opacity = inside ? 0.5 : 0.35;
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _watchdog?.Stop();
        _hook.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        base.OnClosed(e);
    }
}
