using System.Drawing.Drawing2D;

namespace Krypton;

// Chrome-style caption buttons (minimize / maximize-or-restore / close) for
// the dark tab strip. This control is PAINT + HOVER/PRESS visuals only: the
// OS sends WM_NCHITTEST here first, which is forwarded transparently to
// MainForm (see below); MainForm returns the native 8/9/20 codes for hover
// behavior and performs the minimize/maximize/close actions itself on
// same-button release. The control's own MouseMove never fires over its
// surface, so press state arrives via SetPressedHit (hover is untracked:
// the strip must never lighten).
internal sealed class CaptionButtons : Control
{
    // Pressed button for the click gesture (0/1/2, -1 none); hovered button
    // for the hover lighting (same coding, -1 none).
    private int _hover = -1;
    private int _pressed = -1;

    public CaptionButtons()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
        BackColor = MainForm.StripBack;
    }

    // Native-proportioned buttons: same width the OS would use, so the
    // cluster matches user expectations at every DPI.
    public int ButtonWidth => Math.Max(28, SystemInformation.CaptionButtonSize.Width);

    public void SetHoverHit(int hitCode) => SetField(ref _hover, HitToIndex(hitCode));

    public void SetPressedHit(int hitCode) => SetField(ref _pressed, HitToIndex(hitCode));

    // The currently pressed button (0/1/2, -1 none): MainForm owns the whole
    // click gesture (press on DOWN, act on same-button UP) instead of letting
    // DefWindowProc track it — its tracking rects are the system-metric ones,
    // not our painted ones, so its release validation cancels the action and
    // its press paint flashes white in the wrong place.
    public int PressedIndex => _pressed;

    internal static int HitToIndex(int hitCode) => hitCode switch
    {
        8 => 0, // HTMINBUTTON
        9 => 1, // HTMAXBUTTON
        20 => 2, // HTCLOSE
        _ => -1,
    };

    // Glyph follows live WindowState (maximize box <-> restore overlap).
    public void RefreshState() => Invalidate();

    private void SetField(ref int field, int value)
    {
        if (field != value)
        {
            field = value;
            Invalidate();
        }
    }

    // Transparent to hit-testing (see TabStripHost): the OS sends WM_NCHITTEST
    // to the topmost child first, so without this MainForm's caption mapping
    // would never run and the buttons would be dead paint. HTTRANSPARENT
    // forwards to MainForm, which answers the native 8/9/20 codes. Hover/press
    // still arrive via the form's WM_NCMOUSEMOVE/WM_NCLBUTTONDOWN.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) // WM_NCHITTEST
        {
            m.Result = (IntPtr)(-1); // HTTRANSPARENT: ask my parent instead
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Hover lighting (subtle) + press lighting (stronger), red for close:
        // the strip reacts like Chrome's caption buttons. Actions confirm
        // themselves too, but the lighting is the live feedback.
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = Width / 3; // Width is always 3 * ButtonWidth: equal thirds
        var form = FindForm();
        bool maximized = form?.WindowState == FormWindowState.Maximized ||
            (form is MainForm mf && mf.IsFullScreen);
        for (int i = 0; i < 3; i++)
        {
            var r = new Rectangle(i * w, 0, w, Height);
            Color bg = BackColor;
            Color fg = MainForm.CaptionGlyph;
            if (i == _pressed)
            {
                bg = i == 2 ? MainForm.CaptionClosePress : MainForm.CaptionPress;
                if (i == 2)
                {
                    fg = Color.White;
                }
            }
            else if (i == _hover)
            {
                bg = i == 2 ? MainForm.CaptionCloseHover : MainForm.CaptionHover;
                if (i == 2)
                {
                    fg = Color.White;
                }
            }
            using (var b = new SolidBrush(bg))
            {
                g.FillRectangle(b, r);
            }
            DrawGlyph(g, fg, BackColor, r, i, i == 1 && maximized);
        }
    }

    private static void DrawGlyph(Graphics g, Color fg, Color bg, Rectangle r, int index, bool restore)
    {
        float u = r.Width / 46f; // glyph unit: reference buttons are 46px wide
        float cx = r.X + r.Width / 2f;
        float cy = r.Height / 2f;
        using var pen = new Pen(fg, Math.Max(1f, u * 1.1f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        switch (index)
        {
            case 0: // minimize: horizontal dash
                g.DrawLine(pen, cx - 5 * u, cy, cx + 5 * u, cy);
                break;
            case 1 when restore:
                // Restore: a real overlap — the back square is drawn whole,
                // then the opaque front square erases its lower-left, exactly
                // like stacked windows. No edge math to misalign at any DPI:
                // the visible remainder (top + right + foot) is automatic.
                float bw = 8 * u;
                var back = new RectangleF(cx - 1 * u, cy - 5 * u, bw, bw);
                var front = new RectangleF(cx - 5 * u, cy - 1 * u, bw, bw);
                g.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
                using (var bgFill = new SolidBrush(bg))
                {
                    g.FillRectangle(bgFill, front);
                }
                g.DrawRectangle(pen, front.X, front.Y, front.Width, front.Height);
                break;
            case 1: // maximize: square outline
                float s = 10 * u;
                g.DrawRectangle(pen, cx - s / 2f, cy - s / 2f, s, s);
                break;
            default: // close: symmetric ×
                float d = 4.5f * u;
                g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                break;
        }
    }
}
