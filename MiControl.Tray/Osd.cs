using System.Runtime.InteropServices;

namespace MiControl.Tray;

/// <summary>
/// Short-lived overlay in the middle of the screen, used to confirm a resolution change.
/// Click-through, never takes focus, closes itself after 1.5 seconds.
/// </summary>
internal static class Osd
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private sealed class OsdForm : Form
    {
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }

    public static void Show(string text)
    {
        var font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
        var textSize = TextRenderer.MeasureText(text, font);

        const int padX = 32;
        const int padY = 18;

        var form = new OsdForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            Opacity = 0.72,
            AutoScaleMode = AutoScaleMode.None,
            // Box hugs the text with equal padding above and below.
            Size = new Size(textSize.Width + padX * 2, textSize.Height + padY * 2)
        };

        var label = new Label
        {
            Text = text,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = font,
            Padding = new Padding(0)
        };
        form.Controls.Add(label);

        var area = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        form.Location = new Point(
            area.X + (area.Width - form.Width) / 2,
            area.Y + (int)(area.Height * 0.58) - form.Height / 2);

        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            form.Close();
        };
        form.Shown += (_, _) => timer.Start();

        form.Show();
    }
}
