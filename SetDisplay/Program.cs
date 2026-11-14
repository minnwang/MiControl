using System.Runtime.InteropServices;

namespace SetDisplay;

/// <summary>
/// Cycles the primary display through the resolutions listed in SetDisplay.ini
/// (one "WIDTHxHEIGHT" per line, e.g. "2880x1800"). Each run switches to the next
/// entry after the current mode, so a single hotkey toggles between two modes.
/// </summary>
internal static class Program
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int CDS_UPDATEREGISTRY = 0x00000001;
    private const int CDS_TEST = 0x00000002;

    private const string LogName = "setdisplay.log";

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

    private static string LogPath => Path.Combine(AppContext.BaseDirectory, LogName);

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}"); }
        catch { }
    }

    [STAThread]
    private static void Main()
    {
        try { Run(); }
        catch (Exception ex) { Log("error: " + ex.Message); }
    }

    private static void Run()
    {
        var ini = Path.Combine(AppContext.BaseDirectory, "SetDisplay.ini");
        if (!File.Exists(ini))
        {
            Log("SetDisplay.ini not found");
            return;
        }

        var modes = new List<(uint W, uint H)>();
        foreach (var raw in File.ReadAllLines(ini))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var parts = line.ToLowerInvariant().Split('x');
            if (parts.Length == 2 && uint.TryParse(parts[0], out var w) && uint.TryParse(parts[1], out var h))
                modes.Add((w, h));
        }

        if (modes.Count == 0)
        {
            Log("no modes in ini");
            return;
        }

        var current = new DEVMODE { dmDeviceName = "", dmFormName = "" };
        current.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref current))
        {
            Log("EnumDisplaySettings failed");
            return;
        }

        int index = modes.FindIndex(m => m.W == current.dmPelsWidth && m.H == current.dmPelsHeight);
        var target = modes[(index + 1) % modes.Count];

        var dm = new DEVMODE { dmDeviceName = "", dmFormName = "" };
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
        {
            Log("EnumDisplaySettings (target) failed");
            return;
        }

        dm.dmPelsWidth = target.W;
        dm.dmPelsHeight = target.H;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

        int test = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != 0)
        {
            Log($"mode {target.W}x{target.H} not supported (test={test})");
            return;
        }

        int result = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        Log($"switch {current.dmPelsWidth}x{current.dmPelsHeight} -> {target.W}x{target.H} result={result}");
    }
}
