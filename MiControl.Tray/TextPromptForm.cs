using System.Drawing;
using System.Windows.Forms;

namespace MiControl.Tray;

/// <summary>Small single-line text prompt.</summary>
public sealed class TextPromptForm : Form
{
    private readonly TextBox _box = new() { Dock = DockStyle.Top };

    private TextPromptForm(string title, string label, string initial)
    {
        Text = title;
        ClientSize = new Size(420, 130);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var lbl = new Label { Text = label, Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 8, 8, 0) };
        _box.Text = initial;

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 96 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 96 };
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        panel.Controls.Add(ok);
        panel.Controls.Add(cancel);

        Controls.Add(panel);
        Controls.Add(_box);
        Controls.Add(lbl);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Ask(IWin32Window owner, string title, string label, string initial = "")
    {
        using var f = new TextPromptForm(title, label, initial);
        return f.ShowDialog(owner) == DialogResult.OK ? f._box.Text.Trim() : null;
    }
}
