using System.Drawing.Drawing2D;

namespace DevPulse.App.UI;

/// <summary>
/// Indeterminate spinner overlay. Parents to a host control via Dock=Fill, paints a semi-transparent
/// dark scrim, an animated 8-dot spinner, and an optional caption. Swallows clicks while visible
/// so the user can't interact with controls underneath.
/// </summary>
/// <remarks>
/// Owner-drawn, double-buffered. The animation timer is a <see cref="System.Windows.Forms.Timer"/>
/// at ~30 fps; it only runs while the control is visible to avoid background CPU.
/// </remarks>
public sealed class SpinnerOverlay : Control
{
    private const int DotCount = 8;
    private const int FrameIntervalMs = 33; // ~30 fps
    private const float DotRadius = 5f;
    private const float RingRadius = 22f;
    private const int CaptionGap = 14;

    private static readonly Color ScrimColor = Color.FromArgb(160, 14, 14, 28);
    private static readonly Color DotColor = Color.FromArgb(220, 224, 224, 240);
    private static readonly Color CaptionColor = Color.FromArgb(220, 200, 200, 220);

    // Process-lifetime font, intentionally never disposed (mirrors GdiCache convention).
    private static readonly Font CaptionFont = new("Segoe UI", 9.5f, FontStyle.Regular);

    private readonly System.Windows.Forms.Timer _timer;
    private int _frame;
    private string _message = "";

    public SpinnerOverlay()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, false);
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw, true);

        TabStop = false;
        BackColor = Color.Transparent;
        DoubleBuffered = true;

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Optional caption rendered below the spinner. Empty string hides it.
    /// </summary>
    public string Message
    {
        get => _message;
        set
        {
            _message = value ?? "";
            if (IsHandleCreated) Invalidate();
        }
    }

    /// <summary>Exposed for tests; true while the animation timer is running.</summary>
    internal bool IsAnimating => _timer.Enabled;

    /// <summary>
    /// Parents the overlay to <paramref name="parent"/>, docks it Fill, brings it to front and starts spinning.
    /// Reuses an existing overlay if the parent already hosts one (idempotent).
    /// </summary>
    public static SpinnerOverlay ShowOver(Control parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        SpinnerOverlay? overlay = null;
        foreach (Control c in parent.Controls)
        {
            if (c is SpinnerOverlay existing) { overlay = existing; break; }
        }

        if (overlay is null)
        {
            overlay = new SpinnerOverlay { Dock = DockStyle.Fill };
            parent.Controls.Add(overlay);
        }

        overlay.BringToFront();
        overlay.Show();
        return overlay;
    }

    public new void Show()
    {
        base.Show();
        if (!_timer.Enabled) _timer.Start();
        Invalidate();
    }

    public new void Hide()
    {
        if (_timer.Enabled) _timer.Stop();
        base.Hide();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // Keep the timer in sync if visibility flips via a parent or designer rather than Show/Hide.
        if (Visible && !_timer.Enabled) _timer.Start();
        else if (!Visible && _timer.Enabled) _timer.Stop();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }

    // Swallow mouse interaction so clicks don't pass through to controls underneath.
    protected override void OnMouseDown(MouseEventArgs e) { /* swallow */ }
    protected override void OnMouseUp(MouseEventArgs e) { /* swallow */ }
    protected override void OnMouseClick(MouseEventArgs e) { /* swallow */ }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { /* swallow */ }

    private void OnTick(object? sender, EventArgs e)
    {
        // Step rotation; modulo keeps the int small and avoids overflow on long-running overlays.
        _frame = (_frame + 1) % (DotCount * 256);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Scrim
        g.FillRectangle(GdiCache.Brush(ScrimColor), ClientRectangle);

        var cx = ClientSize.Width / 2f;
        var cy = ClientSize.Height / 2f;

        // Vertically center the spinner+caption block when a caption is present.
        if (!string.IsNullOrEmpty(_message))
        {
            // Shift the spinner up a bit so the combined block reads as centered.
            var captionH = CaptionFont.Height;
            cy -= (captionH + CaptionGap) / 2f;
        }

        DrawDots(g, cx, cy);

        if (!string.IsNullOrEmpty(_message))
        {
            var textY = cy + RingRadius + DotRadius + CaptionGap;
            var rect = new RectangleF(0, textY, ClientSize.Width, CaptionFont.Height + 2);
            g.DrawString(_message, CaptionFont, GdiCache.Brush(CaptionColor), rect, GdiCache.CenterFormat);
        }
    }

    private void DrawDots(Graphics g, float cx, float cy)
    {
        // The "head" dot is fully opaque; trailing dots fade. Rotation advances by one slot per ~8 frames
        // for a smooth but noticeable sweep.
        var head = (_frame / 4) % DotCount;
        const double step = Math.PI * 2.0 / DotCount;

        for (int i = 0; i < DotCount; i++)
        {
            var angle = i * step - Math.PI / 2.0; // start at 12 o'clock
            var x = cx + (float)Math.Cos(angle) * RingRadius;
            var y = cy + (float)Math.Sin(angle) * RingRadius;

            // Distance behind the head dot, going backwards around the ring.
            var trail = (head - i + DotCount) % DotCount;
            // Map trail 0 (head) -> 1.0 alpha, trail (DotCount-1) -> ~0.18 alpha.
            var t = 1.0f - (trail / (float)(DotCount - 1));
            var alpha = (int)(60 + 195 * t); // 60..255

            var color = Color.FromArgb(alpha, DotColor.R, DotColor.G, DotColor.B);
            g.FillEllipse(GdiCache.Brush(color), x - DotRadius, y - DotRadius, DotRadius * 2, DotRadius * 2);
        }
    }
}
