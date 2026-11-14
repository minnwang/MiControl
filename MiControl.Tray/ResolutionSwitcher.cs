using System.Runtime.InteropServices;

namespace MiControl.Tray;

/// <summary>
/// Reads the display modes offered by the primary adapter and switches between two of
/// them. Used by the "res:AxB|CxD" hotkey action, which toggles both ways.
/// </summary>
internal static class ResolutionSwitcher
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int CDS_UPDATEREGISTRY = 0x00000001;
    private const int CDS_TEST = 0x00000002;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr param);

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE { dmDeviceName = "", dmFormName = "" };
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        return dm;
    }

    public static (uint W, uint H) Current()
    {
        var (w, h, _) = CurrentMode();
        return (w, h);
    }

    public static (uint W, uint H, uint Hz) CurrentMode()
    {
        var dm = NewDevMode();
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            return (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);

        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 0, 0);
        return ((uint)bounds.Width, (uint)bounds.Height, 0);
    }

    /// <summary>Distinct modes supported by the primary display, largest first.</summary>
    public static List<(uint W, uint H)> Enumerate()
    {
        var seen = new HashSet<(uint, uint)>();
        var modes = new List<(uint W, uint H)>();

        for (int i = 0; ; i++)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettings(null, i, ref dm)) break;

            if (dm.dmPelsWidth < 800 || dm.dmPelsHeight < 600) continue;

            var key = (dm.dmPelsWidth, dm.dmPelsHeight);
            if (seen.Add(key)) modes.Add(key);
        }

        modes.Sort((a, b) => (b.W * b.H).CompareTo(a.W * a.H));
        return modes;
    }

    public static bool Apply(uint width, uint height)
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return false;

        // Only the size changes; refresh rate and colour depth stay as they are.
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

        return ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>Spec: "2880x1800|1920x1200". Switches to the other one.</summary>
    public static void Toggle(string spec)
    {
        var parts = spec.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var targets = new List<(uint W, uint H)>();
        foreach (var p in parts)
        {
            var wh = p.ToLowerInvariant().Split('x');
            if (wh.Length == 2 && uint.TryParse(wh[0], out var w) && uint.TryParse(wh[1], out var h))
                targets.Add((w, h));
        }

        if (targets.Count == 0) return;

        var current = Current();
        var next = targets.FirstOrDefault(t => t.W != current.W || t.H != current.H);
        if (next.W == 0) next = targets[0];

        Apply(next.W, next.H);
    }
}
