using System.Windows.Forms;

namespace MiControl.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Global\MiControlTray", out bool created);
        if (!created)
        {
            // Another instance is running: ask it to show its settings window.
            try
            {
                using var activate = EventWaitHandle.OpenExisting(@"Global\MiControlTrayActivate");
                activate.Set();
            }
            catch { }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Application.Run(new TrayApp());
    }
}
