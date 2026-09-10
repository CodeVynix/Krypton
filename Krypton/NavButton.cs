using System.Drawing.Drawing2D;

namespace Krypton;

// Chrome-style borderless toolbar button: geometric glyph centered on the
// exact pixel center, circular hover disc, grayed glyph when disabled.
// Covers back / forward / refresh / stop / home — a stock Button can't drop
// its border or do a circular hover.
// `BackColor=Transparent` must come AFTER `SetStyle(SupportsTransparentBackColor)`
// or the ctor throws (kills the app before first paint).
internal enum NavKind
{
    Back,
    Forward,
    Refresh,
    Stop,
    Home,
}

internal sealed class NavButton : Control
{
    // Settable so owners can pair glyph/disc with the bar theme (a dark
    // toolbar needs light glyphs + subtle discs; defaults preserve the light look).
    public Color GlyphColor { get; set; } = Color.FromArgb(68, 68, 68);
    public Color DisabledColor { get; set; } = Color.FromArgb(166, 166, 166);
    public Color HoverBack { get; set; } = Color.FromArgb(213, 213, 213);
    public Color PressedBack { get; set; } = Color.FromArgb(196, 196, 196);

    private bool _hover;
    private bool _pressed;

    public NavKind Kind { get; set; }

    public NavButton()
    {
        // SetStyle first: Control rejects Transparent BackColor without it.
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        Size = new System.Drawing.Size(32, 32);
        Dock = DockStyle.Left;
        Margin = new Padding(1, 0, 1, 0);
        TabStop = false;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover && Enabled)
        {
            int d = Math.Min(Width, Height) - 4;
            using var disc = new SolidBrush(_pressed ? PressedBack : HoverBack);
            g.FillEllipse(disc, (Width - d) / 2f, (Height - d) / 2f, d, d);
        }
        Color fg = Enabled ? GlyphColor : DisabledColor;
        float cx = Width / 2f;
        float cy = Height / 2f;
        switch (Kind)
        {
            case NavKind.Back:
                using (var pen = new Pen(fg, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx + 5, cy, cx - 5.5f, cy);
                    g.DrawLine(pen, cx - 5.5f, cy, cx - 0.5f, cy - 5);
                    g.DrawLine(pen, cx - 5.5f, cy, cx - 0.5f, cy + 5);
                }
                break;
            case NavKind.Forward:
                using (var pen = new Pen(fg, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx - 5, cy, cx + 5.5f, cy);
                    g.DrawLine(pen, cx + 5.5f, cy, cx + 0.5f, cy - 5);
                    g.DrawLine(pen, cx + 5.5f, cy, cx + 0.5f, cy + 5);
                }
                break;
            case NavKind.Refresh:
                // Chrome's circular arrow: near-full ring with the gap at the
                // top, and a short swept head whose base straddles the ring
                // end so the whole mark reads as one continuous loop.
                using (var pen = new Pen(fg, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    const float r = 7f;
                    const float start = 312f;
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, start, 292f);
                    // Arrowhead at the arc start, oriented along the motion.
                    double rad = start * Math.PI / 180.0;
                    var tip = new PointF(cx + r * (float)Math.Cos(rad), cy + r * (float)Math.Sin(rad));
                    var dir = new PointF((float)-Math.Sin(rad), (float)Math.Cos(rad));
                    var n = new PointF(-dir.Y, dir.X);
                    var p1 = new PointF(tip.X - 3.6f * dir.X + 2.2f * n.X, tip.Y - 3.6f * dir.Y + 2.2f * n.Y);
                    var p2 = new PointF(tip.X - 3.6f * dir.X - 2.2f * n.X, tip.Y - 3.6f * dir.Y - 2.2f * n.Y);
                    using var brush = new SolidBrush(fg);
                    g.FillPolygon(brush, [tip, p1, p2]);
                }
                break;
            case NavKind.Stop:
                using (var pen = new Pen(fg, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                }
                break;
            default: // Home: roof + body outline
                using (var pen = new Pen(fg, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, [new PointF(cx - 6, cy + 0.5f), new PointF(cx, cy - 5), new PointF(cx + 6, cy + 0.5f)]);
                    g.DrawRectangle(pen, cx - 4, cy + 0.5f, 8, 6.5f);
                }
                break;
        }
    }
}
