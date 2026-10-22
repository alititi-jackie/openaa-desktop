using System.Runtime.InteropServices;

namespace OpenAADesktop;

/// <summary>
/// Attaches a window to the desktop wallpaper layer using the Explorer
/// WorkerW mechanism (the same approach used by established live-wallpaper
/// apps): the window is parented to the WorkerW that sits *behind* the
/// desktop icons (SHELLDLL_DefView), so icons stay visible and clickable
/// above it, and the taskbar is unaffected.
///
/// Clicks are NOT taken from this window (it is input-transparent); a
/// low-level mouse hook (see MouseHook) hit-tests the brand text instead,
/// so every other desktop click behaves exactly as stock Windows.
/// </summary>
public static class DesktopLayer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    /// <summary>Finds (spawning if needed) the WorkerW that hosts the wallpaper layer.</summary>
    public static IntPtr FindWallpaperWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // Ask Explorer to spawn a WorkerW behind the desktop icons (undocumented but
        // long-stable message 0x052C, used by all major live-wallpaper apps).
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        IntPtr workerW = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            IntPtr defView = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                // The WorkerW *after* the one containing DefView is the wallpaper host.
                workerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        // Fallback: some shells expose the wallpaper WorkerW directly.
        if (workerW == IntPtr.Zero)
            workerW = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);

        return workerW;
    }

    /// <summary>
    /// Parents <paramref name="hwnd"/> to the wallpaper WorkerW and sizes it to the
    /// primary monitor. Returns false if the layer could not be found.
    /// </summary>
    public static bool Attach(IntPtr hwnd)
    {
        IntPtr workerW = FindWallpaperWorkerW();
        if (workerW == IntPtr.Zero) return false;

        int style = GetWindowLong(hwnd, GWL_STYLE);
        style = (style & ~WS_POPUP) | WS_CHILD;
        SetWindowLong(hwnd, GWL_STYLE, style);

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);

        SetParent(hwnd, workerW);

        var rect = PrimaryMonitorRect();
        // Coordinates are relative to the WorkerW, which spans the virtual screen
        // starting at (0,0) of the primary monitor in the standard layout.
        SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return true;
    }

    /// <summary>Physical-pixel bounds of the primary monitor.</summary>
    public static RECT PrimaryMonitorRect()
    {
        IntPtr mon = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref info))
            return info.rcMonitor;
        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }
}
