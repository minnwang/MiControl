using System.Management;
using System.Text;

namespace MiControl.Service;

/// <summary>
/// Subscribes to firmware HID event classes (root\wmi HID_EVENT20..23) and dispatches
/// vendor hotkeys according to the user mapping in state.json.
/// Captured codes on TM2426: 0x01 Fn+F8, 0x1B Fn+F9, 0x21 Fn+F4, 0x23 Fn+F7, 0x25 AI.
/// </summary>
public sealed class HotkeyListener : BackgroundService
{
    private static readonly string[] Classes = { "HID_EVENT20", "HID_EVENT21", "HID_EVENT22", "HID_EVENT23" };

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MiControl", "keys.log");

    private readonly StateStore _store;
    private readonly ILogger<HotkeyListener> _log;
    private readonly List<ManagementEventWatcher> _watchers = new();

    public HotkeyListener(StateStore store, ILogger<HotkeyListener> log)
    {
        _store = store;
        _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        foreach (var cls in Classes)
        {
            try
            {
                var watcher = new ManagementEventWatcher(
                    new ManagementScope(@"root\wmi"),
                    new WqlEventQuery($"SELECT * FROM {cls}"));

                var name = cls;
                watcher.EventArrived += (_, e) =>
                {
                    try { Handle(name, e.NewEvent["EventDetail"] as byte[]); }
                    catch (Exception ex) { Append($"{DateTime.Now:HH:mm:ss.fff}  {name}  handler error {ex.Message}"); }
                };

                watcher.Start();
                _watchers.Add(watcher);
                Append($"{DateTime.Now:HH:mm:ss.fff}  subscribed {name}");
            }
            catch (Exception ex)
            {
                Append($"{DateTime.Now:HH:mm:ss.fff}  subscribe {cls} failed: {ex.Message}");
                _log.LogWarning(ex, "subscribe {Class} failed", cls);
            }
        }

        return Task.Delay(Timeout.Infinite, ct);
    }

    private void Handle(string source, byte[]? payload)
    {
        if (payload is null || payload.Length < 3) return;

        var hex = string.Join(" ", payload.Select(b => b.ToString("X2")));
        Append($"{DateTime.Now:HH:mm:ss.fff}  {source}  [{hex}]");

        var code = payload[1].ToString();
        var arg = payload[2];

        string action;
        lock (_store)
        {
            if (!_store.State.Hotkeys.TryGetValue(code, out action!))
                return;
        }

        if (string.IsNullOrEmpty(action) || action is "none")
            return;

        if (action == "mic")
        {
            // On TM2426 the payload byte is inverted relative to the LED:
            // 0 -> LED on (muted), 1 -> LED off (live).
            ActionQueue.Push(arg == 0 ? "mic:on" : "mic:off");
            return;
        }

        // Everything else is executed in the user session by the tray app:
        //   settings | winp | app:<path> | ms:<page> | keys:<vk[,vk...]>
        ActionQueue.Push("act:" + action);
    }

    private static void Append(string line)
    {
        if (true) return;   // key logging disabled
        try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); }
        catch { }
    }

    public override void Dispose()
    {
        foreach (var w in _watchers)
        {
            try { w.Stop(); w.Dispose(); } catch { }
        }
        base.Dispose();
    }
}

