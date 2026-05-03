using System.Drawing;
using System.Windows.Forms;

namespace MultiMonitorSleepController;

internal sealed class BrightnessTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly BrightnessFlyoutForm _flyout;
    private readonly Action<int> _onBrightnessChanged;
    private readonly Action _onShowMainWindow;
    private bool _disposed;

    public BrightnessTrayIcon(Action<int> onBrightnessChanged, Action onShowMainWindow)
    {
        _onBrightnessChanged = onBrightnessChanged;
        _onShowMainWindow = onShowMainWindow;

        _flyout = new BrightnessFlyoutForm();
        _flyout.BrightnessChanged += (_, value) => _onBrightnessChanged(value);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Main Window", null, (_, _) => _onShowMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => Application.Exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateSunIcon(),
            Text = "Brightness Control",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    public void UpdateBrightness(int value)
    {
        _flyout.BrightnessValue = value;
        _notifyIcon.Text = $"Brightness: {value}%";
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_flyout.Visible)
        {
            _flyout.Hide();
            return;
        }

        _flyout.ShowAtTrayLocation();
    }

    private static Icon CreateSunIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Sun body
        using var sunBrush = new SolidBrush(Color.FromArgb(255, 200, 60));
        g.FillEllipse(sunBrush, 10, 10, 12, 12);

        // Rays
        using var rayPen = new Pen(Color.FromArgb(255, 200, 60), 2f);
        var cx = 16f;
        var cy = 16f;
        for (var i = 0; i < 8; i++)
        {
            var angle = i * 45.0 * Math.PI / 180.0;
            var x1 = cx + (float)(9 * Math.Cos(angle));
            var y1 = cy + (float)(9 * Math.Sin(angle));
            var x2 = cx + (float)(13 * Math.Cos(angle));
            var y2 = cy + (float)(13 * Math.Sin(angle));
            g.DrawLine(rayPen, x1, y1, x2, y2);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _flyout.Dispose();
    }
}
