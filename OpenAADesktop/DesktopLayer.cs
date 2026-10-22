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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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

    /// <summary>
    /// Enumerates the desktop's top-level shell windows, asks Explorer to spawn
    /// the wallpaper WorkerW (0x052C), then returns every plausible wallpaper
    /// host in preference order: WorkerW siblings without SHELLDLL_DefView
    /// (the icons live in DefView, so the wallpaper host must not contain it),
    /// preferring the one right after the DefView host in z-order.
    /// Every step is logged to %AppData%\OpenAADesktop\debug.log: the exact
    /// shell layout differs between Windows builds, and the log is the only
    /// reliable way to see a given machine's tree.
    /// </summary>
    public static List<IntPtr> FindWallpaperCandidates()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Log.Write("FindWallpaperCandidates: Progman not found");
            return new List<IntPtr>();
        }

        SendMessageTimeout(progman, 0x052C, (IntPtr)0xD, IntPtr.Zero, 0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, (IntPtr)0xD, (IntPtr)1, 0, 1000, out _);

        var topLevel = new List<(IntPtr Hwnd, string Class, bool HasDefView)>();
        EnumWindows((top, _) =>
        {
            var buf = new System.Text.StringBuilder(64);
            GetClassName(top, buf, buf.Capacity);
            string cls = buf.ToString();
            bool hasDef = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
            topLevel.Add((top, cls, hasDef));
            return true;
        }, IntPtr.Zero);

        foreach (var (h, cls, hasDef) in topLevel)
            if (cls is "WorkerW" or "Progman" || hasDef)
                Log.Write($"  top-level 0x{h.ToInt64():X} {cls} defView={hasDef}");

        int defIdx = topLevel.FindIndex(t => t.HasDefView);
        var candidates = new List<IntPtr>();
        // Preferred: first WorkerW after the DefView host.
        if (defIdx >= 0)
            for (int i = defIdx + 1; i < topLevel.Count; i++)
                if (topLevel[i].Class == "WorkerW") { candidates.Add(topLevel[i].Hwnd); break; }
        // Then every other WorkerW that does not host the icons, in z-order.
        foreach (var (h, cls, hasDef) in topLevel)
            if (cls == "WorkerW" && !hasDef && !candidates.Contains(h))
                candidates.Add(h);

        Log.Write($"FindWallpaperCandidates: defViewHostIndex={defIdx}, candidates=[{string.Join(", ", candidates.ConvertAll(c => "0x" + c.ToInt64().ToString("X")))}]");
        return candidates;
    }

    /// <summary>Convenience: the most preferred wallpaper host, or Zero.</summary>
    public static IntPtr FindWallpaperWorkerW()
    {
        var list = FindWallpaperCandidates();
        return list.Count > 0 ? list[0] : IntPtr.Zero;
    }

    /// <summary>
    /// Parents <paramref name="hwnd"/> to the given wallpaper host and sizes it
    /// to the primary monitor. Returns false if parenting did not stick.
    /// </summary>
    public static bool Attach(IntPtr hwnd, IntPtr workerW)
    {
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
        // HWND_BOTTOM (1): stay at the bottom of the host even inside the layer.
        SetWindowPos(hwnd, (IntPtr)1, rect.Left, rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        bool ok = GetParent(hwnd) == workerW;
        Log.Write($"Attach: parent=0x{workerW.ToInt64():X} ok={ok}");
        return ok;
    }

    /// <summary>
    /// Tries every wallpaper-host candidate in preference order. Even when a
    /// candidate accepts the parenting, only the FIRST is kept: which one is
    /// actually visible depends on the shell, so the attempt order in
    /// FindWallpaperCandidates encodes the known-good layouts first.
    /// </summary>
    public static bool AttachToFirstCandidate(IntPtr hwnd)
    {
        foreach (var candidate in FindWallpaperCandidates())
            if (Attach(hwnd, candidate))
                return true;
        Log.Write("AttachToFirstCandidate: no candidate accepted");
        return false;
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
