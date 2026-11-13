using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiControl.Service;

/// <summary>Loopback IPC. Tray app connects, sends one JSON line, receives one JSON line.</summary>
public sealed class IpcServer : BackgroundService
{
    private readonly MifsClient _mifs;
    private readonly StateStore _store;
    private readonly ILogger<IpcServer> _log;

    private static readonly List<StreamWriter> Writers = new();
    private static readonly object WriterGate = new();

    /// <summary>True while a hotkey/power handler is running a WMI sequence.</summary>
    public static volatile bool WmiBusy;

    private static volatile bool _dirty;
    private static volatile bool _forceRefresh;
    private DateTime _cacheAt = DateTime.MinValue;
    private PerfMode? _cachedPerf;
    private ChargeLevel? _cachedCharge;
    private int? _cachedHealth;
    private int? _cachedWatts;

    public IpcServer(MifsClient mifs, StateStore store, ILogger<IpcServer> log)
    {
        _mifs = mifs;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _current = this;
        ActionQueue.Changed += Broadcast;
        var listener = new TcpListener(IPAddress.Loopback, _store.State.Port);
        listener.Start();
        _log.LogInformation("IPC listening on 127.0.0.1:{Port}", _store.State.Port);

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "accept failed"); continue; }

            _ = Task.Run(() => Handle(client), ct);
        }

        listener.Stop();
    }

    private static IpcServer? _current;

    public static void InvalidateStatus() => _dirty = true;

    /// <summary>Build a fresh status and push it (with data) to every connected tray.</summary>
    public static void PushFreshStatus()
    {
        var self = _current;
        if (self is null) return;
        InvalidateStatus();
        _forceRefresh = true;
        JsonObject status;
        try { status = self.Status(); } finally { _forceRefresh = false; }
        status["push"] = "status";
        lock (WriterGate)
        {
            foreach (var w in Writers.ToArray())
            {
                try { w.WriteLine(status.ToJsonString()); } catch { }
            }
        }
    }

    public static void BroadcastStatusPayload(JsonObject payload)
    {
        payload["push"] = "status";
        TimingLog.Log("push ac=" + payload["ac"] + " perf=" + payload["perf"]);
        var line = payload.ToJsonString();
        lock (WriterGate)
        {
            foreach (var w in Writers.ToArray())
            {
                try { w.WriteLine(line); } catch { }
            }
        }
    }

    public static void BroadcastStatus()
    {
        lock (WriterGate)
        {
            foreach (var w in Writers.ToArray())
            {
                try { w.WriteLine(new JsonObject { ["push"] = "status" }.ToJsonString()); } catch { }
            }
        }
    }

    private static void Broadcast()
    {
        var actions = ActionQueue.Drain();
        if (actions.Length == 0) return;

        var arr = new JsonArray();
        foreach (var a in actions) arr.Add(a);
        var line = new JsonObject { ["push"] = "actions", ["actions"] = arr }.ToJsonString();

        lock (WriterGate)
        {
            foreach (var w in Writers.ToArray())
            {
                try { w.WriteLine(line); } catch { }
            }
        }
    }

    private void Handle(TcpClient client)
    {
        try
        {
            using var _ = client;
            client.ReceiveTimeout = 5000;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            lock (WriterGate) Writers.Add(writer);
            try {

            while (true)
            {
            var line = reader.ReadLine();
            if (line is null) return;

            var req = JsonNode.Parse(line)?.AsObject();
            if (req is null) { Reply(writer, Error("bad request")); return; }

            var token = req["token"]?.GetValue<string>();
            if (!string.Equals(token, _store.State.Token, StringComparison.Ordinal))
            {
                Reply(writer, Error("unauthorized"));
                return;
            }

            var cmd = req["cmd"]?.GetValue<string>() ?? "";
            switch (cmd)
            {
                case "status":
                    Reply(writer, Status());
                    break;

                case "setPerf":
                {
                    var mode = (PerfMode)(byte)req["mode"]!.GetValue<int>();
                    bool ok = _mifs.SetPerfMode(mode);
                    _cacheAt = DateTime.MinValue;
                    if (ok) _store.Update(s => s.PerfMode = mode);
                    Reply(writer, Result(ok, ok ? null : "firmware readback mismatch"));
                    break;
                }

                case "setCharge":
                {
                    var pct = req["pct"]!.GetValue<int>();
                    var level = ChargeLevelExtensions.FromPercent(pct);
                    bool ok = _mifs.SetChargeLevel(level);
                    _cacheAt = DateTime.MinValue;
                    if (ok) _store.Update(s => { s.ChargeLevel = level; s.ChargeProtectEnabled = pct < 100; });
                    Reply(writer, Result(ok, ok ? null : "firmware readback mismatch"));
                    break;
                }

                case "setAutoFull":
                {
                    bool enabled = req["enabled"]!.GetValue<bool>();
                    _store.Update(s => s.AutoFullSpeedOnAc = enabled);
                    Reply(writer, Result(true, null));
                    break;
                }

                case "getConfig":
                {
                    var hk = new JsonObject();
                    lock (_store)
                    {
                        foreach (var kv in _store.State.Hotkeys) hk[kv.Key] = kv.Value;
                    }
                    Reply(writer, new JsonObject { ["ok"] = true, ["hotkeys"] = hk, ["autoStart"] = _store.State.AutoStart });
                    break;
                }

                case "setAutoStart":
                {
                    bool enabled = req["enabled"]?.GetValue<bool>() ?? true;
                    _store.Update(s => s.AutoStart = enabled);
                    Reply(writer, Result(true, null));
                    break;
                }

                case "setHotkey":
                {
                    var key = req["key"]?.GetValue<string>() ?? "";
                    var action = req["action"]?.GetValue<string>() ?? "none";
                    if (key.Length == 0) { Reply(writer, Error("missing key")); break; }
                    _store.Update(s => s.Hotkeys[key] = action);
                    Reply(writer, Result(true, null));
                    break;
                }

                case "pollActions":
                {
                    var actions = ActionQueue.Drain();
                    var arr = new JsonArray();
                    foreach (var a in actions) arr.Add(a);
                    var o = new JsonObject { ["ok"] = true, ["actions"] = arr };
                    Reply(writer, o);
                    break;
                }

                default:
                    Reply(writer, Error("unknown command"));
                    break;
            }
            }
            }
            finally { lock (WriterGate) Writers.Remove(writer); }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ipc handler failed");
        }
    }

    // Cache the WMI-backed values: they only change on user action or power events.
    private void RefreshCache()
    {
        if (WmiBusy && !_forceRefresh) return;   // never block a status request behind a WMI sequence
        if (_dirty) { _dirty = false; _cacheAt = DateTime.MinValue; }
        if ((DateTime.UtcNow - _cacheAt).TotalSeconds < 30) return;
        _cachedPerf = _mifs.GetPerfMode();
        _cachedCharge = _mifs.GetChargeLevel();
        _cachedHealth = _mifs.GetBatteryHealth();
        _cachedWatts = _mifs.GetAdapterWatts();
        _cacheAt = DateTime.UtcNow;
    }

    private JsonObject Status()
    {
        var st = _store.State;
        RefreshCache();
        var perf = WmiBusy ? (PerfMode)_store.State.PerfMode : _cachedPerf;   // optimistic while switching
        var charge = _cachedCharge;
        GetSystemPowerStatus(out var ps);

        var o = new JsonObject
        {
            ["ok"] = true,
            ["ac"] = ps.ACLineStatus == 1,
            ["charging"] = (ps.BatteryFlag & 0x08) != 0,
            ["battery"] = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : -1,
            ["perf"] = perf is null ? -1 : (int)perf.Value,
            ["chargeCode"] = charge is null ? -1 : (int)charge.Value,
            ["chargePct"] = charge is null ? -1 : charge.Value.ToPercent(),
            ["health"] = _cachedHealth ?? -1,
            ["watts"] = _cachedWatts ?? -1,
            ["autoFull"] = st.AutoFullSpeedOnAc,
            ["chargeEnabled"] = st.ChargeProtectEnabled
        };
        return o;
    }

    private static JsonObject Result(bool ok, string? message) => new()
    {
        ["ok"] = ok,
        ["message"] = message ?? ""
    };

    private static JsonObject Error(string message) => new()
    {
        ["ok"] = false,
        ["message"] = message
    };

    private static void Reply(StreamWriter w, JsonObject o)
    {
        lock (WriterGate) { w.WriteLine(o.ToJsonString()); }
    }

    // ---------------- power status ----------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    internal static SYSTEM_POWER_STATUS PowerStatus()
    {
        GetSystemPowerStatus(out var s);
        return s;
    }
}



















