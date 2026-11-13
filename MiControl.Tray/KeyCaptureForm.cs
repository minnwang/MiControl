using System.Drawing;
using System.Windows.Forms;

namespace MiControl.Tray;

/// <summary>Captures a key combination so the user can map a vendor key to it.</summary>
public sealed class KeyCaptureForm : Form
{
    private readonly Label _hint = new()
    {
        Text = "请按下要模拟的键（可按住 Ctrl / Alt / Shift / Win）",
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        Font = new Font("Microsoft YaHei UI", 10F)
    };

    private byte _mainVk;
    private readonly List<byte> _modifiers = new();

    public string Spec { get; private set; } = "";

    private KeyCaptureForm()
    {
        Text = "录制按键";
        ClientSize = new Size(360, 130);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 100 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 100 };
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        panel.Controls.Add(ok);
        panel.Controls.Add(cancel);

        Controls.Add(_hint);
        Controls.Add(panel);

        AcceptButton = ok;
        CancelButton = cancel;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; return; }

            _mainVk = (byte)e.KeyValue;
            _modifiers.Clear();
            if (e.Control) _modifiers.Add(0x11);
            if (e.Shift) _modifiers.Add(0x10);
            if (e.Alt) _modifiers.Add(0x12);

            var names = new List<string>();
            if (e.Control) names.Add("Ctrl");
            if (e.Shift) names.Add("Shift");
            if (e.Alt) names.Add("Alt");
            names.Add(e.KeyCode.ToString());

            _hint.Text = "已录制：" + string.Join(" + ", names) + "\n按「确定」保存";
            Spec = "0x" + _mainVk.ToString("X2");
            foreach (var m in _modifiers) Spec += "+0x" + m.ToString("X2");
        };
    }

    public static string? CaptureSpec(IWin32Window owner)
    {
        using var f = new KeyCaptureForm();
        return f.ShowDialog(owner) == DialogResult.OK && f.Spec.Length > 0 ? f.Spec : null;
    }
}
