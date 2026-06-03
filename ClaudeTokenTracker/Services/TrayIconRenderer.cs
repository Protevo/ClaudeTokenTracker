using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeTokenTracker.UI;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Draws the tray icon on the fly so it can show the current peak utilization as a
/// number with traffic-light colours. Returns the raw HICON alongside the Icon so
/// the caller can DestroyIcon it and avoid leaking GDI handles.
/// </summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            DestroyIcon(handle);
    }

    /// <param name="percent">5-hour session utilization 0-100+, or null when unknown/errored.</param>
    public static (Icon Icon, IntPtr Handle) Render(int? percent)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color fill = ColorFor(percent);
            using var bg = new SolidBrush(fill);
            using var border = new Pen(Darken(fill, 0.65f), 2f);

            var rect = new Rectangle(1, 1, size - 3, size - 3);
            g.FillEllipse(bg, rect);
            g.DrawEllipse(border, rect);

            string text = percent is null ? "?" : (percent >= 100 ? "!" : percent.Value.ToString());
            float fontSize = text.Length >= 2 ? 15f : 19f;

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            // Nudge up a hair; digits sit slightly low otherwise.
            var textRect = new RectangleF(0, -1, size, size);
            g.DrawString(text, font, textBrush, textRect, fmt);
        }

        IntPtr handle = bmp.GetHicon();
        // Clone so the Icon owns managed data independent of the (about-to-be-disposed) bitmap.
        using var fromHandle = Icon.FromHandle(handle);
        var icon = (Icon)fromHandle.Clone();
        return (icon, handle);
    }

    // Share the app's semantic ramp so the tray icon, meters, and labels all agree.
    private static Color ColorFor(int? percent) =>
        percent is null ? Theme.InkMuted : Theme.SemanticColor(percent.Value);

    private static Color Darken(Color c, float factor) => Color.FromArgb(
        c.A,
        (int)(c.R * factor),
        (int)(c.G * factor),
        (int)(c.B * factor));
}
