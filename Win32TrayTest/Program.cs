using System.Runtime.InteropServices;

namespace Win32TrayTest;

/// <summary>
/// Minimal Win32 tray prototype: notification icon + popup menu with a two-line
/// greyed header, one submenu and an exit item. No WinForms anywhere.
/// </summary>
internal static class Program
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAYICON = WM_APP + 1;
    private const int WM_COMMAND = 0x0111;
    private const int WM_DESTROY = 0x0002;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_TASKBARCREATED = WM_APP + 2;

    private const int NIM_ADD = 0;
    private const int NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;

    private const int MF_STRING = 0x0000;
    private const int MF_GRAYED = 0x0001;
    private const int MF_CHECKED = 0x0008;
    private const int MF_SEPARATOR = 0x0800;
    private const int MF_POPUP = 0x0010;

    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;

    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x0010;

    private static IntPtr _hwnd;
    private static IntPtr _icon;

    private static WndProcDelegate _wndProc = null!;
    private static readonly string IconPath =
        Path.Combine(AppContext.BaseDirectory, "app.ico");

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "win32tray.log");
    internal static void LogPublic(string m) => Log(m);

    private static void Log(string m)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {m}{Environment.NewLine}"); } catch { }
    }

    private static Mutex? _instanceLock;

    [STAThread]
    private static void Main()
    {
        _instanceLock = new Mutex(true, @"Global\MiControlWin32TrayTest", out bool created);
        if (!created) return;

        EnsureAutostart();

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("FATAL " + e.ExceptionObject);
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        try { SetProcessDPIAware(); } catch { }
        Log("start");

        _wndProc = WndProc;
        var hInstance = GetModuleHandle(null);
        const string className = "MiControlWin32TrayTest";

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className
        };
        Log("cbSize=" + wc.cbSize);
        var atom = RegisterClassEx(ref wc);
        Log("RegisterClassEx=" + atom + " err=" + Marshal.GetLastWin32Error());

        _hwnd = CreateWindowEx(0, className, "tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        Log("CreateWindowEx hwnd=" + _hwnd + " err=" + Marshal.GetLastWin32Error());

        int iconSize = GetSystemMetrics(49);   // SM_CXSMICON: DPI-correct small icon
        _icon = LoadImage(IntPtr.Zero, IconPath, IMAGE_ICON, iconSize, iconSize, LR_LOADFROMFILE);
        Log("LoadImage icon=" + _icon + " path=" + IconPath + " exists=" + File.Exists(IconPath));
        AddIcon();
        Log("icon added");
        Backend.StartPushListener(HandleAction);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        RemoveIcon();
    }

    private static void AddIcon()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _icon,
            szTip = "MiControl (Win32 prototype)"
        };
        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private static void RemoveIcon()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);
    }

    // Register the prototype to start with Windows (via the dotnet host, because the
    // unsigned apphost is blocked by the AV). Only written once.
    private static void EnsureAutostart()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            var dll = typeof(Program).Assembly.Location;
            var exe = Environment.ProcessPath ?? "";
            var value = exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? "\"" + exe + "\" \"" + dll + "\""
                : "\"" + exe + "\"";
            var current = key?.GetValue("MiControlWin32Test") as string;
            if (current != value) key?.SetValue("MiControlWin32Test", value);
            Log("autostart set");
        }
        catch (Exception ex) { Log("autostart failed " + ex.Message); }
    }

    private static void HandleAction(string action)
    {
        Log("action " + action);
        if (action == "act:settings" || action == "settings")
        {
            SettingsWindow.Show();
        }
        else if (action.StartsWith("act:ms:", StringComparison.Ordinal))
        {
            var page = action.Substring(7);
            var uri = page.Length == 0 ? "ms-settings:" : "ms-settings:" + page;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex) { Log("ms failed " + ex.Message); }
        }
        else if (action.StartsWith("act:app:", StringComparison.Ordinal))
        {
            var path = action.Substring(8);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log("app failed " + ex.Message); }
        }
        else if (action.StartsWith("act:keys:", StringComparison.Ordinal))
        {
            var spec = action.Substring(9);
            var vks = spec.Split('+', StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                          .Select(s => (byte)Convert.ToInt32(s.Substring(2), 16))
                          .ToList();
            for (int i = 1; i < vks.Count; i++) keybd_event(vks[i], 0, 0, UIntPtr.Zero);
            if (vks.Count > 0)
            {
                keybd_event(vks[0], 0, 0, UIntPtr.Zero);
                keybd_event(vks[0], 0, 2, UIntPtr.Zero);
            }
            for (int i = vks.Count - 1; i >= 1; i--) keybd_event(vks[i], 0, 2, UIntPtr.Zero);
        }
        else if (action == "mic:on" || action == "mic:off")
        {
            bool want = action == "mic:on";
            var r = MicControl.SetMute(want);
            Log("mic " + want + " -> " + (r?.ToString() ?? "failed"));
        }
        else if (action == "act:winp" || action == "winp")
        {
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x50, 0, 0, UIntPtr.Zero);
            keybd_event(0x50, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }
    }

    private static void ShowMenu()
    {
        var st = Backend.GetStatus();
        Log("status ac=" + st.Ac + " bat=" + st.Battery + " perf=" + st.Perf + " pct=" + st.ChargePct);

        var menu = CreatePopupMenu();
        var perf = CreatePopupMenu();

        AppendMenu(perf, MF_STRING | (st.Perf == 0x02 ? MF_CHECKED : 0), 101, "静音");
        AppendMenu(perf, MF_STRING | (st.Perf == 0x09 ? MF_CHECKED : 0), 102, "智能");
        AppendMenu(perf, MF_STRING | (st.Perf == 0x03 ? MF_CHECKED : 0), 103, "性能");
        AppendMenu(perf, MF_STRING | (st.Perf == 0x04 ? MF_CHECKED : 0), 104, "全速");

        string powerState = st.Ac
            ? (st.Watts > 0 ? $"{st.Watts} W" : "已接电源") + (st.Charging ? " 充电中" : " 未充电")
            : "电池供电";
        AppendMenu(menu, MF_STRING | MF_GRAYED, 0, $"电量：{st.Battery}%  ·  {powerState}");
        AppendMenu(menu, MF_STRING | MF_GRAYED, 0, $"运行：{st.PerfName}  ·  充电上限：{st.ChargePct}%");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_POPUP, (nuint)perf, "运行模式");

        var charge = CreatePopupMenu();
        int[] levels = { 100, 90, 80, 70, 60, 50, 40 };
        for (int i = 0; i < levels.Length; i++)
        {
            int flags = MF_STRING | (levels[i] == st.ChargePct ? MF_CHECKED : 0);
            AppendMenu(charge, flags, (nuint)(201 + i), levels[i] + "%");
        }
        AppendMenu(menu, MF_POPUP, (nuint)charge, "充电上限");
        AppendMenu(menu, MF_STRING, 110, "设置…");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, 200, "退出");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);

        int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == 110)
        {
            SettingsWindow.Show();
        }
        else if (cmd >= 101 && cmd <= 104)
        {
            int mode = cmd switch { 101 => 0x02, 102 => 0x09, 103 => 0x03, _ => 0x04 };
            Log("setPerf " + mode + " -> " + Backend.SetPerf(mode));
        }
        else if (cmd >= 201 && cmd <= 207)
        {
            int[] lv = { 100, 90, 80, 70, 60, 50, 40 };
            int pct = lv[cmd - 201];
            Log("setCharge " + pct + " -> " + Backend.SetCharge(pct));
        }
        else if (cmd == 200)
        {
            RemoveIcon();
            PostQuitMessage(0);
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAYICON:
                if ((int)lParam == WM_RBUTTONUP || (int)lParam == WM_LBUTTONUP)
                    ShowMenu();
                break;

            case WM_TASKBARCREATED:
                AddIcon();
        Log("icon added");
        Backend.StartPushListener(HandleAction);
                break;

            case WM_COMMAND:
                {
                    int id = wParam.ToInt32() & 0xFFFF;
                    if (id == 200)
                    {
                        RemoveIcon();
                        PostQuitMessage(0);
                    }
                    break;
                }

            case WM_DESTROY:
                PostQuitMessage(0);
                break;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
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
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string name, int type, int cx, int cy, int load);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, nuint id, string? item);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
}












