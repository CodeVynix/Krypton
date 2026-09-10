using System.Drawing.Drawing2D;

namespace Krypton;

// Chrome-style new-tab button: borderless geometric +, larger than the tab ×,
// with a white circular hover disc. Owner-drawn (a stock Button can't drop
// its border or do a circular hover). Transparent backcolor must be set after
// SetStyle, same as TabCloseButton.
internal sealed class TabPlusButton : Control
{
    // Settable so owners can pair the glyph/disc with the strip theme.
    public Color GlyphColor { get; set; } = Color.FromArgb(68, 68, 68);
    public Color HoverColor { get; set; } = Color.White;

    private bool _hover;

    public TabPlusButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        Size = new System.Drawing.Size(32, 26); // large Chrome-style hit area (× is 16x16)
        Cursor = Cursors.Hand;
        TabStop = false; // no focus rect: stays borderless like Chrome
        BackColor = Color.Transparent; // no borders, transparent until hover
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover)
        {
            int d = Math.Min(Width, Height) - 6;
            using var disc = new SolidBrush(HoverColor);
            g.FillEllipse(disc, (Width - d) / 2f, (Height - d) / 2f, d, d);
        }
        float cx = Width / 2f;
        float cy = Height / 2f;
        const float r = 7f; // large Chrome-style + (pairs with the small ×)
        using var pen = new Pen(GlyphColor, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(pen, cx - r, cy, cx + r, cy);
        g.DrawLine(pen, cx, cy - r, cx, cy + r);
    }
}
