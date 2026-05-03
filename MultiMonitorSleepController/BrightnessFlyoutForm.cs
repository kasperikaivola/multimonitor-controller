using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MultiMonitorSleepController;

internal sealed class BrightnessFlyoutForm : Form
{
    private readonly TrackBar _slider;
    private readonly Label _valueLabel;
    private readonly Label _titleLabel;

    public event EventHandler<int>? BrightnessChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int BrightnessValue
    {
        get => _slider.Value;
        set
        {
            if (value >= _slider.Minimum && value <= _slider.Maximum)
            {
                _slider.Value = value;
                _valueLabel.Text = $"{value}%";
            }
        }
    }

    public BrightnessFlyoutForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(320, 100);
        BackColor = Color.FromArgb(43, 43, 43);
        ForeColor = Color.White;
        Padding = new Padding(16, 12, 16, 12);
        DoubleBuffered = true;

        _titleLabel = new Label
        {
            Text = "☀  Global Brightness",
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(230, 230, 230),
            AutoSize = true,
            Location = new Point(16, 12),
        };

        var sunLabel = new Label
        {
            Text = "☀",
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            ForeColor = Color.FromArgb(255, 200, 60),
            AutoSize = true,
            Location = new Point(14, 42),
        };

        _slider = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            TickStyle = TickStyle.None,
            Location = new Point(44, 44),
            Size = new Size(210, 30),
            BackColor = Color.FromArgb(43, 43, 43),
        };

        _valueLabel = new Label
        {
            Text = "50%",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
            Location = new Point(262, 48),
        };

        _slider.ValueChanged += (_, _) =>
        {
            _valueLabel.Text = $"{_slider.Value}%";
        };

        _slider.MouseCaptureChanged += (_, _) =>
        {
            BrightnessChanged?.Invoke(this, _slider.Value);
        };

        _slider.KeyUp += (_, _) =>
        {
            BrightnessChanged?.Invoke(this, _slider.Value);
        };

        Controls.Add(_titleLabel);
        Controls.Add(sunLabel);
        Controls.Add(_slider);
        Controls.Add(_valueLabel);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw rounded border
        using var pen = new Pen(Color.FromArgb(70, 70, 70), 1);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRectangle(rect, 8);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        var rect = new Rectangle(0, 0, Width, Height);
        using var path = RoundedRectangle(rect, 8);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;
            const int WsExTopMost = 0x00000008;

            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTopMost;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => false;

    public void ShowAtTrayLocation()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);

        // Position near bottom-right, just above the taskbar
        var x = workingArea.Right - Width - 12;
        var y = workingArea.Bottom - Height - 12;

        Location = new Point(x, y);
        Show();
        Activate();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();

        // Top left
        path.AddArc(arc, 180, 90);

        // Top right
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        // Bottom right
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        // Bottom left
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
