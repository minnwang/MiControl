using System.Runtime.InteropServices;

namespace MiControl.Tray;

/// <summary>
/// Toggles the default communications microphone mute through the Windows Core
/// Audio API. The firmware key only drives its LED; the actual mute has to be done
/// by software once the vendor service is gone.
/// </summary>
internal static class MicControl
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

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

    private static IAudioEndpointVolume? GetEndpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out var device);
            if (hr != 0) return null;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var iface);
            if (hr != 0) return null;

            return iface as IAudioEndpointVolume;
        }
        catch
        {
            return null;
        }
    }

    public static bool? IsMuted()
    {
        var ep = GetEndpoint();
        if (ep is null) return null;
        try
        {
            ep.GetMute(out bool muted);
            return muted;
        }
        catch { return null; }
    }

    /// <summary>Applies an absolute mute state; returns the resulting value.</summary>
    public static bool? SetMute(bool muted)
    {
        var ep = GetEndpoint();
        if (ep is null) return null;

        try
        {
            Guid ctx = Guid.Empty;
            int hr = ep.SetMute(muted, ref ctx);
            if (hr != 0) return null;

            ep.GetMute(out bool after);
            return after;
        }
        catch { return null; }
    }

    /// <summary>Flips the mute state and returns the new value (null on failure).</summary>
    public static bool? Toggle()
    {
        var ep = GetEndpoint();
        if (ep is null) return null;

        try
        {
            ep.GetMute(out bool current);
            bool target = !current;
            Guid ctx = Guid.Empty;
            int hr = ep.SetMute(target, ref ctx);
            if (hr != 0) return null;

            ep.GetMute(out bool after);
            return after;
        }
        catch { return null; }
    }
}
