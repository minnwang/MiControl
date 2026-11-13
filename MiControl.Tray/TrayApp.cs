using System.Drawing;
using System.Windows.Forms;

namespace MiControl.Tray;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;

    private static readonly Font HeaderFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
    private readonly ToolStripMenuItem _header = new() { Enabled = false, Font = HeaderFont };
    private readonly ToolStripMenuItem _header2 = new() { Enabled = false, Font = HeaderFont };
    private readonly ToolStripMenuItem _perfRoot = new("运行模式");
    private readonly ToolStripMenuItem _chargeRoot = new("充电上限");
    private readonly ToolStripMenuItem _info = new() { Enabled = false };
    private readonly ToolStripMenuItem _autoFull = new("插电自动切全速");
    private readonly ToolStripMenuItem _exit = new("退出");

    private readonly Dictionary<int, ToolStripMenuItem> _perfItems = new();
    private readonly Dictionary<int, ToolStripMenuItem> _chargeItems = new();

    private readonly EventWaitHandle _activateSignal = new(false, EventResetMode.AutoReset, @"Global\MiControlTrayActivate");
    private SynchronizationContext? _uiContext;

    private IpcClient? _ipc;
    private Status? _status;
    private SettingsForm? _settings;
    private bool _busy;
    private bool _refreshPending;

    private static readonly (int Value, string Label)[] PerfModes =
    {
        (0x02, "静音"),
        (0x09, "智能"),
        (0x03, "性能"),
        (0x04, "全速")
    };

    private static readonly int[] ChargePcts = { 100, 90, 80, 70, 60, 50, 40 };

    public TrayApp()
    {
        foreach (var (value, label) in PerfModes)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => ApplyPerf(value);
            _perfItems[value] = item;
            _perfRoot.DropDownItems.Add(item);
        }

        foreach (var pct in ChargePcts)
        {
            var item = new ToolStripMenuItem($"{pct}%");
            item.Click += (_, _) => ApplyCharge(pct);
            _chargeItems[pct] = item;
            _chargeRoot.DropDownItems.Add(item);
        }


        _autoFull.Click += (_, _) => ToggleAutoFull();
        _exit.Click += (_, _) => ExitApp();

        Padding rowPad = Padding.Empty;
        var settingsItem = new ToolStripMenuItem("设置…");
        settingsItem.Padding = rowPad;
        settingsItem.Click += (_, _) => ShowSettings();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _header,
            _header2,
            new ToolStripSeparator(),
            _perfRoot,
            _chargeRoot,
            new ToolStripSeparator(),
            settingsItem,
            new ToolStripSeparator(),
            _exit
        });

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "MiControl",
            ContextMenuStrip = _menu
        };

        _menu.Opening += (_, _) =>
        {
            if (_status is not null) Render(_status);   // instant, from cache
            RefreshAsync();
        };
        _header2.Padding = rowPad;
        _perfRoot.Padding = rowPad;
        _chargeRoot.Padding = rowPad;
        _exit.Padding = rowPad;

        _timer = new System.Windows.Forms.Timer { Interval = 300000 };
        _timer.Tick += (_, _) => RefreshAsync();
        _timer.Start();

        _uiContext = SynchronizationContext.Current;

        ThreadPool.RegisterWaitForSingleObject(_activateSignal,
            (_, _) => _uiContext?.Post(_ => ShowSettings(), null), null, Timeout.Infinite, false);

        RefreshAsync();
        ApplyAutostart();
    }

    private void ApplyAutostart()
    {
        Autostart.Enable();
    }

    private bool EnsureClient()
    {
        if (_ipc is not null) return true;
        try
        {
            _ipc = new IpcClient();
            _ipc.ActionsPushed += acts => _uiContext?.Post(_ => HandleActions(acts), null);
            _ipc.StatusPushed += obj => _uiContext?.Post(_ =>
            {
                try
                {
                    TrayLog("recv");
                    var st = IpcClient.Parse(obj);
                    _status = st;
                    Render(st);
                    TrayLog("render ac=" + st.Ac + " perf=" + st.Perf);
                }
                catch { }
            }, null);
            return true;
        }
        catch
        {
            _ipc = null;
            return false;
        }
    }

    private void RefreshAsync()
    {
        if (_busy) { _refreshPending = true; return; }   // coalesce instead of dropping
        _busy = true;
        Task.Run(() =>
        {
            try
            {
                if (!EnsureClient()) { _uiContext?.Post(_ => SetHeader("服务未运行"), null); return; }
                var st = _ipc!.GetStatus();
                _uiContext?.Post(_ => { _status = st; Render(st); }, null);
            }
            catch { _ipc = null; }
            finally
            {
                _busy = false;
                if (_refreshPending) { _refreshPending = false; RefreshAsync(); }
            }
        });
    }

    private void Render(Status st)
    {
        // Updates must also land while the drop-down is open, otherwise the user keeps
        // seeing the values captured when it opened.

        if (!EnsureClient())
        {
            SetHeader("服务未运行");
            return;
        }

        {
            // Line 1: battery level + power state. Line 2:运行模式 + charge limit.
            var powerState = st.Ac
                ? (st.Watts > 0 ? $"{st.Watts} W" : "已接电源") + (st.Charging ? " 充电中" : " 未充电")
                : "电池供电";

            var perfName = PerfModes.FirstOrDefault(p => p.Value == st.Perf).Label ?? $"未知(0x{st.Perf:X2})";

            _header.Text = st.Battery >= 0 ? $"电量：{st.Battery}%  ·  {powerState}" : powerState;
            _header2.Text = $"运行：{perfName}  ·  充电上限：{st.ChargePct}%";

            foreach (var (value, item) in _perfItems)
                item.Checked = value == st.Perf;

            foreach (var (pct, item) in _chargeItems)
                item.Checked = pct == st.ChargePct;

            _autoFull.Checked = st.AutoFull;

            _icon.Text = Truncate($"MiControl · {st.Battery}% · {powerState}", 63);
            if (_perfItems.TryGetValue(0x04, out var fsItem)) fsItem.Enabled = st.Ac;
        }
    }

    private void SetHeader(string text)
    {
        _header.Text = text;
        _header2.Text = "";
        _info.Text = "";
        _icon.Text = Truncate("MiControl · " + text, 63);
    }

    private async void ApplyPerf(int mode)
    {
        if (_busy || !EnsureClient()) return;
        _busy = true;
        try
        {
            var (ok, msg) = await Task.Run(() => _ipc!.SetPerf(mode));
            if (!ok) ShowError($"切换失败：{msg}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
        RefreshAsync();
    }

    private async void ApplyCharge(int pct)
    {
        if (_busy || !EnsureClient()) return;
        _busy = true;
        try
        {
            var (ok, msg) = await Task.Run(() => _ipc!.SetCharge(pct));
            if (!ok) ShowError($"设置失败：{msg}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
        RefreshAsync();
    }

    private async void ApplyCustomCharge()
    {
        if (_busy || !EnsureClient()) return;
        var pct = PercentPrompt.Ask();
        if (pct is null) return;

        _busy = true;
        try
        {
            var (ok, msg) = await Task.Run(() => _ipc!.SetCharge(pct.Value));
            if (!ok) ShowError($"设置失败：{msg}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
        RefreshAsync();
    }

    private async void ToggleAutoFull()
    {
        if (_busy || !EnsureClient()) return;
        _busy = true;
        try
        {
            var target = !(_status?.AutoFull ?? false);
            var (ok, msg) = await Task.Run(() => _ipc!.SetAutoFull(target));
            if (!ok) ShowError($"设置失败：{msg}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
        RefreshAsync();
    }

    private void ShowError(string message)
    {
        _icon.ShowBalloonTip(4000, "MiControl", message, ToolTipIcon.Warning);
    }

    internal static void TrayLog(string m)
    {
        if (true) return;   // diagnostics disabled
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "micontrol-tray.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  [tray] {m}{Environment.NewLine}", System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(TrayApp).Assembly
                .GetManifestResourceStream("MiControl.Tray.app.ico");
            if (stream is not null)
                return new Icon(stream, new Size(32, 32));
        }
        catch { }

        try
        {
            var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exeIcon is not null) return exeIcon;
        }
        catch { }

        return SystemIcons.Application;
    }

    private void HandleActions(string[] actions)
    {
        try
        {
            foreach (var action in actions)
            {
                switch (action)
                {
                    case "settings":
                    case "act:settings":
                        ShowSettings();
                        break;

                    case "winp":
                    case "act:winp":
                        HotkeySender.SendWinP();
                        break;

                    case "mic:on":
                    case "mic:off":
                    {
                        // The keyboard LED already reflects the state; stay silent unless it fails.
                        bool want = action == "mic:on";
                        if (MicControl.SetMute(want) is null)
                            _icon.ShowBalloonTip(2000, "MiControl", "麦克风切换失败", ToolTipIcon.Warning);
                        break;
                    }

                    default:
                        if (action.StartsWith("act:", StringComparison.Ordinal))
                            ActionRunner.Run(action.Substring(4));
                        break;
                }
            }
        }
        catch
        {
            _ipc = null;
        }
    }

    private void ShowSettings()
    {
        if (!EnsureClient())
        {
            ShowError("服务未运行");
            return;
        }

        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsForm(_ipc!);
        _settings.Show();
        _settings.Activate();
    }

    private void ExitApp()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        ExitThread();
    }
}



































