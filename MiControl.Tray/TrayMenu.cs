using System.Text;
using System.Windows.Forms;

namespace MiControl.Tray;

/// <summary>
/// Context menu with key logging. Used to find out whether the screenshot key
/// (PrintScreen / Fn+F11) reaches the menu window at all while it is open.
/// </summary>
internal sealed class TrayMenu : ContextMenuStrip
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MiControl", "menu-keys.log");

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    protected override void OnOpened(EventArgs e)
    {
        Log("--- menu opened ---");
        base.OnOpened(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is WM_KEYDOWN or WM_SYSKEYDOWN or WM_KEYUP)
        {
            Log($"WndProc msg=0x{m.Msg:X4} vk=0x{m.WParam.ToInt32():X2} hwnd={m.HWnd}");
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message m, Keys keyData)
    {
        Log($"ProcessCmdKey keyData={keyData} msg=0x{m.Msg:X4} vk=0x{m.WParam.ToInt32():X2}");
        return base.ProcessCmdKey(ref m, keyData);
    }
}
