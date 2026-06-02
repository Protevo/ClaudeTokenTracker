using System.Drawing;
using ClaudeTokenTracker.Models;

namespace ClaudeTokenTracker.UI;

/// <summary>One window's row in <see cref="UsageForm"/>: a progress ring on the left,
/// window name and reset time on the right.</summary>
internal sealed class UsageRow : Panel
{
    private const int PadX = 18;
    private const int RingSize = 42;
    private const int TextGap = 14;

    private readonly Label _name;
    private readonly Label _reset;
    private readonly UsageRing _ring;

    public UsageRow(UsageWindow window)
    {
        Height = 58;
        Margin = new Padding(0);
        BackColor = Theme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        _ring = new UsageRing
        {
            Value = window.Utilization,
        };

        _name = new Label
        {
            Text = window.DisplayName,
            AutoSize = true,
            Font = Theme.Sans(10f),
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
        };

        _reset = new Label
        {
            Text = FormatResetDetailed(window.ResetsAt),
            AutoSize = true,
            Font = Theme.Sans(8.25f),
            ForeColor = Theme.InkMuted,
            BackColor = Color.Transparent,
        };

        Controls.Add(_ring);
        Controls.Add(_name);
        Controls.Add(_reset);

        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        int textLeft = PadX + RingSize + TextGap;
        int rowCenterY = Height / 2;

        _ring.SetBounds(PadX, rowCenterY - RingSize / 2, RingSize, RingSize);

        _reset.Location = new Point(Width - PadX - _reset.Width, rowCenterY - _reset.Height / 2);

        int nameMax = Math.Max(80, _reset.Left - textLeft - 12);
        _name.MaximumSize = new Size(nameMax, 0);
        _name.Location = new Point(textLeft, rowCenterY - _name.Height / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, PadX, Height - 1, Width - PadX, Height - 1);
    }

    public static string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
            return string.Empty;

        TimeSpan remaining = resetsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
            return "resetting…";

        if (remaining.TotalDays >= 1)
            return $"resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"resets in {Math.Max(1, (int)remaining.TotalMinutes)}m";
    }

    /// <summary>
    /// The exact wall-clock reset moment, phrased relative to today:
    /// "6:30 PM", "tomorrow 6:30 PM", "Wed 6:30 PM", or "Jun 9, 6:30 PM".
    /// </summary>
    public static string FormatResetClock(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
            return string.Empty;

        DateTime when = resetsAt.Value.ToLocalTime().DateTime;
        string time = when.ToString("h:mm tt");

        int days = (when.Date - DateTime.Now.Date).Days;
        return days switch
        {
            <= 0 => time,
            1 => "tomorrow " + time,
            < 7 => when.ToString("ddd ") + time,
            _ => when.ToString("MMM d, ") + time,
        };
    }

    /// <summary>
    /// Reset countdown paired with the absolute time for the detail rows, e.g.
    /// "resets in 2h 30m  ·  6:30 PM".
    /// </summary>
    public static string FormatResetDetailed(DateTimeOffset? resetsAt)
    {
        string countdown = FormatReset(resetsAt);
        string clock = FormatResetClock(resetsAt);
        if (clock.Length == 0 || countdown is "" or "resetting…")
            return countdown;
        return $"{countdown}  ·  {clock}";
    }
}
