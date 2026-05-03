using System.Drawing;
using System.Windows.Forms;

namespace MultiMonitorSleepController;

public sealed class OverlayControllerService : IDisposable
{
    private readonly Dictionary<string, BlackoutOverlayForm> _overlays = new(StringComparer.OrdinalIgnoreCase);

    public void SetOverlay(string monitorId, Rectangle bounds, bool show, double opacity = 1.0)
    {
        if (show)
        {
            EnsureOverlay(monitorId, bounds, opacity);
            return;
        }

        RemoveOverlay(monitorId);
    }

    public void RemoveOverlay(string monitorId)
    {
        if (!_overlays.TryGetValue(monitorId, out var overlay))
        {
            return;
        }

        _overlays.Remove(monitorId);

        if (!overlay.IsDisposed)
        {
            overlay.Hide();
            overlay.Dispose();
        }
    }

    public void RemoveOverlaysNotInSet(ISet<string> activeMonitorIds)
    {
        var staleIds = _overlays.Keys.Where(id => !activeMonitorIds.Contains(id)).ToList();

        foreach (var staleId in staleIds)
        {
            RemoveOverlay(staleId);
        }
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values.ToList())
        {
            if (!overlay.IsDisposed)
            {
                overlay.Hide();
                overlay.Dispose();
            }
        }

        _overlays.Clear();
    }

    private void EnsureOverlay(string monitorId, Rectangle bounds, double opacity)
    {
        if (_overlays.TryGetValue(monitorId, out var existing))
        {
            if (!existing.IsDisposed)
            {
                if (existing.Bounds != bounds)
                {
                    existing.Bounds = bounds;
                }

                if (Math.Abs(existing.Opacity - opacity) > 0.001)
                {
                    existing.Opacity = opacity;
                }

                if (!existing.Visible)
                {
                    existing.Show();
                }

                return;
            }

            _overlays.Remove(monitorId);
        }

        var overlay = new BlackoutOverlayForm(bounds, opacity);
        _overlays[monitorId] = overlay;
        overlay.Show();
    }
}
