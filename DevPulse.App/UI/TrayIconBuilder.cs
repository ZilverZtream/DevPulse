using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DevPulse.App.UI;

public enum HealthStatus
{
    Initializing,
    Healthy,
    Stale,
    Failing,
    AuthRequired
}

/// <summary>
/// Composes the tray icon by overlaying a small health-indicator dot on top of the base icon.
/// Each composed Icon owns an HICON allocated by Bitmap.GetHicon() — caller must Dispose to free it.
/// TrayApplicationContext caches per-status icons so the bitmap isn't rebuilt on every refresh.
/// </summary>
public static class TrayIconBuilder
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Dictionary<HealthStatus, Color> StatusColors = new()
    {
        [HealthStatus.Healthy] = Color.FromArgb(0x4a, 0xde, 0x80),
        [HealthStatus.Stale] = Color.FromArgb(0xfa, 0xcc, 0x15),
        [HealthStatus.Failing] = Color.FromArgb(0xef, 0x44, 0x44),
        [HealthStatus.AuthRequired] = Color.FromArgb(0xef, 0x44, 0x44),
        [HealthStatus.Initializing] = Color.FromArgb(0x9c, 0xa3, 0xaf),
    };

    /// <summary>
    /// Returns a new Icon with a colored health dot drawn over the bottom-right corner of the base.
    /// Caller must Dispose the returned Icon.
    /// </summary>
    public static Icon Compose(Icon baseIcon, HealthStatus status)
    {
        ArgumentNullException.ThrowIfNull(baseIcon);

        var size = baseIcon.Size;
        // Some HICONs report 0×0 — fall back to the standard 16×16 tray size.
        var w = size.Width <= 0 ? 16 : size.Width;
        var h = size.Height <= 0 ? 16 : size.Height;

        // Dot is ~40% of the icon's edge — 6px on a 16px icon, ~13px on a 32px icon.
        var dot = Math.Max(5, (int)Math.Round(w * 0.40));

        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawIcon(baseIcon, new Rectangle(0, 0, w, h));

            var color = StatusColors[status];
            var x = w - dot;
            var y = h - dot;
            // White outline so the dot reads against light or dark tray backgrounds.
            using (var outlineBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                g.FillEllipse(outlineBrush, x - 1, y - 1, dot + 2, dot + 2);
            using (var fill = new SolidBrush(color))
                g.FillEllipse(fill, x, y, dot, dot);
        }

        var hIcon = bmp.GetHicon();
        // GetHicon allocates an HICON we own. Icon.FromHandle does not take ownership, so we clone
        // (which allocates its own HICON) and then destroy the original. The result is one
        // self-owning Icon whose Dispose will free its HICON.
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
