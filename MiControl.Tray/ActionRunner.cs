using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MiControl.Tray;

/// <summary>Executes a configured hotkey action in the user session.</summary>
internal static class ActionRunner
{
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void Run(string action)
    {
        if (string.IsNullOrWhiteSpace(action) || action == "none") return;

        if (action.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            var raw = action.Substring(4).Trim();
            if (raw.Length == 0) return;

            // "app:<path>|<args>"
            var parts = raw.Split('|', 2);
            var path = parts[0].Trim().Trim('"');
            var args = parts.Length > 1 ? parts[1] : "";
            if (path.Length == 0) return;

            if (args.Length > 0) Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        if (action.StartsWith("ms:", StringComparison.OrdinalIgnoreCase))
        {
            var page = action.Substring(3).Trim();
            var uri = page.Length == 0 ? "ms-settings:" : "ms-settings:" + page;
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return;
        }

        if (action.StartsWith("keys:", StringComparison.OrdinalIgnoreCase))
        {
            SendKeySpec(action.Substring(5));
            return;
        }

        if (action.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
        {
            var before = ResolutionSwitcher.CurrentMode();
            ResolutionSwitcher.Toggle(action.Substring(4));
            var after = ResolutionSwitcher.CurrentMode();
            if (after != before)
                Osd.Show($"{after.W} × {after.H}" + (after.Hz > 0 ? $" · {after.Hz} Hz" : ""));
            return;
        }
    }

    /// <summary>Spec format: "0x24" or "0x24+0x11+0x10" (main key first, then modifiers).</summary>
    private static void SendKeySpec(string spec)
    {
        var keys = KeyNames.ParseOrdered(spec);
        if (keys.Count == 0) return;

        // hold modifiers (all but the first), press the main key, release in reverse
        for (int i = 1; i < keys.Count; i++) keybd_event(keys[i], 0, 0, UIntPtr.Zero);
        keybd_event(keys[0], 0, 0, UIntPtr.Zero);
        keybd_event(keys[0], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        for (int i = keys.Count - 1; i >= 1; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
