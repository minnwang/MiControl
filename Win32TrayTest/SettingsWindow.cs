using System.Runtime.InteropServices;

namespace Win32TrayTest;

/// <summary>
/// Win32 settings window laid out like the WinForms version.
/// Hotkey combo selection is written back to the service; picking a program opens the
/// standard file dialog.
/// </summary>
internal static class SettingsWindow
{
    private const int WS_OVERLAPPEDWINDOW = 0x00C80000;   // caption | sysmenu | minimize, no thick frame
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_TABSTOP = 0x00010000;
    private const int WS_VSCROLL = 0x00200000;
    private const int BS_AUTOCHECKBOX = 0x0003;
    private const int BS_PUSHBUTTON = 0x0000;
    private const int SS_LEFT = 0x0000;
    private const int CBS_DROPDOWNLIST = 0x0003;
    private const int CBS_HASSTRINGS = 0x0200;
    private const int WM_CREATE = 0x0001;
    private const int WM_COMMAND = 0x0111;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_CLOSE = 0x0010;
    private const int WM_DESTROY = 0x0002;
    private const int WM_SETFONT = 0x0030;
    private const int WM_SETTEXT = 0x000C;
    private const int CB_ADDSTRING = 0x0143;
    private const int CB_SETCURSEL = 0x014E;
    private const int CB_GETCURSEL = 0x0147;
    private const int CB_RESETCONTENT = 0x014B;
    private const int CBN_SELCHANGE = 1;
    private const int BM_SETCHECK = 0x00F1;
    private const int BM_GETCHECK = 0x00F0;
    private const int BST_CHECKED = 1;
    private const int SW_SHOW = 5;
    private const int SPI_GETNONCLIENTMETRICS = 41;

    private const int IdCheckbox = 1001;
    private const int IdClose = 1002;
    private const int IdComboBase = 1100;

    private const int ClientW = 352;
    private const int ClientH = 486;
    private const int RowTop = 210;
    private const int RowH = 44;

    private static IntPtr _hwnd;
    private static readonly IntPtr[] _statusLabels = new IntPtr[4];
    private static IntPtr _check;
    private static IntPtr _font;
    private static IntPtr _boldFont;
    private static int _dpi = 96;
    private static WndProcDelegate _proc = null!;

    private static readonly (string Code, string Name, string DefaultText)[] Keys =
    {
        ("33", "Fn + F4", "默认：麦克风静音"),
        ("35", "Fn + F7", "默认：无"),
        ("1",  "Fn + F8", "默认：投影 (Win+P)"),
        ("27", "Fn + F9", "默认：Windows 设置"),
        ("37", "自定义键", "默认：无")
    };

    private static int S(int v) => (int)Math.Round(v * _dpi / 96.0);

    public static void Show()
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
            return;
        }

        _proc = WndProc;
        var hInstance = GetModuleHandle(null);
        const string cls = "MiControlSettingsV4";

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0003,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            hbrBackground = (IntPtr)16,
            hIcon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "app.ico"), 1, 32, 32, 0x10),
            hIconSm = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "app.ico"), 1, 16, 16, 0x10),
            lpszClassName = cls
        };
        var atom = RegisterClassEx(ref wc);
        Program.LogPublic($"settings RegisterClassEx atom={atom} err={Marshal.GetLastWin32Error()}");

        var rect = new RECT { Left = 0, Top = 0, Right = S(ClientW), Bottom = S(ClientH) };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);
        int winW = rect.Right - rect.Left;
        int winH = rect.Bottom - rect.Top;

        _hwnd = CreateWindowEx(0, cls, "", WS_OVERLAPPEDWINDOW,
            S(300), S(160), winW, winH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        GetClientRect(_hwnd, out var cr);
        Program.LogPublic($"settings hwnd={_hwnd} err={Marshal.GetLastWin32Error()} want={winW}x{winH} client={cr.Right - cr.Left}x{cr.Bottom - cr.Top} dpi={_dpi} scale={(double)_dpi / 96:0.00}");

        // Set the caption with a manually allocated wide string. Passing a managed
        // string through the marshaller truncates to one character here.
        var titlePtr = Marshal.StringToHGlobalAnsi("MiControl 设置");
        try
        {
            var res = SendMessagePtr(_hwnd, WM_SETTEXT, IntPtr.Zero, titlePtr);
            Program.LogPublic($"settings title set res={res} err={Marshal.GetLastWin32Error()}");
        }
        finally { Marshal.FreeHGlobal(titlePtr); }
        ShowWindow(_hwnd, SW_SHOW);
        UpdateWindow(_hwnd);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_CREATE:
                _dpi = (int)GetDpiForWindow(hWnd);
                if (_dpi <= 0) _dpi = 96;
                _font = CreateUiFont(_dpi, false);
                _boldFont = CreateUiFont(_dpi, true);

                // The window was created before the DPI was known, so resize it now.
                var fit = new RECT { Left = 0, Top = 0, Right = S(ClientW), Bottom = S(ClientH) };
                AdjustWindowRectEx(ref fit, WS_OVERLAPPEDWINDOW, false, 0);
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, fit.Right - fit.Left, fit.Bottom - fit.Top, 0x0002 | 0x0004);

                BuildControls(hWnd);
                return IntPtr.Zero;

            case WM_ACTIVATE:
                if (wParam.ToInt64() != 0) RefreshStatus();
                return IntPtr.Zero;

            case WM_COMMAND:
            {
                int id = wParam.ToInt32() & 0xFFFF;
                int notify = (int)((wParam.ToInt64() >> 16) & 0xFFFF);

                if (id == IdCheckbox)
                {
                    bool on = SendMessage(_check, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() == BST_CHECKED;
                    Backend.SetAutoFull(on);
                    return IntPtr.Zero;
                }

                if (id == IdClose)
                {
                    DestroyWindow(_hwnd);
                    return IntPtr.Zero;
                }

                if (notify == CBN_SELCHANGE && id >= IdComboBase && id < IdComboBase + Keys.Length)
                {
                    OnHotkeyChanged(id - IdComboBase, lParam);
                    return IntPtr.Zero;
                }
                break;
            }

            case WM_CLOSE:
                DestroyWindow(_hwnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                _hwnd = IntPtr.Zero;
                _check = IntPtr.Zero;
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static string Describe(string action) => action switch
    {
        "mic" => "麦克风静音",
        "ms:" => "Windows 设置",
        "winp" => "投影 (Win+P)",
        "none" => "无",
        _ when action.StartsWith("app:", StringComparison.Ordinal) => "启动程序",
        _ when action.StartsWith("keys:", StringComparison.Ordinal) => "模拟按键",
        _ => action
    };

    private static void OnHotkeyChanged(int index, IntPtr combo)
    {
        var code = Keys[index].Code;
        int sel = (int)SendMessage(combo, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();

        if (sel == 3) return;   // "current binding" entry: nothing to change
        if (sel == 2)
        {
            KeyCaptureWindow.Show(spec =>
            {
                if (spec is null)
                {
                    FillCombo(combo, code);
                    return;
                }

                bool saved = Backend.SetHotkey(code, "keys:" + spec);
                Program.LogPublic("hotkey " + code + " -> keys:" + spec + " ok=" + saved);
                FillCombo(combo, code);
            });
            return;
        }

        string? action = sel switch
        {
            0 => DefaultAction(code),
            1 => PickProgram(),
            _ => null
        };

        if (action is null)
        {
            FillCombo(combo, code);
            return;
        }

        bool ok = Backend.SetHotkey(code, action);
        Program.LogPublic("hotkey " + code + " -> " + action + " ok=" + ok);
        FillCombo(combo, code);
    }

    private static string DefaultAction(string code) => code switch
    {
        "33" => "mic",
        "27" => "ms:",
        "1" => "winp",
        _ => "none"
    };

    private static string? PickProgram()
    {
        var buffer = new char[512];
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            lpstrFilter = "程序 (*.exe)\0*.exe\0所有文件 (*.*)\0*.*\0\0",
            nFilterIndex = 1,
            lpstrFile = new string(buffer),
            nMaxFile = buffer.Length,
            lpstrTitle = "选择要启动的程序",
            Flags = 0x00001000 | 0x00080000 // FILEMUSTEXIST | EXPLORER
        };

        if (!GetOpenFileName(ref ofn)) return null;

        var path = ofn.lpstrFile;
        int nul = path.IndexOf('\0');
        if (nul >= 0) path = path.Substring(0, nul);
        return path.Length > 0 ? "app:" + path : null;
    }

    private static void BuildControls(IntPtr hWnd)
    {
        var st = Backend.GetStatus();
        var hotkeys = Backend.GetHotkeys();

        var title = Make(hWnd, "STATIC", "电池与性能", WS_CHILD | WS_VISIBLE | SS_LEFT, 16, 12, 400, 26, 0);
        SendMessage(title, WM_SETFONT, _boldFont, (IntPtr)1);

        _statusLabels[0] = AddStatusLabel(hWnd, $"电量：{st.Battery}%", 18, 44, 316);
        _statusLabels[1] = AddStatusLabel(hWnd, $"运行模式：{st.PerfName}", 18, 70, 316);
        _statusLabels[2] = AddStatusLabel(hWnd, $"充电上限：{st.ChargePct}%", 18, 96, 316);
        var health0 = st.Health > 0 ? $"{st.Health}%" : "—";
        var adapter0 = st.Ac ? (st.Watts > 0 ? $"{st.Watts} W" : "已连接") : "未接电源";
        _statusLabels[3] = AddStatusLabel(hWnd, $"电池健康：{health0}   适配器：{adapter0}", 18, 122, 316);
        _check = Make(hWnd, "BUTTON", "插电时自动切换到全速模式",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTOCHECKBOX, 18, 150, 316, 26, IdCheckbox);
        SendMessage(_check, WM_SETFONT, _font, (IntPtr)1);
        if (Backend.GetAutoFull()) SendMessage(_check, BM_SETCHECK, (IntPtr)BST_CHECKED, IntPtr.Zero);

        var hkTitle = Make(hWnd, "STATIC", "快捷键", WS_CHILD | WS_VISIBLE | SS_LEFT, 16, 184, 200, 26, 0);
        SendMessage(hkTitle, WM_SETFONT, _boldFont, (IntPtr)1);

        for (int i = 0; i < Keys.Length; i++)
        {
            int top = RowTop + i * RowH;

            var name = Make(hWnd, "STATIC", Keys[i].Name, WS_CHILD | WS_VISIBLE | SS_LEFT, 18, top + 6, 100, 26, 0);
            SendMessage(name, WM_SETFONT, _font, (IntPtr)1);

            var combo = Make(hWnd, "COMBOBOX", "",
                WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST | CBS_HASSTRINGS,
                126, top, 208, 200, IdComboBase + i);
            SendMessage(combo, WM_SETFONT, _font, (IntPtr)1);
            FillCombo(combo, Keys[i].Code);
        }

        var close = Make(hWnd, "BUTTON", "关闭",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON, 244, 438, 90, 30, IdClose);
        SendMessage(close, WM_SETFONT, _font, (IntPtr)1);
    }

    /// <summary>Rebuild the combo entries from the binding the service currently holds.</summary>
    private static void FillCombo(IntPtr combo, string code)
    {
        var bindings = Backend.GetHotkeys();
        string current = bindings.TryGetValue(code, out var bound) && !string.IsNullOrEmpty(bound)
            ? bound
            : "none";

        SendMessage(combo, CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        SendMessageStr(combo, CB_ADDSTRING, IntPtr.Zero, "默认：" + Describe(DefaultAction(code)));
        SendMessageStr(combo, CB_ADDSTRING, IntPtr.Zero, "启动程序…");
        SendMessageStr(combo, CB_ADDSTRING, IntPtr.Zero, "模拟按键…");

        int selected = 0;
        if (current.StartsWith("app:", StringComparison.Ordinal))
        {
            SendMessageStr(combo, CB_ADDSTRING, IntPtr.Zero, "当前：" + Path.GetFileName(current.Substring(4)));
            selected = 3;
        }
        else if (current.StartsWith("keys:", StringComparison.Ordinal))
        {
            SendMessageStr(combo, CB_ADDSTRING, IntPtr.Zero, "当前：" + Friendly(current.Substring(5)));
            selected = 3;
        }

        SendMessage(combo, CB_SETCURSEL, (IntPtr)selected, IntPtr.Zero);
    }

    private static string Friendly(string spec)
    {
        var names = new List<string>();
        foreach (var token in spec.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(token.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out int vk)) continue;
            names.Add(vk switch
            {
                0x11 => "Ctrl",
                0x10 => "Shift",
                0x12 => "Alt",
                0x5B or 0x5C => "Win",
                >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
                >= 0x30 and <= 0x39 => ((char)vk).ToString(),
                >= 0x41 and <= 0x5A => ((char)vk).ToString(),
                _ => token
            });
        }
        return names.Count > 0 ? string.Join("+", names) : spec;
    }

    private static void AddLabel(IntPtr hWnd, string text, int x, int y, int w)
    {
        var c = Make(hWnd, "STATIC", text, WS_CHILD | WS_VISIBLE | SS_LEFT, x, y, w, 22, 0);
        SendMessage(c, WM_SETFONT, _font, (IntPtr)1);
    }

    private static IntPtr AddStatusLabel(IntPtr hWnd, string text, int x, int y, int w)
    {
        var c = Make(hWnd, "STATIC", text, WS_CHILD | WS_VISIBLE | SS_LEFT, x, y, w, 22, 0);
        SendMessage(c, WM_SETFONT, _font, (IntPtr)1);
        return c;
    }

    /// <summary>Refresh the status lines from the service (called when the window is activated).</summary>
    private static void RefreshStatus()
    {
        if (_statusLabels.Length == 0 || _statusLabels[0] == IntPtr.Zero) return;
        var st = Backend.GetStatus();
        SetWindowText(_statusLabels[0], $"电量：{st.Battery}%");
        SetWindowText(_statusLabels[1], $"运行模式：{st.PerfName}");
        SetWindowText(_statusLabels[2], $"充电上限：{st.ChargePct}%");

        var health = st.Health > 0 ? $"{st.Health}%" : "—";
        var adapter = st.Ac ? (st.Watts > 0 ? $"{st.Watts} W" : "已连接") : "未接电源";
        SetWindowText(_statusLabels[3], $"电池健康：{health}     适配器：{adapter}");
    }

    private static IntPtr Make(IntPtr parent, string cls, string text, int style,
        int x, int y, int w, int h, int id)
    {
        return CreateWindowEx(0, cls, text, style,
            S(x), S(y), S(w), S(h), parent, (IntPtr)id, IntPtr.Zero, IntPtr.Zero);
    }

    private static IntPtr CreateUiFont(int dpi, bool bold)
    {
        var ncm = new NONCLIENTMETRICS { cbSize = Marshal.SizeOf<NONCLIENTMETRICS>() };
        if (!SystemParametersInfo(SPI_GETNONCLIENTMETRICS, ncm.cbSize, ref ncm, 0))
            return GetStockObject(17);

        var lf = ncm.lfMessageFont;
        lf.lfHeight = -MulDiv(9, dpi, 72);
        lf.lfWeight = bold ? 700 : 400;
        return CreateFontIndirect(ref lf);
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
    private struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NONCLIENTMETRICS
    {
        public int cbSize;
        public int iBorderWidth;
        public int iScrollWidth;
        public int iScrollHeight;
        public int iCaptionWidth;
        public int iCaptionHeight;
        public LOGFONT lfCaptionFont;
        public int iSmCaptionWidth;
        public int iSmCaptionHeight;
        public LOGFONT lfSmCaptionFont;
        public int iMenuWidth;
        public int iMenuHeight;
        public LOGFONT lfMenuFont;
        public LOGFONT lfStatusFont;
        public LOGFONT lfMessageFont;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFilter;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFile;
        public int nMaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
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
    private static extern bool SetWindowText(IntPtr hWnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageStr(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessagePtr(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int action, int param, ref NONCLIENTMETRICS data, int winIni);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirect(ref LOGFONT lf);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string name, int type, int cx, int cy, int load);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT rect, int style, bool menu, int exStyle);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("kernel32.dll")]
    private static extern int MulDiv(int number, int numerator, int denominator);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);
}









