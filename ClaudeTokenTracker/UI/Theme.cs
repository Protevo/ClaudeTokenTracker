using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeTokenTracker.UI;

/// <summary>
/// The app's shared visual language, modelled on Anthropic's warm, editorial
/// aesthetic: an ivory canvas instead of clinical white, warm charcoal ink, a
/// single clay accent used sparingly, hairline rules instead of heavy borders,
/// and a serif for headings. Centralising it keeps every window consistent.
/// Palette values are Anthropic's published brand colours.
/// </summary>
internal static class Theme
{
    // --- Surfaces (warm "paper" — never pure white) ---
    public static readonly Color Canvas      = Color.FromArgb(0xFA, 0xF9, 0xF5); // primary background
    public static readonly Color SurfaceAlt  = Color.FromArgb(0xF0, 0xEE, 0xE6); // bands / subtle fills
    public static readonly Color SurfaceHover = Color.FromArgb(0xE8, 0xE6, 0xDC); // hover on alt surfaces
    public static readonly Color Field        = Color.FromArgb(0xFF, 0xFF, 0xFF); // text inputs

    // --- Hairlines ---
    public static readonly Color Border       = Color.FromArgb(0xE8, 0xE6, 0xDC); // light-gray rule
    public static readonly Color BorderStrong = Color.FromArgb(0xD6, 0xD2, 0xC6);

    // --- Ink (warm charcoal, deliberately not blue-black) ---
    public static readonly Color Ink          = Color.FromArgb(0x14, 0x14, 0x13);
    public static readonly Color InkSecondary = Color.FromArgb(0x57, 0x55, 0x4E);
    public static readonly Color InkMuted     = Color.FromArgb(0x8A, 0x87, 0x7E);
    public static readonly Color InkOnAccent  = Color.FromArgb(0xFD, 0xFC, 0xF8);

    // --- Clay accent (the signature; used sparingly for interaction) ---
    public static readonly Color Accent        = Color.FromArgb(0xD9, 0x77, 0x57);
    public static readonly Color AccentHover   = Color.FromArgb(0xC6, 0x61, 0x3F);
    public static readonly Color AccentPressed = Color.FromArgb(0xB2, 0x55, 0x37);

    // --- Semantic usage ramp (warm, harmonised with the palette) ---
    public static readonly Color Safe   = Color.FromArgb(0x6E, 0x8B, 0x4E); // olive green  (< 50%)
    public static readonly Color Notice = Color.FromArgb(0xC4, 0x8A, 0x33); // ochre        (50–79%)
    public static readonly Color Warn   = Color.FromArgb(0xD9, 0x77, 0x57); // clay         (80–94%)
    public static readonly Color Danger = Color.FromArgb(0xB5, 0x3D, 0x2C); // deep brick   (95%+)

    // --- Progress track ---
    public static readonly Color Track  = Color.FromArgb(0xEC, 0xE9, 0xDF);

    // --- Type families (system fonts that evoke the brand: serif headings, clean sans body) ---
    public const string SerifFamily = "Georgia";
    public const string SansFamily  = "Segoe UI";
    public const string MonoFamily  = "Consolas";

    public static Font Serif(float size, FontStyle style = FontStyle.Regular) => new(SerifFamily, size, style);
    public static Font Sans(float size, FontStyle style = FontStyle.Regular) => new(SansFamily, size, style);
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) => new(MonoFamily, size, style);

    /// <summary>Maps a utilization percentage (0–100+) onto the semantic ramp.</summary>
    public static Color SemanticColor(int percent) => percent switch
    {
        >= 95 => Danger,
        >= 80 => Warn,
        >= 50 => Notice,
        _ => Safe,
    };

    /// <summary>Maps a utilization fraction (0.0–1.0+) onto the semantic ramp.</summary>
    public static Color SemanticColor(double fraction) =>
        SemanticColor((int)Math.Round(fraction * 100));

    /// <summary>Paints a 1px hairline rule along the top or bottom edge of a control,
    /// for use from its <c>Paint</c> handler (the Anthropic look favours rules over bands).</summary>
    public static void Hairline(Control c, PaintEventArgs e, bool top)
    {
        int y = top ? 0 : c.Height - 1;
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, 0, y, c.Width, y);
    }

    /// <summary>A rounded-rectangle path for the given bounds (caller disposes).</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
