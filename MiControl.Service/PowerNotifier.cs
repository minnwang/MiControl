using System.Runtime.InteropServices;

namespace MiControl.Service;

/// <summary>
/// Minimal Win32 power notification receiver. Replaces WinForms/SystemEvents so the
/// service does not need the whole desktop stack just to hear about AC changes.
/// Creates a message-only window on its own thread and registers for
/// GUID_ACDC_POWER_SOURCE plus resume broadcasts.
/// </summary>
internal sealed class PowerNotifier : IDisposable
{
    private const int WM_POWERBROADCAST = 0x0218;
    private const int WM_QUIT = 0x0012;
    private const int WM_DESTROY = 0x0002;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    private static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Action _onChange;
    private readonly WndProcDelegate _wndProc;   // keep alive: native code holds the pointer
    private IntPtr _hwnd;
    private uint _threadId;
    private bool _disposed;

    public PowerNotifier(Action onChange)
    {
        _onChange = onChange;
        _wndProc = WndProc;
    }

    public void Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "PowerNotifications"
        };
        thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var hInstance = GetModuleHandle(null);
        const string className = "MiControlPowerWindow";

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className
        };

        if (RegisterClassEx(ref wc) == 0)
            return;

        _hwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            return;

        var guid = AcDcPowerSource;
        RegisterPowerSettingNotification(_hwnd, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_POWERBROADCAST)
        {
            int w = wParam.ToInt32();
            if (w == PBT_APMRESUMEAUTOMATIC || w == PBT_POWERSETTINGCHANGE)
            {
                try { _onChange(); } catch { }
            }
        }
        else if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    // ---------------- interop ----------------

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

