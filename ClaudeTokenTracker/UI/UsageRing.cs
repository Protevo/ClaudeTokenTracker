using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeTokenTracker.UI;

/// <summary>
/// Compact circular progress ring with the percentage in the centre. Reads clearly
/// at a glance and avoids the clipped-cap artefacts of a slim pill bar.
/// </summary>
public sealed class UsageRing : Control
{
    private const float Stroke = 3.5f;
    private const int Pad = 2;

    private double _value;

    public UsageRing()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(42, 42);
        MinimumSize = Size;
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
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? Theme.Canvas);

        if (Width <= Pad * 2 || Height <= Pad * 2)
            return;

        var rect = new RectangleF(
            Pad + Stroke / 2f,
            Pad + Stroke / 2f,
            Width - (Pad * 2 + Stroke),
            Height - (Pad * 2 + Stroke));

        using (var trackPen = new Pen(Theme.Track, Stroke))
        {
            trackPen.StartCap = LineCap.Round;
            trackPen.EndCap = LineCap.Round;
            g.DrawArc(trackPen, rect, 0, 360);
        }

        double fraction = Math.Clamp(_value, 0, 1);
        if (fraction > 0)
        {
            float sweep = 360f * (float)fraction;
            using var fillPen = new Pen(Theme.SemanticColor(_value), Stroke);
            fillPen.StartCap = LineCap.Round;
            fillPen.EndCap = LineCap.Round;
            // Start at 12 o'clock and sweep clockwise.
            g.DrawArc(fillPen, rect, -90, sweep);
        }

        int percent = (int)Math.Round(Math.Clamp(_value, 0, 9.99) * 100);
        string text = percent + "%";
        float fontSize = text.Length >= 4 ? 8.5f : 9.5f;

        using var font = Theme.Sans(fontSize, FontStyle.Bold);
        using var brush = new SolidBrush(Theme.SemanticColor(percent));
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, brush, new RectangleF(0, 0, Width, Height), fmt);
    }
}
