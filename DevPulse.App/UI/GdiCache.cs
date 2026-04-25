using System.Collections.Concurrent;
using System.Drawing.Drawing2D;

namespace DevPulse.App.UI;

/// <summary>
/// App-lifetime GDI primitives shared across owner-drawn controls.
/// Fonts/Brushes/Pens/StringFormats here are intentionally never disposed —
/// they live for the process lifetime to avoid per-paint allocations.
/// </summary>
internal static class GdiCache
{
    public static readonly Font TitleFont = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font IdFont = new("Segoe UI", 8f, FontStyle.Bold);
    public static readonly Font BadgeFont = new("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font InitialsFont = new("Segoe UI", 7f, FontStyle.Bold);
    public static readonly Font LinkFont = new("Segoe UI", 7.5f, FontStyle.Regular);

    public static readonly StringFormat TitleFormat = new()
    {
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.LineLimit,
        LineAlignment = StringAlignment.Near
    };

    public static readonly StringFormat CenterFormat = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center
    };

    public static readonly StringFormat LeftFormat = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near
    };

    public static readonly StringFormat WordEllipsisFormat = new()
    {
        Trimming = StringTrimming.EllipsisWord,
        FormatFlags = StringFormatFlags.LineLimit,
        LineAlignment = StringAlignment.Near
    };

    private static readonly ConcurrentDictionary<int, SolidBrush> _brushCache = new();
    private static readonly ConcurrentDictionary<long, Pen> _penCache = new();

    /// <summary>
    /// Returns a cached SolidBrush for the given color. The brush is owned by the cache —
    /// callers must NOT dispose it.
    /// </summary>
    public static SolidBrush Brush(Color c)
        => _brushCache.GetOrAdd(c.ToArgb(), argb => new SolidBrush(Color.FromArgb(argb)));

    /// <summary>
    /// Returns a cached Pen for the given color and width. The pen is owned by the cache —
    /// callers must NOT dispose it.
    /// </summary>
    public static Pen Pen(Color c, float width)
    {
        // Pack ARGB (32) and width (rounded to 0.1 increments, 32) into a 64-bit key.
        long key = ((long)c.ToArgb() << 32) | (uint)(int)(width * 10f);
        return _penCache.GetOrAdd(key, _ => new Pen(c, width));
    }

    /// <summary>
    /// Fills a rounded rect using a cached brush. Allocates only the GraphicsPath, which is disposed.
    /// </summary>
    public static void FillRoundedRect(Graphics g, Color color, float x, float y, float w, float h, float r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(Brush(color), path);
    }

    /// <summary>
    /// Draws a rounded rect outline using a cached pen. Allocates only the GraphicsPath, which is disposed.
    /// </summary>
    public static void DrawRoundedRect(Graphics g, Color color, float width, float x, float y, float w, float h, float r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.DrawPath(Pen(color, width), path);
    }

    public static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
