using System.Runtime.InteropServices;

namespace MiControl.Tray;

/// <summary>Sends the standard Win+P projection shortcut.</summary>
internal static class HotkeySender
{
    private const byte VK_LWIN = 0x5B;
    private const byte VK_P = 0x50;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void SendWinP()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_P, 0, 0, UIntPtr.Zero);
        keybd_event(VK_P, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
