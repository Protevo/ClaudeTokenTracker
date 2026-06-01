using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeTokenTracker.UI;

/// <summary>
/// A slim, fully-rounded usage meter. The track is a warm neutral; the fill is
/// coloured by the semantic ramp (green → ochre → clay → brick). The numeric
/// percentage is rendered by the owning row, so the bar stays clean.
/// </summary>
public sealed class UsageBar : Control
{
    private double _value;

    public UsageBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }

    /// <summary>Utilization fraction (0.0 - 1.0+).</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Max(0, value);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Canvas);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        int radius = bounds.Height / 2;

        using (GraphicsPath track = Theme.RoundedRect(bounds, radius))
        using (var trackBrush = new SolidBrush(Theme.Track))
            g.FillPath(trackBrush, track);

        double fraction = Math.Clamp(_value, 0, 1);
        int fillWidth = (int)Math.Round(bounds.Width * fraction);
        if (fillWidth <= 0)
            return;

        // Keep the fill at least pill-width so low values still read as a rounded sliver.
        fillWidth = Math.Max(fillWidth, bounds.Height);
        var fillRect = new Rectangle(0, 0, fillWidth, bounds.Height);
        using GraphicsPath fillPath = Theme.RoundedRect(fillRect, radius);
        using var fillBrush = new SolidBrush(Theme.SemanticColor(_value));
        g.FillPath(fillBrush, fillPath);
    }
}
