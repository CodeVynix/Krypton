using System.Drawing.Drawing2D;

namespace Krypton;

// Chrome-style tab close button: a solid dark-gray × that stays legible on
// both active (white) and inactive (light gray) tab backgrounds, with a light
// circular hover disc so it's obvious the glyph is clickable. Owner-drawn
// because a stock Button can't do a circular hover highlight.
internal sealed class TabCloseButton : Control
{
    private static readonly Color GlyphColor = Color.FromArgb(60, 60, 60);
    private static readonly Color HoverColor = Color.FromArgb(218, 218, 218);

    private bool _hover;

    public TabCloseButton()
    {
        // SetStyle first: Control rejects Transparent BackColor without it.
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        Size = new System.Drawing.Size(22, 22);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
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
            int d = Math.Min(Width, Height) - 2;
            using var disc = new SolidBrush(HoverColor);
            g.FillEllipse(disc, (Width - d) / 2f, (Height - d) / 2f, d, d);
        }
        // Geometric × (not a font glyph): two strokes mirrored around the exact
        // control center, so the mark is optically centered vertically and
        // horizontally regardless of font metrics. Matches the + alignment.
        float cx = Width / 2f;
        float cy = Height / 2f;
        const float r = 5.5f;
        using var pen = new Pen(GlyphColor, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
        g.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
    }
}
