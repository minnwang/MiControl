using System.Management;

namespace MiControl.Service;

/// <summary>
/// Xiaomi/Redmi MIFS ACPI-WMI transport.
/// root\wmi : MICommonInterface.MiInterface(InData[32], OutData[32], ReturnCode)
/// Packet: [0]=0x00 [1]=0xFA(GET)/0xFB(SET) [2]=0x00 [3]=func [4]=arg [5]=0 [6]=arg2 [7]=0
/// Response byte1: 0x80 = supported/ok, 0xE0 = unsupported.
/// </summary>
public sealed class MifsClient : IDisposable
{
    private const byte OpGet = 0xFA;
    private const byte OpSet = 0xFB;
    private const byte StatusOk = 0x80;

    private readonly ManagementObject _instance;
    private readonly object _gate = new();

    public string InstanceName { get; }

    public MifsClient()
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MICommonInterface");
        foreach (ManagementObject o in searcher.Get())
        {
            _instance = o;
            break;
        }
        if (_instance is null)
            throw new InvalidOperationException("MICommonInterface instance not found (is this a Xiaomi/Redmi laptop?)");

        InstanceName = _instance["InstanceName"]?.ToString() ?? "?";
    }

    private (bool ok, byte[] data) Call(byte op, byte func, byte arg, byte arg2)
    {
        lock (_gate)
        {
            var buf = new byte[32];
            buf[0] = 0x00;
            buf[1] = op;
            buf[2] = 0x00;
            buf[3] = func;
            buf[4] = arg;
            buf[5] = 0x00;
            buf[6] = arg2;
            buf[7] = 0x00;

            using var inParams = _instance.GetMethodParameters("MiInterface");
            inParams["InData"] = buf;
            using var outParams = _instance.InvokeMethod("MiInterface", inParams, null);
            var data = (byte[])outParams["OutData"];
            if (data.Length < 8) Array.Resize(ref data, 32);
            return (data[1] == StatusOk, data);
        }
    }

    public bool TryGet(byte func, byte arg, byte arg2, out byte[] data)
    {
        var r = Call(OpGet, func, arg, arg2);
        data = r.data;
        return r.ok;
    }

    public bool TrySet(byte func, byte arg, byte arg2)
    {
        var r = Call(OpSet, func, arg, arg2);
        return r.ok;
    }

    // ---------------- high level ----------------

    public PerfMode? GetPerfMode()
    {
        if (!TryGet(0x08, 0, 0, out var d)) return null;
        return (PerfMode)d[4];
    }

    /// <summary>SET then GET; returns true only when the firmware readback matches.</summary>
    public bool SetPerfMode(PerfMode mode)
    {
        if (!TrySet(0x08, (byte)mode, 0)) return false;
        Thread.Sleep(150);
        return GetPerfMode() == mode;
    }

    public ChargeLevel? GetChargeLevel()
    {
        if (!TryGet(0x10, 0x02, 0, out var d)) return null;
        return (ChargeLevel)d[6];
    }

    /// <summary>Re-arm without readback: fast path used on power events.</summary>
    public bool ArmChargeLevel(ChargeLevel level) => TrySet(0x10, 0x02, (byte)level);

    public bool SetChargeLevel(ChargeLevel level)
    {
        if (!TrySet(0x10, 0x02, (byte)level)) return false;
        Thread.Sleep(150);
        return GetChargeLevel() == level;
    }

    public int? GetBatteryHealth()
    {
        if (!TryGet(0x10, 0x01, 0, out var d)) return null;
        return d[6];
    }

    public int? GetAdapterWatts()
    {
        if (!TryGet(0x10, 0x06, 0, out var d)) return null;
        return d[6];
    }

    /// <summary>Hardware microphone mute (WMI function 0x0A). Value lives in OUT[4].</summary>
    public bool? GetMicMute()
    {
        if (!TryGet(0x0A, 0, 0, out var d)) return null;
        return d[4] != 0;
    }

    public bool? ToggleMicMute()
    {
        var current = GetMicMute();
        if (current is null) return null;

        bool target = !current.Value;
        if (!TrySet(0x0A, (byte)(target ? 1 : 0), 0)) return null;

        Thread.Sleep(120);
        var after = GetMicMute();
        return after is null ? null : after;
    }

    public void Dispose() => _instance?.Dispose();
}

public enum PerfMode : byte
{
    Quiet = 0x02,
    Performance = 0x03,
    FullSpeed = 0x04,
    Smart = 0x09,
    Eco = 0x0A
}

// Code -> percent mapping calibrated on TM2426 (Redmi Book Pro 14 2026).
// Verified: 0x04 stops charging at 90%. Others follow the EC 0xA7 write order
// seen in the Kernel patch thread for this firmware family.
public enum ChargeLevel : byte
{
    Percent100 = 0x00,
    Percent90 = 0x04,
    Percent80 = 0x01,
    Percent70 = 0x05,
    Percent60 = 0x06,
    Percent50 = 0x07,
    Percent40 = 0x08
}

public static class ChargeLevelExtensions
{
    public static readonly int[] SupportedPercents = { 100, 90, 80, 70, 60, 50, 40 };

    public static int ToPercent(this ChargeLevel l) => l switch
    {
        ChargeLevel.Percent100 => 100,
        ChargeLevel.Percent90 => 90,
        ChargeLevel.Percent80 => 80,
        ChargeLevel.Percent70 => 70,
        ChargeLevel.Percent60 => 60,
        ChargeLevel.Percent50 => 50,
        ChargeLevel.Percent40 => 40,
        _ => -1
    };

    /// <summary>Snap a user-visible percentage to the nearest firmware-supported level.</summary>
    public static ChargeLevel FromPercent(int pct)
    {
        int best = SupportedPercents[0];
        foreach (var p in SupportedPercents)
        {
            if (Math.Abs(p - pct) < Math.Abs(best - pct)) best = p;
        }
        return best switch
        {
            100 => ChargeLevel.Percent100,
            90 => ChargeLevel.Percent90,
            80 => ChargeLevel.Percent80,
            70 => ChargeLevel.Percent70,
            60 => ChargeLevel.Percent60,
            50 => ChargeLevel.Percent50,
            _ => ChargeLevel.Percent40
        };
    }
}


