using System.Text;
namespace MiControl.Service;
internal static class TimingLog
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiControl", "timing.log");
    public static void Log(string m)
    {
        if (true) return;   // diagnostics disabled
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  [svc] {m}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }
}

