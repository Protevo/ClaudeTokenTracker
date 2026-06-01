using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeTokenTracker.UI;

/// <summary>
/// A flat, rounded, owner-drawn button matching the Anthropic aesthetic.
/// <see cref="ButtonVariant.Primary"/> is a filled clay call-to-action;
/// <see cref="ButtonVariant.Secondary"/> is a quiet outlined button. Corners are
/// anti-aliased and the area outside them is painted with the parent colour so
/// the button reads as truly rounded against the ivory canvas.
/// </summary>
internal sealed class FlatButton : Button
{
    public enum ButtonVariant { Primary, Secondary }

    private bool _hover;
    private bool _pressed;
    private ButtonVariant _variant = ButtonVariant.Secondary;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        Font = Theme.Sans(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 34;
    }

    public ButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    public int CornerRadius { get; set; } = 8;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Canvas);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRect(rect, CornerRadius);

        if (_variant == ButtonVariant.Primary)
        {
            Color fill = !Enabled ? Theme.InkMuted
                : _pressed ? Theme.AccentPressed
                : _hover ? Theme.AccentHover
                : Theme.Accent;
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        else
        {
            Color fill = !Enabled ? Theme.Canvas
                : _pressed ? Theme.SurfaceHover
                : _hover ? Theme.SurfaceAlt
                : Theme.Canvas;
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            using var pen = new Pen(Enabled ? Theme.BorderStrong : Theme.Border, 1f);
            g.DrawPath(pen, path);
        }

        Color textColor = !Enabled ? Theme.InkMuted
            : _variant == ButtonVariant.Primary ? Theme.InkOnAccent
            : Theme.Ink;
        TextRenderer.DrawText(g, Text, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
