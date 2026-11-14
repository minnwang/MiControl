using System.Drawing;
using System.Windows.Forms;

namespace MiControl.Tray;

/// <summary>Settings window: battery/performance status plus vendor hotkey mapping.</summary>
public sealed class SettingsForm : Form
{
    private readonly IpcClient _ipc;

    private readonly Label _battery = new() { AutoSize = true };
    private readonly Label _perf = new() { AutoSize = true };
    private readonly Label _charge = new() { AutoSize = true };
    private readonly Label _health = new() { AutoSize = true };
    private readonly CheckBox _autoFull = new() { Text = "插电时自动切换到全速模式", AutoSize = true };


    private const int LabelX = 18;
    private const int ComboX = 128;
    private const int ComboW = 250;
    private const int ParamX = 388;
    private const int ParamW = 260;

    private readonly int _rowH;
    private readonly int _ctlH;
    private readonly int _rowTop = 250;

    // code, display name, default action, human readable default
    private static readonly (string Code, string Name, string Default, string DefaultText)[] Keys =
    {
        ("33", "Fn + F4", "mic",  "麦克风静音"),
        ("35", "Fn + F7", "none", "无"),
        ("1",  "Fn + F8", "winp", "投影 (Win+P)"),
        ("27", "Fn + F9", "ms:",  "Windows 设置"),
        ("37", "自定义键", "none", "无")
    };

    private readonly Dictionary<string, ComboBox> _combos = new();
    private readonly Dictionary<string, Label> _params = new();
    private readonly Dictionary<string, string> _current = new();
    private bool _loading;

    public SettingsForm(IpcClient ipc)
    {
        _ipc = ipc;

        AutoScaleMode = AutoScaleMode.None;
        Text = "MiControl 设置";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        _ctlH = Math.Max(26, Font.Height + 8);
        _rowH = _ctlH + 18;

        ClientSize = new Size(680, _rowTop + Keys.Length * _rowH + 70);

        Controls.Add(new Label
        {
            Text = "电池与性能",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            Location = new Point(16, 12),
            AutoSize = true
        });

        _battery.Location = new Point(18, 44);
        _perf.Location = new Point(18, 68);
        _charge.Location = new Point(18, 92);
        _health.Location = new Point(18, 116);
        _autoFull.Location = new Point(18, 146);
        Controls.AddRange(new Control[] { _battery, _perf, _charge, _health, _autoFull });

        Controls.Add(new Label
        {
            Text = "快捷键",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            Location = new Point(16, 210),
            AutoSize = true
        });

        for (int i = 0; i < Keys.Length; i++)
        {
            var (code, name, _, defaultText) = Keys[i];
            int top = _rowTop + i * _rowH;

            var nameLabel = new Label
            {
                Text = name,
                Location = new Point(LabelX, top),
                Size = new Size(104, _ctlH),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(ComboX, top),
                Size = new Size(ComboW, _ctlH)
            };
            combo.Items.Add($"默认：{defaultText}");
            combo.Items.Add("启动程序…");
            combo.Items.Add("模拟按键…");
            combo.Items.Add("切换分辨率…");
            combo.Tag = code;
            combo.SelectedIndexChanged += (_, _) => OnActionChanged(code, combo);

            var paramLabel = new Label
            {
                Text = "",
                Location = new Point(ParamX, top),
                Size = new Size(ParamW, _ctlH),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _combos[code] = combo;
            _params[code] = paramLabel;

            Controls.Add(nameLabel);
            Controls.Add(combo);
            Controls.Add(paramLabel);
        }

        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 32),
            Location = new Point(ClientSize.Width - 108, ClientSize.Height - 48)
        };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        AcceptButton = close;

        _autoFull.Click += (_, _) =>
        {
            try { _ipc.SetAutoFull(_autoFull.Checked); } catch { }
        };


        Load += (_, _) => Reload();
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => RefreshData();
        timer.Start();
        FormClosed += (_, _) => timer.Stop();
    }

    private static (string Code, string Name, string Default, string DefaultText) KeyOf(string code)
        => Keys.First(k => k.Code == code);

    private void Reload()
    {
        _loading = true;
        try
        {
            var map = _ipc.GetHotkeys();
            foreach (var k in Keys)
            {
                var action = map.TryGetValue(k.Code, out var v) ? v : k.Default;
                _current[k.Code] = action;
                _combos[k.Code].SelectedIndex = IndexFor(action);
                _params[k.Code].Text = DescribeParam(action);
            }
        }
        catch
        {
            _battery.Text = "无法连接 MiControl 服务";
        }
        finally
        {
            _loading = false;
        }

        RefreshData();
    }

    private static int IndexFor(string action)
    {
        if (action.StartsWith("app:", StringComparison.Ordinal)) return 1;
        if (action.StartsWith("keys:", StringComparison.Ordinal)) return 2;
        if (action.StartsWith("res:", StringComparison.Ordinal)) return 3;
        return 0;
    }

    private static string DescribeParam(string action)
    {
        if (action.StartsWith("app:", StringComparison.Ordinal))
            return Path.GetFileName(action.Substring(4).Split('|')[0]);
        if (action.StartsWith("keys:", StringComparison.Ordinal))
            return KeyNames.Describe(action.Substring(5));
        if (action.StartsWith("res:", StringComparison.Ordinal))
            return action.Substring(4).Replace("|", "⇄");
        return "";
    }

    private void OnActionChanged(string code, ComboBox combo)
    {
        if (_loading) return;

        switch (combo.SelectedIndex)
        {
            case 0:
                Commit(code, KeyOf(code).Default);
                break;

            case 1:
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择要启动的程序",
                    Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) { Revert(code); return; }
                Commit(code, "app:" + dlg.FileName);
                break;
            }

            case 2:
            {
                var spec = KeyCaptureForm.CaptureSpec(this);
                if (spec is null) { Revert(code); return; }
                Commit(code, "keys:" + spec);
                break;
            }

            case 3:
            {
                using var dlg = new ResolutionForm(_current[code]);
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Spec.Length == 0)
                {
                    Revert(code);
                    return;
                }
                Commit(code, dlg.Spec);
                break;
            }
        }
    }

    private void Revert(string code)
    {
        _loading = true;
        _combos[code].SelectedIndex = IndexFor(_current[code]);
        _loading = false;
    }

    private void Commit(string code, string action)
    {
        try
        {
            if (_ipc.SetHotkey(code, action))
            {
                _current[code] = action;
                _params[code].Text = DescribeParam(action);
                return;
            }
        }
        catch { }

        Revert(code);
    }

    private void RefreshData()
    {
        try
        {
            var st = _ipc.GetStatus();
            _battery.Text = $"电量：{st.Battery}%　{(st.Ac ? "已接电源" : "电池供电")}" +
                            (st.Charging ? "　充电中" : (st.Ac ? "　已限充" : ""));
            _perf.Text = $"运行模式：{PerfName(st.Perf)}";
            _charge.Text = $"充电上限：{st.ChargePct}%";
            _health.Text = $"电池健康：{(st.Health >= 0 ? st.Health + "%" : "—")}　适配器：{(st.Watts > 0 ? st.Watts + " W" : "—")}";
            _autoFull.Checked = st.AutoFull;
        }
        catch
        {
            _battery.Text = "无法连接 MiControl 服务";
        }
    }

    private static string PerfName(int mode) => mode switch
    {
        0x02 => "静音",
        0x09 => "智能",
        0x03 => "性能",
        0x04 => "全速",
        0x0A => "ECO",
        _ => $"未知 (0x{mode:X2})"
    };
}








