using System.Threading;

namespace MiControl.Service;

/// <summary>
/// Applies the persisted settings to the EC firmware.
///
/// The configuration file is the single source of truth: whatever ChargeLevel and
/// PerfMode it holds is written to the EC. The EC forgets those values whenever it
/// re-initialises (boot, wake from sleep, some power transitions), so the values are
/// re-applied on every such event instead of relying on a polling fallback.
///
/// Triggers:
///   - service start
///   - AC plug/unplug  (policy may update PerfMode first, then apply)
///   - resume from sleep/hibernate
/// </summary>
public sealed class ChargeGuard : BackgroundService
{
    private readonly MifsClient _mifs;
    private readonly StateStore _store;
    private readonly ILogger<ChargeGuard> _log;

    private readonly AutoResetEvent _wake = new(false);
    private PowerNotifier? _notifier;
    private volatile int _pending = (int)PowerEventKind.Resume;

    public ChargeGuard(MifsClient mifs, StateStore store, ILogger<ChargeGuard> log)
    {
        _mifs = mifs;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _notifier = new PowerNotifier(OnPowerEvent);
        _notifier.Start();

        await Task.Delay(1500, ct);

        // Startup: firmware state is unknown, push the configured values.
        RunSafely("startup", applyPolicy: false);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (WaitHandle.WaitAny(new[] { _wake, ct.WaitHandle }) == 1) break;

                var kind = (PowerEventKind)_pending;
                if (kind == PowerEventKind.AcChange)
                    RunSafely("power change", applyPolicy: true);
                else
                    RunSafely("resume", applyPolicy: false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "power event handling failed");
            }
        }
    }

    private void OnPowerEvent(PowerEventKind kind)
    {
        _pending = (int)kind;
        _wake.Set();
    }

    private void RunSafely(string reason, bool applyPolicy)
    {
        IpcServer.WmiBusy = true;
        try
        {
            if (applyPolicy) ApplyPowerPolicy();
            ApplySettings();
            _log.LogInformation("{Reason}: applied charge={Charge} perf={Perf}", reason,
                _store.State.ChargeLevel, _store.State.PerfMode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Reason}: apply failed", reason);
        }
        finally
        {
            IpcServer.WmiBusy = false;
            IpcServer.PushFreshStatus();
        }
    }

    /// <summary>Updates the stored mode when "auto full speed on AC" is enabled.</summary>
    private void ApplyPowerPolicy()
    {
        var st = _store.State;
        if (!st.AutoFullSpeedOnAc) return;

        bool ac = IpcServer.PowerStatus().ACLineStatus == 1;
        var target = ac ? PerfMode.FullSpeed : PerfMode.Smart;
        if (st.PerfMode != target)
            _store.Update(s => s.PerfMode = target);
    }

    /// <summary>Writes the configured charge level and performance mode to the firmware.
    /// Both values are read back; a rejected write is retried once and then logged.</summary>
    private void ApplySettings()
    {
        var st = _store.State;

        bool chargeOk = ApplyWithRetry(() => _mifs.SetChargeLevel(st.ChargeLevel));
        bool perfOk = ApplyWithRetry(() => _mifs.SetPerfMode(st.PerfMode));

        if (chargeOk && perfOk)
        {
            _log.LogInformation("apply charge={Charge} perf={Perf} verified", st.ChargeLevel, st.PerfMode);
        }
        else
        {
            _log.LogWarning("apply charge={Charge}({ChargeOk}) perf={Perf}({PerfOk}) not verified",
                st.ChargeLevel, chargeOk, st.PerfMode, perfOk);
        }
    }

    private static bool ApplyWithRetry(Func<bool> write)
    {
        if (write()) return true;
        Thread.Sleep(300);
        return write();
    }

    public override void Dispose()
    {
        try { _notifier?.Dispose(); } catch { }
        try { _wake.Dispose(); } catch { }
        base.Dispose();
    }
}
