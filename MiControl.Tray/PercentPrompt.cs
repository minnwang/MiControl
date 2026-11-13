using System.Globalization;
using System.Windows.Forms;

namespace MiControl.Tray;

/// <summary>Minimal numeric prompt used by the "custom" charge level entry.</summary>
public sealed class PercentPrompt : Form
{
    private readonly NumericUpDown _value = new()
    {
        Minimum = 40,
        Maximum = 100,
        Increment = 5,
        Value = 80,
        Dock = DockStyle.Top,
        TextAlign = HorizontalAlignment.Center
    };

    public int Result => (int)_value.Value;

    private PercentPrompt()
    {
        Text = "自定义充电上限";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(260, 110);
        Font = new Font("Microsoft YaHei UI", 9F);

        var label = new Label
        {
            Text = "输入目标电量（40–100），将自动对齐到固件支持的档位：",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 8, 8, 0)
        };

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        panel.Controls.Add(ok);
        panel.Controls.Add(cancel);

        Controls.Add(panel);
        Controls.Add(_value);
        Controls.Add(label);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static int? Ask()
    {
        using var f = new PercentPrompt();
        return f.ShowDialog() == DialogResult.OK ? f.Result : null;
    }
}
