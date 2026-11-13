param([switch]$Watch, [int]$Seconds = 60)

$ErrorActionPreference = "Stop"

if (-not ("MicProbe" -as [type])) {
Add-Type -Language CSharp @"
using System;
using System.Runtime.InteropServices;

public static class MicProbe
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                     [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IntPtr store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr p);
        int UnregisterControlChangeNotify(IntPtr p);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
        int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(uint ch, out float level);
        int GetChannelVolumeLevelScalar(uint ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public static string Query()
    {
        var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
        IMMDevice device;
        int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out device);
        if (hr != 0) return "no capture device (hr=0x" + hr.ToString("X8") + ")";

        string id;
        device.GetId(out id);

        Guid iid = typeof(IAudioEndpointVolume).GUID;
        object iface;
        hr = device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out iface);
        if (hr != 0) return "activate failed (hr=0x" + hr.ToString("X8") + ")";

        var volume = (IAudioEndpointVolume)iface;
        bool mute;
        float scalar;
        volume.GetMute(out mute);
        volume.GetMasterVolumeLevelScalar(out scalar);

        return string.Format("muted={0}  volume={1:P0}  id={2}", mute, scalar, id);
    }
}
"@
}

if (-not $Watch) {
    [MicProbe]::Query()
    exit 0
}

$end = (Get-Date).AddSeconds($Seconds)
$last = ""
while ((Get-Date) -lt $end) {
    $now = [MicProbe]::Query()
    if ($now -ne $last) {
        "{0}  {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $now
        $last = $now
    }
    Start-Sleep -Milliseconds 400
}
