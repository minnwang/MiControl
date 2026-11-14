namespace MiControl.Tray;

/// <summary>Lets the user pick the two resolutions a hotkey switches between.</summary>
internal sealed class ResolutionForm : Form
{
    private readonly ComboBox _first = new();
    private readonly ComboBox _second = new();

    public string Spec { get; private set; } = "";

    public ResolutionForm(string? current)
    {
        Text = "切换分辨率";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 170);
        Font = new Font("Microsoft YaHei UI", 9F);

        var modes = ResolutionSwitcher.Enumerate();
        var names = modes.Select(m => $"{m.W}x{m.H}").ToList();

        var label1 = new Label { Text = "分辨率 1：", Location = new Point(16, 20), Size = new Size(90, 24), TextAlign = ContentAlignment.MiddleLeft };
        var label2 = new Label { Text = "分辨率 2：", Location = new Point(16, 60), Size = new Size(90, 24), TextAlign = ContentAlignment.MiddleLeft };

        _first.Location = new Point(110, 18);
        _first.Size = new Size(220, 24);
        _first.DropDownStyle = ComboBoxStyle.DropDownList;
        _first.Items.AddRange(names.ToArray());

        _second.Location = new Point(110, 58);
        _second.Size = new Size(220, 24);
        _second.DropDownStyle = ComboBoxStyle.DropDownList;
        _second.Items.AddRange(names.ToArray());

        var currentMode = $"{ResolutionSwitcher.Current().W}x{ResolutionSwitcher.Current().H}";

        // Preselect from the existing spec when possible, otherwise current + next smaller mode.
        var existing = (current ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (existing.Length == 2 && names.Contains(existing[0]) && names.Contains(existing[1]))
        {
            _first.SelectedItem = existing[0];
            _second.SelectedItem = existing[1];
        }
        else
        {
            _first.SelectedItem = names.Contains(currentMode) ? currentMode : (names.Count > 0 ? names[0] : null);
            var fallback = names.FirstOrDefault(n => n != _first.SelectedItem as string) ?? _first.SelectedItem as string;
            _second.SelectedItem = fallback;
        }

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(160, 120), Size = new Size(84, 30) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(252, 120), Size = new Size(84, 30) };

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { label1, label2, _first, _second, ok, cancel });

        ok.Click += (_, _) =>
        {
            if (_first.SelectedItem is string a && _second.SelectedItem is string b && a != b)
                Spec = $"res:{a}|{b}";
        };
    }
}
