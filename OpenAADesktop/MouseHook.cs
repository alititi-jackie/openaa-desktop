using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenAADesktop;

/// <summary>
/// Low-level global mouse hook (WH_MOUSE_LL) used only to hit-test clicks
/// against the brand-text rectangle. Events are never swallowed: the hook
/// always passes input through, so desktop icons, the taskbar and every
/// application behave exactly as stock Windows.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_MOUSEMOVE = 0x0200;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private readonly LowLevelMouseProc _proc; // keep alive so the GC never collects the callback
    private IntPtr _hook = IntPtr.Zero;
    private long _lastMoveHandled;

    /// <summary>Raised on the hook thread for any left-button click (screen coords, physical px).</summary>
    public event Action<int, int>? LeftClicked;
    /// <summary>Raised (throttled) for mouse movement (screen coords, physical px).</summary>
    public event Action<int, int>? MouseMoved;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var cur = Process.GetCurrentProcess();
        using var module = cur.MainModule;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN)
                {
                    LeftClicked?.Invoke(data.pt.X, data.pt.Y);
                }
                else if (msg == WM_MOUSEMOVE)
                {
                    // Throttle hover handling to ~20 Hz to keep idle CPU at zero-ish.
                    long now = Environment.TickCount64;
                    if (now - _lastMoveHandled >= 50)
                    {
                        _lastMoveHandled = now;
                        MouseMoved?.Invoke(data.pt.X, data.pt.Y);
                    }
                }
            }
        }
        catch
        {
            // A hook must never throw; input continues regardless.
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
