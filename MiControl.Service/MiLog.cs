using System.Text;

namespace MiControl.Service;

/// <summary>
/// Small circular log: keeps the most recent 100 entries in
/// C:\ProgramData\MiControl\mi.log. Used only for real anomalies
/// (EC drift and rejected writes), never for routine activity.
/// </summary>
internal static class MiLog
{
    private const int MaxEntries = 100;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiControl", "mi.log");

    private static readonly object Gate = new();
    private static readonly List<string> Entries = new();

    public static void Write(string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{category}] {message}";

        lock (Gate)
        {
            if (Entries.Count == 0) LoadExisting();

            Entries.Add(line);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllLines(FilePath, Entries, Encoding.UTF8);
            }
            catch { }
        }
    }

    public static void Drift(string item, string wanted, string actual, string result)
        => Write("drift", $"{item} {wanted} -> {actual} ({result})");

    public static void Failure(string item, string wanted, string readback, int attempt)
        => Write("fail", $"{item} wanted={wanted} readback={readback} attempt={attempt}");

    private static void LoadExisting()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var lines = File.ReadAllLines(FilePath);
            var start = Math.Max(0, lines.Length - MaxEntries);
            for (int i = start; i < lines.Length; i++)
                if (lines[i].Length > 0) Entries.Add(lines[i]);
        }
        catch { }
    }
}
