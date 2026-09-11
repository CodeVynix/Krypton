namespace Krypton;

// Owner-drawn tab title. A stock Label leaves too much implicit on a
// machine we can't see: AutoSize/AutoEllipsis interplay, transparent
// background over themed panels, theme-driven foreground. This control
// paints TextRenderer text (same GDI look) with EXPLICIT bounds, colors,
// and ellipsis — the owner (BuildStripItem/ActivateTab) always sets an
// opaque BackColor + contrasting ForeColor pair, so the text cannot
// wash out or misplace on any theme.
internal sealed class TabTitleLabel : Control
{
    // Remote paint proof: incremented every OnPaint, surfaced in the diag
    // snapshot. If titles are blank but paints=0, paint never runs (parent /
    // clipping); if paints>0 + err set, the draw call itself throws.
    internal static int Paints;
    internal static string? LastPaintError;

    public TabTitleLabel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate(); // Text sets from CEF threads must repaint; don't rely on default invalidation
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // GDI+ DrawString (same stack as the working ×/+ glyphs), NOT GDI
        // TextRenderer: the strip's geometric paints render on the user's
        // machine while GDI text may not, which matches blank-title + visible-×.
        try
        {
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(BackColor))
            {
                g.FillRectangle(back, ClientRectangle);
            }
            if (Text.Length > 0)
            {
                using var fore = new SolidBrush(ForeColor);
                // GenericTypographic: no intrinsic 1/6-em left padding that the
                // default StringFormat adds (it stacked with the icon gap to
                // read as double-space). Glyphs now start at the layout rect.
                using var sf = new StringFormat(StringFormat.GenericTypographic)
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                };
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var layout = new RectangleF(0, 0, Width, Height);
                g.DrawString(Text, Font, fore, layout, sf);
            }
            Paints++;
        }
        catch (Exception ex)
        {
            // Magenta = unmistakable in a screenshot: paint runs, draw throws.
            LastPaintError = $"{ex.GetType().Name}: {ex.Message}".Replace("\r", " ").Replace("\n", " ");
            try
            {
                using var fail = new SolidBrush(Color.Magenta);
                e.Graphics.FillRectangle(fail, ClientRectangle);
            }
            catch { }
            Paints++;
        }
    }
}
