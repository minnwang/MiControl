using System.Text.Json.Nodes;
using System.Threading;


namespace MiControl.Service;

/// <summary>
/// Keeps the EC charge limit armed and mirrors the AC/DC performance mode.
/// Driven by Windows power notifications (no polling): plug/unplug and resume
/// are delivered immediately, with a five minute safety tick as a fallback.
/// </summary>
public sealed class ChargeGuard : BackgroundService
{
    private readonly MifsClient _mifs;
    private readonly StateStore _store;
    private readonly ILogger<ChargeGuard> _log;

    private readonly AutoResetEvent _wake = new(false);
    private bool _lastAc;
    private int _ticks;

    public ChargeGuard(MifsClient mifs, StateStore store, ILogger<ChargeGuard> log)
    {
        _mifs = mifs;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        StartPowerEventPump();

        await Task.Delay(1500, ct);

        var st = _store.State;
        if (st.ChargeProtectEnabled)
        {
            bool ok = _mifs.SetChargeLevel(st.ChargeLevel);
            _log.LogInformation("initial charge arm {Level} -> {Ok}", st.ChargeLevel, ok);
        }

        while (!ct.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { _log.LogWarning(ex, "charge guard tick failed"); }

            // Wake on power events; fall back to a five minute safety check.
            if (WaitHandle.WaitAny(new[] { _wake, ct.WaitHandle }, TimeSpan.FromMinutes(5)) == 1) break;
        }
    }

    private PowerNotifier? _notifier;

    private void StartPowerEventPump()
    {
        _notifier = new PowerNotifier(() => _wake.Set());
        _notifier.Start();
    }

    private void Tick()
    {
        TimingLog.Log("event/tick");
        IpcServer.WmiBusy = true;
        try { TickCore(); }
        finally
        {
            IpcServer.WmiBusy = false;
            IpcServer.PushFreshStatus();
        }
    }

    private void TickCore()
    {
        var ps = IpcServer.PowerStatus();
        bool ac = ps.ACLineStatus == 1;
        bool acChanged = ac != _lastAc;
        _lastAc = ac;
        _ticks++;

        var st = _store.State;

        if (acChanged)
        {
            IpcServer.InvalidateStatus();
            PushFastStatus(ac, ps, st, (PerfMode)st.PerfMode);   // step 1: power state first, no WMI
        }

        if (st.ChargeProtectEnabled)
        {
            if (acChanged)
            {
                bool ok = _mifs.ArmChargeLevel(st.ChargeLevel);   // fast path: no readback
                _log.LogInformation("re-arm charge after power change -> {Ok}", ok);
            }
            else if (_ticks % 60 == 0)
            {
                var current = _mifs.GetChargeLevel();
                if (current is not null && current.Value != st.ChargeLevel)
                {
                    bool ok = _mifs.ArmChargeLevel(st.ChargeLevel);
                    _log.LogInformation("charge drift {Cur} != {Want} -> {Ok}", current, st.ChargeLevel, ok);
                }
            }
        }

        if (st.AutoFullSpeedOnAc && acChanged)
        {
            var current = _mifs.GetPerfMode();
            if (ac && current != PerfMode.FullSpeed)
            {
                _store.Update(s => s.PerfMode = PerfMode.FullSpeed);
                bool ok = _mifs.SetPerfMode(PerfMode.FullSpeed);
                _log.LogInformation("AC connected -> FullSpeed {Ok}", ok);
                PushFastStatus(ac, ps, st, PerfMode.FullSpeed);
                IpcServer.InvalidateStatus();
                IpcServer.BroadcastStatus();
            }
            else if (!ac && current == PerfMode.FullSpeed)
            {
                _store.Update(s => s.PerfMode = PerfMode.Smart);
                bool ok = _mifs.SetPerfMode(PerfMode.Smart);
                _log.LogInformation("AC disconnected -> Smart {Ok}", ok);
                PushFastStatus(ac, ps, st, PerfMode.Smart);
                IpcServer.InvalidateStatus();
                IpcServer.BroadcastStatus();
            }
        }
    }

    // Push immediately using values we already have: no WMI round trip.
    private static void PushFastStatus(bool ac, IpcServer.SYSTEM_POWER_STATUS ps, AppState st, PerfMode mode)
    {
        var payload = new JsonObject
        {
            ["ok"] = true,
            ["ac"] = ac,
            ["charging"] = (ps.BatteryFlag & 0x08) != 0,
            ["battery"] = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : -1,
            ["perf"] = (int)mode,
            ["chargeCode"] = (int)st.ChargeLevel,
            ["chargePct"] = st.ChargeLevel.ToPercent(),
            ["health"] = -1,
            ["watts"] = -1,
            ["autoFull"] = st.AutoFullSpeedOnAc,
            ["chargeEnabled"] = st.ChargeProtectEnabled
        };
        IpcServer.BroadcastStatusPayload(payload);
    }

    public override void Dispose()
    {
        try { _notifier?.Dispose(); } catch { }
        try { _wake.Dispose(); } catch { }
        base.Dispose();
    }
}











