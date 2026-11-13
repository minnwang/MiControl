using System.Runtime.InteropServices;

namespace Win32TrayTest;

/// <summary>
/// Small window that records the next key combination and returns a spec such as
/// "0x5B+0x50" (mutually exclusive with the service's keys: action format).
/// </summary>
internal static class KeyCaptureWindow
{
    private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int SS_LEFT = 0x0000;
    private const int WM_CREATE = 0x0001;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_CLOSE = 0x0010;
    private const int WM_DESTROY = 0x0002;
    private const int WM_SETFONT = 0x0030;
    private const int SW_SHOW = 5;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;      // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_ESCAPE = 0x1B;

    private static IntPtr _hwnd;
    private static Action<string?>? _callback;
    private static bool _captured;
    private static WndProcDelegate _proc = null!;

    public static void Show(Action<string?> callback)
    {
        _callback = callback;
        _captured = false;

        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
            return;
        }

        _proc = WndProc;
        var hInstance = GetModuleHandle(null);
        const string cls = "MiControlKeyCapture";

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0003,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            hbrBackground = (IntPtr)16,
            lpszClassName = cls
        };
        RegisterClassEx(ref wc);

        _hwnd = CreateWindowEx(0, cls, "按下组合键", WS_OVERLAPPEDWINDOW,
            420, 260, 420, 180, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        ShowWindow(_hwnd, SW_SHOW);
        SetForegroundWindow(_hwnd);
        UpdateWindow(_hwnd);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_CREATE:
            {
                var label = CreateWindowEx(0, "STATIC",
                    "请按下要模拟的组合键（例如 Ctrl+Shift+A），Esc 取消",
                    WS_CHILD | WS_VISIBLE | SS_LEFT, 16, 24, 380, 40,
                    hWnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                SendMessage(label, WM_SETFONT, GetStockObject(17), (IntPtr)1);
                return IntPtr.Zero;
            }

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
            {
                int vk = wParam.ToInt32();

                if (vk == VK_ESCAPE)
                {
                    DestroyWindow(_hwnd);
                    return IntPtr.Zero;
                }

                if (vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN)
                    return IntPtr.Zero;   // wait for the real key

                var parts = new List<string>();
                if (IsDown(VK_CONTROL)) parts.Add("0x11");
                if (IsDown(VK_SHIFT)) parts.Add("0x10");
                if (IsDown(VK_MENU)) parts.Add("0x12");
                if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) parts.Add("0x5B");
                parts.Add("0x" + vk.ToString("X2"));

                var spec = string.Join("+", parts);
                Program.LogPublic("captured " + spec);

                _captured = true;
                _callback?.Invoke(spec);
                DestroyWindow(_hwnd);
                return IntPtr.Zero;
            }

            case WM_CLOSE:
                DestroyWindow(_hwnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                _hwnd = IntPtr.Zero;
                if (!_captured) _callback?.Invoke(null);
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

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
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vk);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);
}

