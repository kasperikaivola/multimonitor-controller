using System.Drawing;
using System.Windows.Forms;

namespace MultiMonitorSleepController;

internal sealed class BlackoutOverlayForm : Form
{
    public BlackoutOverlayForm(Rectangle bounds, double opacity = 1.0)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = opacity;
        KeyPreview = true;

        // Avoid grabbing focus from active windows while still covering the screen region.
        Enabled = false;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }
}
