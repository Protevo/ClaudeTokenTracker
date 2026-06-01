using System.Drawing;
using ClaudeTokenTracker.Models;

namespace ClaudeTokenTracker.UI;

/// <summary>One window's row in <see cref="UsageForm"/>: name + reset on top,
/// a slim meter with a bold, colour-coded percentage beneath.</summary>
internal sealed class UsageRow : Panel
{
    private const int PadX = 18;

    private readonly Label _name;
    private readonly Label _reset;
    private readonly Label _percent;
    private readonly UsageBar _bar;

    public UsageRow(UsageWindow window)
    {
        Height = 78;
        Margin = new Padding(0);
        BackColor = Theme.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        _name = new Label
        {
            Text = window.DisplayName,
            AutoSize = true,
            Font = Theme.Sans(10f),
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Location = new Point(PadX, 12),
        };

        _reset = new Label
        {
            Text = FormatResetDetailed(window.ResetsAt),
            AutoSize = true,
            Font = Theme.Sans(8.25f),
            ForeColor = Theme.InkMuted,
            BackColor = Color.Transparent,
            Location = new Point(PadX, 58),
        };

        _percent = new Label
        {
            Text = window.Percent + "%",
            AutoSize = true,
            Font = Theme.Sans(11f, FontStyle.Bold),
            ForeColor = Theme.SemanticColor(window.Percent),
            BackColor = Color.Transparent,
        };

        _bar = new UsageBar
        {
            Value = window.Utilization,
            Location = new Point(PadX, 34),
            Height = 8,
        };

        Controls.Add(_name);
        Controls.Add(_reset);
        Controls.Add(_percent);
        Controls.Add(_bar);

        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        int barCenterY = 38;
        _percent.Location = new Point(Width - PadX - _percent.Width, barCenterY - _percent.Height / 2);

        _bar.Location = new Point(PadX, barCenterY - _bar.Height / 2);
        _bar.Width = Math.Max(40, _percent.Left - 12 - PadX);

        _reset.Location = new Point(PadX, 58);
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
