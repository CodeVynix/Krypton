using CefSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Krypton;

// Behavior: tab management + the 6 features, each wired to its CefSharp hook
// (hook locations marked below). CEF events arrive on non-UI threads, so every
// handler marshals through Ui() before touching controls.
partial class MainForm : Form
{
    internal const string NewTabUrl = "krypton://new-tab"; // bundled page (newtab.html), like Chrome's NTP
    private const string SearchUrl = "https://www.google.com/search?q=";

    private static readonly Image DefaultFavicon;
    private static readonly Image? BrandGmail; // bundled M backing the override below (null when missing)
    private static readonly Icon? AppIcon; // process lifetime, cloned per form
    private static readonly HttpClient FaviconHttp = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly List<BrowserTab> _tabs = new();
    private BrowserTab? _activeTab;

    static MainForm()
    {
        FaviconHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Krypton/0.1");
        // Brand assets from the embedded krypton.ico (fallback: generic icon).
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("Krypton.krypton.ico");
            if (s != null)
            {
                AppIcon = new Icon(s, 32, 32);
                // 48px source for the 24px omnibox/tab boxes: pure downscale at
                // 100/125/150% DPI, never an upscale. (Upscaling the old 16px
                // bitmap into the 24px box is what blurred the Kr logo.)
                using var s48 = typeof(MainForm).Assembly.GetManifestResourceStream("Krypton.krypton.ico");
                if (s48 != null)
                {
                    using var icon48 = new Icon(s48, 48, 48);
                    DefaultFavicon = icon48.ToBitmap();
                }
                else
                {
                    DefaultFavicon = new Icon(AppIcon, 32, 32).ToBitmap();
                }
            }
            else
            {
                DefaultFavicon = SystemIcons.Application.ToBitmap();
            }
        }
        catch
        {
            DefaultFavicon = SystemIcons.Application.ToBitmap();
        }
        // Bundled Gmail M for the explicit brand override below. Decoded once;
        // each use gets its own clone so tab disposal can never kill the shared
        // original. Missing resource just disables the override (network path
        // decides as before).
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("Krypton.gmail-dial.png");
            if (s != null)
            {
                using var tmp = Image.FromStream(s);
                BrandGmail = new Bitmap(tmp);
            }
        }
        catch { /* override unavailable -> network pipeline decides */ }
    }

    // ---------- Dark strip theme (Chrome frame colors) ----------
    // Explicit opaque pairs everywhere (never theme-driven): the strip is
    // always dark, so SystemColors pairs would wash out or clash.
    internal static readonly Color StripBack = Color.FromArgb(43, 48, 56);
    private static readonly Color ActiveTabBack = Color.FromArgb(60, 66, 77);
    private static readonly Color ActiveTitleFore = Color.FromArgb(237, 239, 241);
    private static readonly Color InactiveTitleFore = Color.FromArgb(178, 184, 193);
    private static readonly Color TabGlyphLight = Color.FromArgb(204, 208, 213);
    private static readonly Color TabHoverDisc = Color.FromArgb(80, 87, 100);
    // Caption-button chrome (see CaptionButtons): light glyphs on the dark
    // strip, subtle hover, standard red close.
    internal static readonly Color CaptionGlyph = Color.FromArgb(204, 208, 213);
    internal static readonly Color CaptionHover = Color.FromArgb(66, 72, 84);
    internal static readonly Color CaptionPress = Color.FromArgb(84, 91, 104);
    internal static readonly Color CaptionCloseHover = Color.FromArgb(232, 17, 35);
    internal static readonly Color CaptionClosePress = Color.FromArgb(178, 12, 26);
    // Dark toolbar + omnibox (Chrome NTP-dark family) + light nav glyphs.
    private static readonly Color ToolbarBack = Color.FromArgb(32, 33, 36);
    private static readonly Color NavGlyphLight = Color.FromArgb(205, 209, 214);
    private static readonly Color NavGlyphDim = Color.FromArgb(105, 109, 116);
    private static readonly Color NavHoverDisc = Color.FromArgb(58, 60, 65);
    private static readonly Color NavPressDisc = Color.FromArgb(78, 80, 87);

    public MainForm()
    {
        InitializeComponent();
        // Owner-drawn caption cluster, docked right (LayoutTabs subtracts its
        // real width via CaptionReserve, since docking alone does not shrink
        // the host's ClientSize).
        captionButtons = new CaptionButtons { Dock = DockStyle.Right };
        captionButtons.Width = captionButtons.ButtonWidth * 3;
        tabsHost.Controls.Add(captionButtons);
        // Safety net: any client mouse movement means we left the (non-client
        // hit-tested) button zone — drop hover even if an NC-leave went missing.
        tabsHost.MouseMove += (_, _) => captionButtons.SetHoverHit(0);
        btnNewTab.GlyphColor = TabGlyphLight; // + lives on the dark strip now
        btnNewTab.HoverColor = TabHoverDisc;
        foreach (var b in new[] { btnBack, btnForward, btnRefresh, btnStop, btnHome })
        {
            b.GlyphColor = NavGlyphLight; // nav glyphs live on the dark toolbar now
            b.DisabledColor = NavGlyphDim;
            b.HoverBack = NavHoverDisc;
            b.PressedBack = NavPressDisc;
        }
        LayoutToolbar(); // fixed-height centered omnibox (needs docked siblings placed first)
        LoadHistory(); // local completion cache (seeds stay if the file is missing)
        if (AppIcon != null)
        {
            Icon = (Icon)AppIcon.Clone(); // window/taskbar icon matches the exe icon
        }
        faviconBox.Image = DefaultFavicon;
        FormClosing += (_, _) =>
        {
            DisposeAllTabs();
            lock (_historyLock)
            {
                _historyStore?.Dispose();
                _historyStore = null;
            }
        };
        CreateTab(NewTabUrl, activate: true);
    }

    // ---------- Chrome-style frame: no title bar, owner-drawn caption ----------
    // The entire top non-client (caption band + top border) is absorbed into
    // the client area (WM_NCCALCSIZE lifts the client top by the measured
    // topNC), and NO glass is extended, so DWM draws nothing above our strip:
    // no border left to catch light. CaptionButtons paints the min/max/close glyphs, WndProc answers the native
    // HTMINBUTTON/HTMAXBUTTON/HTCLOSE codes (hover behavior), and MainForm
    // performs the actions itself on same-button release — DefWindowProc is
    // cut out of the gesture because its tracking validates the release
    // against the system-metric rects rather than our paint. Empty
    // strip drags (HTCAPTION); a slim top edge resizes (HTTOP).
    // Hit-testing is done ENTIRELY in screen coords (LParam point vs
    // RectangleToScreen rects): it never consults the client origin, so no
    // origin shift — however caused — can ever separate clicks from paint.

    private CaptionButtons captionButtons = null!; // created in the ctor (needs tabsHost)

    private const int WM_NCCALCSIZE = 0x83;
    private const int WM_NCHITTEST = 0x84;
    private const int WM_NCMOUSEMOVE = 0xA0;
    private const int WM_NCMOUSELEAVE = 0x2A2;
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int WM_NCLBUTTONUP = 0xA2;
    private const int WM_NCLBUTTONDBLCLK = 0xA3;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTMINBUTTON = 8;
    private const int HTMAXBUTTON = 9;
    private const int HTTOP = 12;
    private const int HTCLOSE = 20;
    private const int TME_LEAVE = 0x2;
    private const int TME_NONCLIENT = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public RECT[] rgrc;
        public IntPtr lppos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZE = 0x01000000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x1;
    private const uint SWP_NOMOVE = 0x2;
    private const uint SWP_NOZORDER = 0x4;
    private const uint SWP_FRAMECHANGED = 0x20;

    // Last measured top non-client thickness, for the diag.
    private int _lastTopNC;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // DWM frame-color results (diag only): prove the calls landed.
    private int _darkModeHr = int.MinValue; // NOT called yet
    private int _borderColorHr = int.MinValue;

    // Hit-test trace (diag only): the last SCREEN point per region +
    // counters. Only a mapping IN a region overwrites its slot — the + click
    // that snapshots the diag lands in "other" and never disturbs cap/drag/top.
    private int _htCap, _htDrag, _htTop, _htOther;
    private int _capSX, _capSY, _capCode;
    private int _dragSX, _dragSY;
    private int _topSX, _topSY;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutTabs(); // final layout once the handle + DPI bounds exist
        WriteDiagSnapshot("shown"); // first snapshot where Visible is meaningful (create/activate run pre-show)
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PaintFrameDark();
    }

    private FormWindowState _lastWindowState = FormWindowState.Normal;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsDisposed || Disposing || WindowState == _lastWindowState)
        {
            return;
        }
        FormWindowState prev = _lastWindowState;
        _lastWindowState = WindowState;
        if (_fullScreen && WindowState == FormWindowState.Maximized)
        {
            // OS-maximized (Win+Up, taskbar menu, ...) while fullscreen: drop
            // the fullscreen bookkeeping so a stale TopMost can never stick.
            _fullScreen = false;
            TopMost = false;
            captionButtons.RefreshState();
        }
        if (prev != FormWindowState.Minimized || WindowState == FormWindowState.Minimized)
        {
            return; // only the minimized -> visible trip needs repair
        }
        // Coming back from minimized: a transient rect may have slipped past
        // the clamp and left a stale (blank white) frame. Force a fresh frame
        // calculation, then full layout + repaint of chrome and page alike.
        try
        {
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch { }
        LayoutTabs();
        PerformLayout();
        Invalidate(true);
        captionButtons.RefreshState();
        _activeTab?.Browser.Invalidate();
        WriteDiagSnapshot("restore");
    }

    // The top frame border is the last DWM-painted chrome (thin white line on
    // light Windows themes). Recolor it to the strip color (Windows 11+;
    // unknown attributes fail silently on older Windows, keeping OS chrome).
    // Results land in _darkModeHr/_borderColorHr for the diag — no guessing
    // about whether DWM accepted the colors.
    private void PaintFrameDark()
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }
        try
        {
            int dark = 1;
            _darkModeHr = DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
            int border = 0x0038302B; // StripBack #2B3038 as COLORREF 0x00BBGGRR
            _borderColorHr = DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref border, 4);
        }
        catch { /* pre-Win11: keep the OS chrome */ }
    }

    // Read-back for the diag: what DWM reports the border color as (proves
    // the Set landed; if the bar stays white while this reads dark, the bar
    // is NOT the frame border and needs a different fix). Never throws.
    private string ReadBorderColor()
    {
        try
        {
            int hr = DwmGetWindowAttribute(Handle, 34, out int color, 4);
            return $"hr={hr} color=0x{color & 0xFFFFFF:X6}";
        }
        catch (Exception ex)
        {
            return $"ERROR {ex.GetType().Name}";
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
                base.WndProc(ref m); // default rect (below caption, inside borders)
                var ncc = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                // Absorb the ENTIRE top non-client (caption band + top border).
                // topNC is a frame constant (border + caption): LEARN it from a
                // live measurement only when plausible, then always APPLY the
                // stored value to DefWindowProc's own (always self-consistent)
                // proposed client rect. Never mix a live window rect with a
                // proposed client rect across a state transition: during
                // un-maximize they disagree by ~140px, the clamp rejected the
                // measurement, and the kept default rect stuck a native caption
                // back on with no recalc ever following. Iconic garbage is
                // still rejected for learning (the hidden window keeps a stale-
                // but-harmless rect; restore relearns from consistent rects).
                try
                {
                    RECT win = new RECT();
                    if (GetWindowRect(Handle, ref win))
                    {
                        int measured = ncc.rgrc[0].Top - win.Top;
                        if (measured > 0 && measured <= 128)
                        {
                            _lastTopNC = measured;
                        }
                    }
                }
                catch { /* measurement is best-effort; stored value still applies */ }
                if (_lastTopNC > 0)
                {
                    // Maximized windows already hide their resize borders
                    // off-screen (window top goes negative): absorbing the full
                    // constant would push the strip above the monitor and clip
                    // tab titles + caption glyphs. Absorb the caption only.
                    // (Style bits, not WindowState: the state property can lag
                    // the frame calculation mid-transition.)
                    bool isMax = false;
                    try { isMax = (GetWindowLong(Handle, GWL_STYLE) & WS_MAXIMIZE) != 0; }
                    catch { }
                    ncc.rgrc[0].Top -= isMax ? SystemInformation.CaptionHeight : _lastTopNC;
                }
                Marshal.StructureToPtr(ncc, m.LParam, false);
                return;
            case WM_NCHITTEST:
                base.WndProc(ref m); // borders/edges first, then our mapping wins
                Point screen = LParamToScreenPoint(m.LParam);
                int code = HitTestFrame(screen);
                int final = code != 0 ? code : m.Result.ToInt32();
                if (code != 0)
                {
                    m.Result = (IntPtr)code;
                }
                TraceHit(m.LParam, final);
                return;
            case WM_NCMOUSEMOVE:
                // Non-client mouse traffic center: hover lighting lives here
                // (hit-testing steers the mouse away from control MouseMove),
                // plus leave-tracking and wheel-focus grab below. Snap flyout
                // and tooltips are unaffected (hit codes unchanged).
                int hover = m.WParam.ToInt32();
                captionButtons.SetHoverHit(IsCaptionHit(hover) ? hover : 0);
                TrackNcLeave();
                // Wheel events go to the focused control: the transparent host
                // never gets MouseEnter anymore, so grab focus on strip moves
                // instead (only when there is overflow worth scrolling).
                if (hover == HTCAPTION && NeedsTabScroll() && !tabsHost.Focused)
                {
                    try { tabsHost.Focus(); } catch { }
                }
                base.WndProc(ref m);
                return;
            case WM_NCMOUSELEAVE:
                captionButtons.SetHoverHit(0);
                captionButtons.SetPressedHit(0);
                base.WndProc(ref m);
                return;
            case WM_NCLBUTTONDOWN:
            {
                int down = m.WParam.ToInt32();
                if (IsCaptionHit(down))
                {
                    // Owned gesture: track press and act on same-button release
                    // below. DefWindowProc must never see this DOWN — its press
                    // tracking validates the release against the SYSTEM-metric
                    // button rects (not our painted ones), so it would paint a
                    // white press flash in the wrong place and then cancel the
                    // action as "released outside".
                    captionButtons.SetPressedHit(down);
                    return; // swallow
                }
                base.WndProc(ref m);
                return;
            }
            case WM_NCLBUTTONUP:
            {
                int up = m.WParam.ToInt32();
                int pressed = captionButtons.PressedIndex;
                captionButtons.SetPressedHit(0);
                if (pressed != -1)
                {
                    // Release ends OUR gesture (see DOWN): act only when the
                    // release lands on the same button that was pressed.
                    if (IsCaptionHit(up) && CaptionButtons.HitToIndex(up) == pressed)
                    {
                        PerformCaptionAction(pressed);
                    }
                    return; // swallow
                }
                base.WndProc(ref m);
                return;
            }
            case WM_NCLBUTTONDBLCLK:
                // Double-click the maximize button toggles, like native chrome.
                if (m.WParam.ToInt32() == HTMAXBUTTON)
                {
                    ToggleMaxRestore();
                    return; // swallow
                }
                base.WndProc(ref m);
                return;
        }
        base.WndProc(ref m);
    }

    private static bool IsCaptionHit(int hitCode) =>
        hitCode is HTMINBUTTON or HTMAXBUTTON or HTCLOSE;

    // Caption actions, performed directly (see the DOWN handler above for why
    // DefWindowProc is cut out of this gesture). 0 = minimize, 1 = maximize /
    // restore toggle, 2 = close. State changes flow through Resize ->
    // TabsHost_SizeChanged, which swaps the maximize/restore glyph.
    private void PerformCaptionAction(int index)
    {
        switch (index)
        {
            case 0:
                WindowState = FormWindowState.Minimized;
                break;
            case 1:
                ToggleMaxRestore();
                break;
            default:
                Close();
                break;
        }
    }

    private void ToggleMaxRestore()
    {
        if (_fullScreen)
        {
            SetFullScreen(false); // ❐ in fullscreen exits fullscreen, like Chrome
            return;
        }
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    // True fullscreen (covers the taskbar too — OS maximize only fills the
    // work area). No handle recreation: same Sizable frame, same handle, just
    // full-monitor bounds + TopMost, so the CEF renderers never notice. Screen
    // comes from the live monitor, so every resolution/DPI fills exactly.
    private bool _fullScreen;
    private Rectangle _fullRestoreBounds;

    internal bool IsFullScreen => _fullScreen;

    internal void ToggleFullScreen() => SetFullScreen(!_fullScreen);

    internal void SetFullScreen(bool on)
    {
        if (on == _fullScreen || IsDisposed || Disposing)
        {
            return;
        }
        if (on)
        {
            if (WindowState != FormWindowState.Normal)
            {
                WindowState = FormWindowState.Normal;
            }
            _fullRestoreBounds = Bounds;
            _fullScreen = true;
            TopMost = true;
            Bounds = Screen.FromControl(this).Bounds;
        }
        else
        {
            _fullScreen = false;
            TopMost = false;
            if (WindowState != FormWindowState.Normal)
            {
                WindowState = FormWindowState.Normal;
            }
            Bounds = _fullRestoreBounds;
        }
        captionButtons.RefreshState(); // restore glyph follows fullscreen too
    }

    private void TraceHit(IntPtr lParam, int finalCode)
    {
        long lp = lParam.ToInt64();
        int sx = unchecked((short)(lp & 0xFFFF));
        int sy = unchecked((short)((lp >> 16) & 0xFFFF));
        switch (finalCode)
        {
            case HTMINBUTTON:
            case HTMAXBUTTON:
            case HTCLOSE:
                _htCap++;
                _capSX = sx; _capSY = sy; _capCode = finalCode;
                break;
            case HTCAPTION:
                _htDrag++;
                _dragSX = sx; _dragSY = sy;
                break;
            case HTTOP:
                _htTop++;
                _topSX = sx; _topSY = sy;
                break;
            default:
                _htOther++;
                break;
        }
    }

    private void TrackNcLeave()
    {
        try
        {
            var tme = new TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE | TME_NONCLIENT,
                hwndTrack = Handle,
                dwHoverTime = 0,
            };
            TrackMouseEvent(ref tme);
        }
        catch { /* hover clearing is best-effort (MouseMove net exists too) */ }
    }

    // Maps a SCREEN point onto frame behavior. 0 = keep DefWindowProc's answer.
    // Screen-space throughout: the LParam point is compared against
    // RectangleToScreen rects, so the client origin is never consulted and no
    // origin shift can separate clicks from paint.
    private int HitTestFrame(Point screen)
    {
        if (tabStripPanel.IsDisposed || tabsHost.IsDisposed)
        {
            return 0;
        }
        Rectangle strip;
        try { strip = tabStripPanel.RectangleToScreen(tabStripPanel.ClientRectangle); }
        catch { return 0; }
        if (!strip.Contains(screen))
        {
            return 0; // toolbar/content/borders: default handling
        }
        Rectangle cap = CaptionButtonScreenBounds();
        if (!cap.IsEmpty && cap.Contains(screen))
        {
            int third = Math.Max(1, cap.Width / 3);
            int rel = screen.X - cap.Left;
            return rel < third ? HTMINBUTTON : rel < third * 2 ? HTMAXBUTTON : HTCLOSE;
        }
        if (WindowState == FormWindowState.Normal && !_fullScreen && screen.Y < strip.Top + TopGrip())
        {
            return HTTOP; // slim top-edge resize grip, like Chrome (none in fullscreen)
        }
        if (IsStripDragPointScreen(screen))
        {
            return HTCAPTION; // empty strip background drags the window
        }
        return 0;
    }

    private int TopGrip() => Math.Max(4, (int)Math.Round(6 * DeviceDpi / 96.0));

    // Screen coords of the painted caption cluster (right-aligned in the strip).
    private Rectangle CaptionButtonScreenBounds()
    {
        try
        {
            if (captionButtons == null || captionButtons.IsDisposed)
            {
                return Rectangle.Empty;
            }
            return captionButtons.RectangleToScreen(captionButtons.ClientRectangle);
        }
        catch
        {
            return Rectangle.Empty;
        }
    }

    // Client-coords twin of the above, for the diag only: comparing the two
    // exposes any client-origin shift numerically (see originProbe below).
    private Rectangle CaptionButtonBounds()
    {
        if (captionButtons == null || captionButtons.IsDisposed || tabsHost.IsDisposed)
        {
            return Rectangle.Empty;
        }
        int w = captionButtons.Width;
        int x = tabsHost.Location.X + tabsHost.Width - w;
        return new Rectangle(x, tabStripPanel.Top, w, tabStripPanel.Height);
    }

    // The hit-tested point comes from the message itself, never Cursor.Position:
    // the two can disagree under DPI virtualization, which silently kills dragging.
    // ToInt64 + mask (never ToInt32): LPARAM sign-extends on x64, and negative
    // screen coords must survive the trip.
    private static Point LParamToScreenPoint(IntPtr lParam)
    {
        long lp = lParam.ToInt64();
        return new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
    }

    // Draggable only on empty strip background — never over a tab, the +
    // button, or the caption cluster. Screen-space: each visible tab/+/caption
    // rect vetoes the drag, so no client-origin math is involved.
    private bool IsStripDragPointScreen(Point screen)
    {
        foreach (var t in _tabs)
        {
            try
            {
                if (!t.StripItem.IsDisposed && t.StripItem.Visible &&
                    t.StripItem.RectangleToScreen(t.StripItem.ClientRectangle).Contains(screen))
                {
                    return false;
                }
            }
            catch { /* dying tab: ignore it for this message */ }
        }
        try
        {
            if (btnNewTab.RectangleToScreen(btnNewTab.ClientRectangle).Contains(screen))
            {
                return false;
            }
            if (!captionButtons.IsDisposed &&
                captionButtons.RectangleToScreen(captionButtons.ClientRectangle).Contains(screen))
            {
                return false;
            }
        }
        catch { }
        return true;
    }

    // The caption cluster is Dock.Right inside the host: docking does NOT
    // shrink ClientSize, so tabs/+/layout must subtract its real width
    // explicitly. Unlike the old DWM-guess reserve, this is OUR control's own
    // width — paint, hit-test, and layout share one source of truth.
    private int CaptionReserve() =>
        captionButtons != null && !captionButtons.IsDisposed ? captionButtons.Width : 0;

    private int StripContentWidth() =>
        tabsHost.IsDisposed ? 0 : Math.Max(0, tabsHost.ClientSize.Width - CaptionReserve());

    // ---------- Tabs (Feature 6) ----------

    private BrowserTab CreateTab(string url, bool activate)
    {
        var tab = new BrowserTab(url);
        // Hook: FaviconUrlChanged (CEF thread) -> Feature 5 favicon. Sole reason
        // TabDisplayHandler exists; address/title/loading use the events below.
        tab.Browser.DisplayHandler = new TabDisplayHandler(this, tab);
        // Hook: F11/Escape while the page has focus (CEF thread) -> fullscreen.
        tab.Browser.KeyboardHandler = new BrowserKeyHandler(this);
        // Hook: popups/window.open (CEF thread) -> new tabs via OpenPopupUrl.
        tab.Browser.LifeSpanHandler = new TabLifeSpanHandler(this);
        // Hook: AddressChanged (CEF thread) -> Feature 4 address-bar sync.
        tab.Browser.AddressChanged += Browser_AddressChanged;
        // Hook: TitleChanged (CEF thread) -> Feature 5 title.
        tab.Browser.TitleChanged += Browser_TitleChanged;
        // Hook: FrameLoadEnd on the main frame (CEF thread) -> Feature 5 favicon
        // fallback. Fires even when OnFaviconUrlChange yields nothing usable.
        tab.Browser.FrameLoadEnd += Browser_FrameLoadEnd;
        // Hook: LoadingStateChanged (CEF thread) -> Feature 2 buttons + Feature 3 indicator.
        tab.Browser.LoadingStateChanged += Browser_LoadingStateChanged;

        contentPanel.Controls.Add(tab.Browser);
        BuildStripItem(tab);
        tabsHost.Controls.Add(tab.StripItem);
        tab.StripIcon.Image = DefaultFavicon;
        _tabs.Add(tab);

        if (activate)
        {
            ActivateTab(tab);
        }
        LayoutTabs(); // shrink siblings so the new tab (and +) stays visible
        WriteDiagSnapshot("create");
        return tab;
    }

    private void BuildStripItem(BrowserTab tab)
    {
        var item = new Panel
        {
            Width = 200,
            Height = 26,
            Margin = new Padding(0, 3, 4, 3), // 26px tall, centered in the 32px strip
            BorderStyle = BorderStyle.None, // borderless like Chrome: bg contrast separates tabs
            BackColor = StripBack,
            Tag = tab,
        };
        var icon = new PictureBox
        {
            Size = new System.Drawing.Size(16, 16),
            Location = new System.Drawing.Point(6, 5), // centered in the 26px borderless item
            SizeMode = PictureBoxSizeMode.Zoom,
            Tag = tab,
        };
        var title = new TabTitleLabel
        {
            Location = new System.Drawing.Point(28, 1),
            Size = new System.Drawing.Size(142, 24), // full item height: text vertically centered, never clips
            Text = "New Tab",
            BackColor = StripBack, // explicit opaque dark pairs (see ActivateTab)
            ForeColor = InactiveTitleFore,
            Tag = tab,
        };
        var close = new TabCloseButton
        {
            Size = new System.Drawing.Size(18, 18), // Chrome-balanced × (smaller than the +)
            Location = new System.Drawing.Point(174, 4), // ends at 192, centered vertically
            GlyphColor = TabGlyphLight, // light glyph for the dark tab
            HoverColor = TabHoverDisc,
            Tag = tab,
        };
        close.Click += StripClose_Click;
        item.Click += StripItem_Click;
        icon.Click += StripItem_Click;
        title.Click += StripItem_Click;

        item.Controls.Add(close);
        item.Controls.Add(title);
        item.Controls.Add(icon);
        tab.StripItem = item;
        tab.StripIcon = icon;
        tab.StripTitle = title;
        tab.StripClose = close;
    }

    private const int FullTabWidth = 200;
    private const int MinTabWidth = 56; // Chrome-density minimum; inner tiers below keep icon/× from overlapping
    private const int TabRightMargin = 4; // must match BuildStripItem's right margin
    private const int CloseVisibleWidth = 100; // inactive tabs hide their × below this width, like Chrome
    private const int TitleVisibleWidth = 76; // below this even the title hides (icon-only tabs), so nothing overlaps

    private int _tabScroll; // horizontal scroll offset (px) once tabs pass minimum width
    private int _tabW = FullTabWidth; // current shared tab width, set by LayoutTabs

    // Chrome-style strip: tabs share the host width and shrink to the minimum;
    // past that they overflow at full height (no scrollbar control ever appears)
    // and scroll with the wheel, with the active tab and the + kept visible.
    // Total width math includes each tab's right margin, or the margins alone
    // overflow the strip.
    private void LayoutTabs()
    {
        if (tabsHost.IsDisposed || _tabs.Count == 0)
        {
            return;
        }
        int hostW = StripContentWidth();
        if (hostW <= 0)
        {
            return;
        }
        int plusW = btnNewTab.Width + btnNewTab.Margin.Horizontal;
        int avail = hostW - plusW;
        if (avail <= 0)
        {
            return;
        }
        int w = Math.Max(MinTabWidth, Math.Min(FullTabWidth, (avail - _tabs.Count * TabRightMargin) / _tabs.Count));
        _tabW = w;
        int totalTabsW = _tabs.Count * (w + TabRightMargin);
        int maxScroll = Math.Max(0, totalTabsW + plusW - hostW);
        _tabScroll = Math.Max(0, Math.Min(_tabScroll, maxScroll));
        int x = 4 - _tabScroll; // 4px left pad, Chrome-style
        foreach (var t in _tabs)
        {
            if (t.StripItem.IsDisposed)
            {
                continue;
            }
            t.StripItem.Location = new System.Drawing.Point(x, 3);
            t.StripItem.Width = w;
            // Dynamic ×: the active tab always keeps it; inactive tabs drop it
            // once narrow. Below TitleVisibleWidth the title hides too, so the
            // icon and × can never overlap at extreme densities.
            bool showClose = ReferenceEquals(t, _activeTab) || w >= CloseVisibleWidth;
            bool showTitle = w >= TitleVisibleWidth;
            t.StripClose.Visible = showClose;
            t.StripClose.Location = new System.Drawing.Point(w - 26, 4);
            t.StripTitle.Visible = showTitle;
            t.StripTitle.Width = Math.Max(8, showClose ? w - 58 : w - 36); // always refresh, even hidden
            x += w + TabRightMargin;
        }
        // The + stays right after the last tab but pinned inside the strip,
        // so it never scrolls out of reach.
        int plusX = Math.Max(2, Math.Min(x + 2, hostW - btnNewTab.Width));
        btnNewTab.Location = new System.Drawing.Point(plusX, 3);
    }

    // Scrolls just enough to bring the tab fully into view (leaving the +
    // visible), then re-layouts. Called on activation/selection.
    private void EnsureTabVisible(BrowserTab tab)
    {
        int i = _tabs.IndexOf(tab);
        if (i < 0 || tabsHost.IsDisposed)
        {
            return;
        }
        int hostW = StripContentWidth();
        int plusW = btnNewTab.Width + btnNewTab.Margin.Horizontal;
        int viewW = Math.Max(0, hostW - plusW);
        int tabL = 4 + i * (_tabW + TabRightMargin);
        int tabR = tabL + _tabW;
        if (tabL < _tabScroll)
        {
            _tabScroll = tabL;
        }
        else if (tabR > _tabScroll + viewW)
        {
            _tabScroll = Math.Max(0, tabR - viewW);
        }
        LayoutTabs();
    }

    private bool NeedsTabScroll()
    {
        if (tabsHost.IsDisposed)
        {
            return false;
        }
        int hostW = StripContentWidth();
        int plusW = btnNewTab.Width + btnNewTab.Margin.Horizontal;
        return _tabs.Count * (_tabW + TabRightMargin) + plusW > hostW;
    }

    // TEMPORARY diagnostic snapshot (%TEMP%\krypton-diag.txt, overwritten on
    // every tab open/close/activate): proves which binary is running (exe
    // timestamp + title control type) and dumps live tab/chrome state for a
    // report we cannot reproduce locally. REMOVE once that report is resolved.
    // Never throws: diagnostics must not break the app.
    private void WriteDiagSnapshot(string why)
    {
        try
        {
            var sb = new StringBuilder();
            string exe = typeof(MainForm).Assembly.Location;
            sb.AppendLine($"when={DateTime.UtcNow:O} why={why}");
            sb.AppendLine($"exe={exe}");
            try { sb.AppendLine($"exeWriteTimeUtc={File.GetLastWriteTimeUtc(exe):O}"); } catch { }
            sb.AppendLine($"dpi={DeviceDpi} state={WindowState} bounds={Bounds}");
            sb.AppendLine("frame=chrome-style (NCCALCSIZE, owner-drawn caption, no DWM glass)");
            sb.AppendLine($"frameChrome darkModeHr={_darkModeHr} borderColorHr={_borderColorHr} borderReadback={ReadBorderColor()}");
            sb.AppendLine($"frameTop topNC={_lastTopNC}");
            sb.AppendLine($"theme Window={Argb(SystemColors.Window)} WindowText={Argb(SystemColors.WindowText)} Control={Argb(SystemColors.Control)} ControlText={Argb(SystemColors.ControlText)}");
            sb.AppendLine($"captionMetrics button={SystemInformation.CaptionButtonSize.Width}x{SystemInformation.CaptionButtonSize.Height} captionH={SystemInformation.CaptionHeight}");
            sb.AppendLine($"handleCreated={IsHandleCreated} visible={Visible} client={ClientSize.Width}x{ClientSize.Height}");
            try
            {
                sb.AppendLine($"tabStrip panel={R(tabStripPanel.Bounds)} pvis={tabStripPanel.Visible} hostParent={R(tabsHost.Bounds)} hvis={tabsHost.Visible}");
            }
            catch { }
            sb.AppendLine($"strip host={R(tabsHost.Bounds)} reserve={CaptionReserve()} tabW={_tabW} scroll={_tabScroll} count={_tabs.Count}");
            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                sb.AppendLine($"tab{i} active={ReferenceEquals(t, _activeTab)} item={R(t.StripItem.Bounds)} vis={t.StripItem.Visible} " +
                    $"titleType={t.StripTitle.GetType().Name} title=\"{t.StripTitle.Text}\" tb={R(t.StripTitle.Bounds)} tvis={t.StripTitle.Visible} fg={Argb(t.StripTitle.ForeColor)} bg={Argb(t.StripTitle.BackColor)} font={t.StripTitle.Font.Name},{t.StripTitle.Font.SizeInPoints} " +
                    $"close={R(t.StripClose.Bounds)} cvis={t.StripClose.Visible}");
            }
            sb.AppendLine($"plus={R(btnNewTab.Bounds)} vis={btnNewTab.Visible}");
            sb.AppendLine($"caption btnW={captionButtons.ButtonWidth} totalW={captionButtons.Width} capBounds={R(CaptionButtonBounds())}");
            sb.AppendLine($"hitTest cap={_htCap} drag={_htDrag} top={_htTop} other={_htOther}");
            sb.AppendLine($"hitLastCap scr=({_capSX},{_capSY}) code={_capCode}");
            sb.AppendLine($"hitLastDrag scr=({_dragSX},{_dragSY})");
            sb.AppendLine($"hitLastTop scr=({_topSX},{_topSY})");
            try
            {
                // Smoking-gun line: the strip center in screen coords, mapped
                // back through PointToClient. Correct = the client center
                // (w/2,h/2). A shortfall of ~captionH here is the offset bug.
                Rectangle sc = tabStripPanel.RectangleToScreen(tabStripPanel.ClientRectangle);
                Point back = PointToClient(new Point(sc.Left + sc.Width / 2, sc.Top + sc.Height / 2));
                sb.AppendLine($"originProbe stripCenterScr=({sc.Left + sc.Width / 2},{sc.Top + sc.Height / 2}) -> cli=({back.X},{back.Y}) expectCli=({tabStripPanel.ClientRectangle.Width / 2},{tabStripPanel.ClientRectangle.Height / 2})");
                sb.AppendLine($"capScreen={R(CaptionButtonScreenBounds())}");
            }
            catch (Exception ex) { sb.AppendLine($"originProbe ERROR {ex.GetType().Name}"); }
            sb.AppendLine($"scale dpi={DeviceDpi} auto={AutoScaleDimensions.Width}x{AutoScaleDimensions.Height}->{CurrentAutoScaleDimensions.Width}x{CurrentAutoScaleDimensions.Height} captionH={SystemInformation.CaptionHeight} client={ClientSize.Width}x{ClientSize.Height} strip={R(tabStripPanel.Bounds)} host={R(tabsHost.Bounds)}");
            sb.AppendLine($"titlePaint paints={TabTitleLabel.Paints} err={TabTitleLabel.LastPaintError ?? "none"}");
            if (_activeTab != null)
            {
                sb.AppendLine($"fav note=\"{_activeTab.FaviconNote}\"");
            }
            sb.AppendLine($"address=\"{addressBar.Text}\"");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "krypton-diag.txt"), sb.ToString());
        }
        catch { }
    }

    private static string Argb(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    private static string R(Rectangle r) => $"{r.X},{r.Y},{r.Width}x{r.Height}";

    private void ActivateTab(BrowserTab tab)
    {
        _activeTab = tab;
        foreach (var t in _tabs)
        {
            bool active = ReferenceEquals(t, tab);
            t.Browser.Visible = active;
            // Explicit opaque dark pairs (never theme-driven): the strip is
            // always dark, so a theme bg/fg pair could wash out. Both are set
            // explicitly because TabTitleLabel is owner-drawn.
            t.StripItem.BackColor = active ? ActiveTabBack : StripBack;
            t.StripTitle.BackColor = t.StripItem.BackColor;
            t.StripTitle.ForeColor = active ? ActiveTitleFore : InactiveTitleFore;
            t.StripTitle.Invalidate(); // explicit: color-pair swap must repaint the owner-drawn text
            t.StripItem.Invalidate(true);
        }
        tab.Browser.BringToFront();

        SetAddressBarText(IsNewTabUrl(tab.Url) ? string.Empty : tab.Url); // Chrome-like empty omnibox on new tabs
        UpdateNavButtons();
        UpdateLoadingIndicator();
        RefreshActiveTitleFavicon();
        EnsureTabVisible(tab); // scrolled-out tabs come back into view on selection
        WriteDiagSnapshot("activate");
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        bool wasActive = ReferenceEquals(tab, _activeTab);
        _tabs.RemoveAt(index);
        tabsHost.Controls.Remove(tab.StripItem);
        contentPanel.Controls.Remove(tab.Browser);
        tab.StripItem.Dispose();
        tab.Browser.Dispose(); // undisposed browsers leak CEF subprocesses
        if (tab.Favicon != null && !ReferenceEquals(tab.Favicon, DefaultFavicon))
        {
            tab.Favicon.Dispose();
        }

        if (wasActive)
        {
            _activeTab = null;
            if (_tabs.Count == 0)
            {
                Close(); // closing the last tab exits the application
                return;
            }
            ActivateTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
        LayoutTabs(); // freed space goes back to the remaining tabs
        WriteDiagSnapshot("close");
    }

    private void DisposeAllTabs()
    {
        foreach (var tab in _tabs.ToArray())
        {
            try { contentPanel.Controls.Remove(tab.Browser); } catch { }
            try { tab.Browser.Dispose(); } catch { }
            try { tab.StripItem.Dispose(); } catch { }
            if (tab.Favicon != null && !ReferenceEquals(tab.Favicon, DefaultFavicon))
            {
                try { tab.Favicon.Dispose(); } catch { }
            }
        }
        _tabs.Clear();
        _activeTab = null;
    }

    private BrowserTab? FindTab(object? sender) =>
        sender is IWebBrowser wb ? _tabs.FirstOrDefault(t => ReferenceEquals(t.Browser, wb)) : null;

    // ---------- Feature 1: address-bar navigation ----------

    private string? NavigateToInput(string input)
    {
        if (_activeTab == null)
        {
            return null;
        }
        string? url = ResolveInputToUrl(input);
        if (url != null)
        {
            _activeTab.Browser.Load(url);
        }
        return url;
    }

    // Moves keyboard focus to the active page (Chrome parity: committing the
    // omnibox or hitting a toolbar button defocuses it). This is what re-arms
    // first-click select-all via Leave — without it, focus gets stuck in the
    // box after Enter-navigation and the next click finds an already-focused
    // box that (correctly, but surprisingly) only places the caret.
    private void FocusPage()
    {
        var tab = _activeTab;
        if (tab != null && !tab.Browser.IsDisposed && tab.Browser.IsHandleCreated)
        {
            try
            {
                tab.Browser.Focus();
            }
            catch
            {
                // Focus loss during teardown — nothing to do.
            }
        }
    }

    // No scheme -> prepend https://; not URL-shaped -> search query.
    internal static string? ResolveInputToUrl(string input)
    {
        string s = input.Trim();
        if (s.Length == 0)
        {
            return null;
        }
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("krypton://", StringComparison.OrdinalIgnoreCase) ||
            HasKnownScheme(s))
        {
            return s;
        }
        if (IsLikelyUrl(s))
        {
            string scheme = s.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "http://" : "https://";
            return scheme + s;
        }
        return SearchUrl + Uri.EscapeDataString(s);
    }

    // Any other explicit scheme (file://, view-source:, ...) passes through
    // untouched instead of being mangled into a search or https:// URL.
    private static bool HasKnownScheme(string s)
    {
        int colon = s.IndexOf(':');
        if (colon <= 0 || s.Contains(' '))
        {
            return false;
        }
        for (int i = 0; i < colon; i++)
        {
            char c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.'))
            {
                return false;
            }
        }
        return char.IsLetter(s[0]);
    }

    private static bool IsLikelyUrl(string s)
    {
        if (s.Contains(' '))
        {
            return false;
        }
        string hostPort = s.Split(['/', '?', '#'], 2)[0];
        string host = hostPort.Split(':')[0];
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               hostPort.Contains(':') || // host:port, e.g. myhost:3000
               (host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.'));
    }

    // ---------- CEF event handlers (all on CEF threads -> Ui()) ----------

    private void Browser_AddressChanged(object? sender, AddressChangedEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab == null)
        {
            return;
        }
        bool hostChanged = !SameHost(tab.Url, e.Address);
        tab.Url = e.Address;
        Ui(() =>
        {
            if (ReferenceEquals(tab, _activeTab))
            {
                // Feature 4: sync on link clicks etc. (empty omnibox on new tabs, like Chrome)
                SetAddressBarText(IsNewTabUrl(e.Address) ? string.Empty : e.Address);
            }
            tab.StripTitle.Text = ShortTitle(tab);
            if (hostChanged)
            {
                tab.FaviconSource = string.Empty; // new page -> allow a fresh fetch
                SetTabFavicon(tab, DefaultFavicon, disposeOld: true);
            }
        });
    }

    private void Browser_TitleChanged(object? sender, TitleChangedEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab == null)
        {
            return;
        }
        tab.Title = IsNewTabUrl(tab.Url) ? "New Tab"
            : string.IsNullOrWhiteSpace(e.Title) ? tab.Url : e.Title;
        Ui(() =>
        {
            tab.StripTitle.Text = ShortTitle(tab);
            if (ReferenceEquals(tab, _activeTab))
            {
                Text = $"{tab.Title} - Krypton";
            }
        });
    }

    private void Browser_LoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab == null)
        {
            return;
        }
        tab.IsLoading = e.IsLoading;
        tab.CanGoBack = e.CanGoBack;
        tab.CanGoForward = e.CanGoForward;
        Ui(() =>
        {
            if (ReferenceEquals(tab, _activeTab))
            {
                UpdateNavButtons();      // Feature 2: enable/disable Back/Forward
                UpdateLoadingIndicator(); // Feature 3: active tab's state only
            }
        });
    }

    // ---------- Feature 5: favicon ----------

    private const int MaxFaviconCandidates = 5;

    // Called from TabDisplayHandler.OnFaviconUrlChange on a CEF thread.
    internal void OnFaviconUrls(BrowserTab tab, IList<string> urls)
    {
        var httpUrls = urls
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Take(MaxFaviconCandidates)
            .ToList();
        RequestFavicon(tab, httpUrls);
    }

    private void Browser_FrameLoadEnd(object? sender, FrameLoadEndEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab == null || e.Frame is not { IsMain: true })
        {
            return;
        }
        if (!e.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        // Only successful loads teach the completion engine — a committed
        // navigation that fails (DNS, 404, offline) must not pollute history.
        if (e.HttpStatusCode is >= 200 and <= 399)
        {
            try
            {
                string host = new Uri(e.Url).Host;
                if (!string.IsNullOrEmpty(host))
                {
                    AddToHistory(host);
                }
            }
            catch { /* non-absolute URL -> nothing to learn */ }
        }
        RequestFavicon(tab, []); // no reported urls -> derive from page origin
    }

    private void RequestFavicon(BrowserTab tab, List<string> urls)
    {
        // Keyed on the whole candidate set: the same page re-reporting the
        // same icons never refetches, while a changed set (navigation, late
        // icons) triggers a fresh pick.
        string key = urls.Count > 0 ? "fav:" + string.Join("|", urls) : ("origin:" + tab.Url);
        if (tab.FaviconSource == key)
        {
            return; // already fetched/fetching for this source
        }
        tab.FaviconSource = key;
        Debug.WriteLine($"[Krypton] favicon fetch: page={tab.Url} source={key}");
        if (TryBrandOverride(tab))
        {
            return; // explicit product call, no network needed
        }
        _ = FetchFaviconAsync(tab, urls);
    }

    // Explicit brand overrides: pages whose <head> branding differs from the
    // mark users recognize (Google's Gmail marketing pages declare only the
    // 20px G). ONE entry per deliberate product decision — delete the entry
    // to restore strict Chrome parity. This is not site detection and not a
    // table to maintain: no crawling, no hunting, nothing else reads it.
    internal static string? BrandOverrideArt(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        bool google = host == "google.com" || host.EndsWith(".google.com", StringComparison.Ordinal)
            || host == "gmail.com";
        if (!google)
        {
            return null;
        }
        if (host == "gmail.com" || host == "mail.google.com"
            || uri.AbsolutePath.Contains("/gmail/", StringComparison.OrdinalIgnoreCase))
        {
            return "Krypton.gmail-dial.png";
        }
        return null;
    }

    // Applies the override art (a per-tab clone of the shared original).
    // False when unavailable: the caller falls through to the network path.
    private bool TryBrandOverride(BrowserTab tab)
    {
        Image? artBase = BrandGmail;
        if (artBase == null)
        {
            return false;
        }
        string? art;
        try
        {
            art = BrandOverrideArt(new Uri(tab.Url));
        }
        catch
        {
            return false;
        }
        if (art == null)
        {
            return false;
        }
        Image clone;
        try
        {
            clone = new Bitmap(artBase);
        }
        catch
        {
            return false;
        }
        Debug.WriteLine($"[Krypton] favicon brand-override: page={tab.Url} art={art}");
        ApplyFavicon(tab, clone, "brand-override " + art);
        return true;
    }

    private async Task FetchFaviconAsync(BrowserTab tab, List<string> urls)
    {
        var candidates = new List<string>(urls);
        try // Fallback derived from navigation state when the page supplies none.
        {
            var uri = new Uri(tab.Url);
            if (uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add($"{uri.Scheme}://{uri.Host}/favicon.ico");
            }
        }
        catch { /* non-absolute URL (about:blank) -> default icon below */ }

        // Best-mark pick: pages often declare several icons (a 16px badge, a
        // 192px flagship, apple-touch variants...), and first-listed is
        // frequently the smallest. Decode them all and keep the LARGEST —
        // that is almost always the site's representative mark (Gmail's M
        // over a 16px G). Ties keep the earlier (page-preferred) candidate.
        Image? best = null;
        int bestArea = 0;
        string? winner = null;
        var distinct = candidates.Distinct().ToList();
        foreach (string url in distinct)
        {
            try
            {
                byte[] bytes = await FaviconHttp.GetByteArrayAsync(url).ConfigureAwait(false);
                Image? img = DecodeImage(bytes);
                if (img == null)
                {
                    Debug.WriteLine($"[Krypton] favicon decode failed: {url} ({bytes.Length} bytes)");
                    continue;
                }
                int area = img.Width * img.Height;
                if (best == null || area > bestArea)
                {
                    best?.Dispose();
                    best = img;
                    bestArea = area;
                    winner = url;
                }
                else
                {
                    img.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Krypton] favicon download failed: {url} ({ex.Message})");
            }
        }
        string note = best == null
            ? $"default ({distinct.Count} failed)"
            : $"{best.Width}x{best.Height} {winner} ({distinct.Count} candidates)";
        ApplyFavicon(tab, best, note); // null -> generic default icon, never blank
    }

    private static Image? DecodeImage(byte[] bytes)
    {
        // ICO containers hold SEVERAL frames (a 16px badge first and the
        // 256px flagship last is the classic layout): generic decoders return
        // an arbitrary one of them, so pick the largest PNG frame explicitly.
        Image? ico = DecodeIcoBestFrame(bytes);
        if (ico != null)
        {
            return ico;
        }
        try
        {
            using var ms = new MemoryStream(bytes);
            using var tmp = Image.FromStream(ms);
            return new Bitmap(tmp); // detach from stream (GDI+ keeps a ref otherwise)
        }
        catch { }
        try
        {
            using var ms = new MemoryStream(bytes);
            using var icon = new Icon(ms);
            return icon.ToBitmap(); // legacy single-frame (BMP) icons
        }
        catch
        {
            return null;
        }
    }

    private static bool IsIcoFile(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0;

    // Largest PNG-compressed frame in the ICO directory (ICO frames are raw
    // PNGs with no extra header, so each decodes standalone). BMP frames are
    // skipped — legacy small sizes only — and the Icon-class fallback above
    // still handles single-frame BMP icons. Returns null when unusable, in
    // which case decoding continues with the generic paths. Never throws.
    private static Image? DecodeIcoBestFrame(byte[] bytes)
    {
        try
        {
            if (!BitConverter.IsLittleEndian || !IsIcoFile(bytes) || bytes.Length < 6 + 16)
            {
                return null;
            }
            int count = BitConverter.ToUInt16(bytes, 4);
            if (count <= 0 || count > 32 || bytes.Length < 6 + 16 * count)
            {
                return null;
            }
            int bestOffset = -1, bestLen = 0, bestArea = 0;
            for (int i = 0; i < count; i++)
            {
                int off = 6 + 16 * i;
                int w = bytes[off] == 0 ? 256 : bytes[off];
                int h = bytes[off + 1] == 0 ? 256 : bytes[off + 1];
                int len = BitConverter.ToInt32(bytes, off + 8);
                int data = BitConverter.ToInt32(bytes, off + 12);
                if (len <= 0 || data <= 0 || data + len > bytes.Length)
                {
                    continue;
                }
                // PNG magic (Vista+ icons); BMP frames start with 0x28 instead.
                bool png = len > 8 && bytes[data] == 0x89 && bytes[data + 1] == 0x50
                    && bytes[data + 2] == 0x4E && bytes[data + 3] == 0x47;
                if (!png)
                {
                    continue;
                }
                if (w * h > bestArea)
                {
                    bestArea = w * h;
                    bestOffset = data;
                    bestLen = len;
                }
            }
            if (bestOffset < 0)
            {
                return null;
            }
            using var ms = new MemoryStream(bytes, bestOffset, bestLen, writable: false);
            using var tmp = Image.FromStream(ms);
            return new Bitmap(tmp);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyFavicon(BrowserTab tab, Image? img, string note)
    {
        Ui(() =>
        {
            tab.FaviconNote = note; // diag trace: what won and where from
            if (tab.Browser.IsDisposed)
            {
                if (img != null)
                {
                    img.Dispose();
                }
                return;
            }
            SetTabFavicon(tab, img ?? DefaultFavicon, disposeOld: true);
        });
    }

    private void SetTabFavicon(BrowserTab tab, Image img, bool disposeOld)
    {
        Image? old = tab.Favicon;
        tab.Favicon = img;
        tab.StripIcon.Image = img;
        tab.StripIcon.Invalidate();
        if (ReferenceEquals(tab, _activeTab))
        {
            faviconBox.Image = img;
            faviconBox.Invalidate();
        }
        if (disposeOld && old != null && !ReferenceEquals(old, DefaultFavicon))
        {
            old.Dispose();
        }
    }

    // ---------- Toolbar refresh from active tab ----------

    private const int OmniboxHeight = 28;

    // A Dock=Fill single-line TextBox stretches and top-aligns its text,
    // which clipped/misaligned the omnibox. Fixed height, centered on the
    // toolbar midline — same midline as the Kr logo — so the text is fully
    // visible and vertically centered.
    private void LayoutToolbar()
    {
        if (toolbarPanel.IsDisposed || addressBar.IsDisposed || faviconBox.IsDisposed)
        {
            return;
        }
        int left = faviconBox.Right + faviconBox.Margin.Right;
        int top = Math.Max(0, (toolbarPanel.ClientSize.Height - OmniboxHeight) / 2);
        int width = Math.Max(0, toolbarPanel.ClientSize.Width - toolbarPanel.Padding.Right - left);
        addressBar.SetBounds(left, top, width, OmniboxHeight);
    }

    private void ToolbarPanel_Resize(object? sender, EventArgs e) => LayoutToolbar();

    private void UpdateNavButtons()
    {
        btnBack.Enabled = _activeTab?.CanGoBack == true;
        btnForward.Enabled = _activeTab?.CanGoForward == true;
        btnRefresh.Enabled = _activeTab != null;
        btnStop.Enabled = _activeTab?.IsLoading == true; // greyed unless a page is loading
        btnHome.Enabled = _activeTab != null;
    }

    private void UpdateLoadingIndicator()
    {
        loadingStrip.Visible = _activeTab?.IsLoading == true;
    }

    private void RefreshActiveTitleFavicon()
    {
        if (_activeTab == null)
        {
            Text = "Krypton";
            return;
        }
        _activeTab.StripTitle.Text = ShortTitle(_activeTab);
        Text = $"{_activeTab.Title} - Krypton";
        faviconBox.Image = _activeTab.Favicon ?? DefaultFavicon;
    }

    private static string ShortTitle(BrowserTab tab) =>
        IsNewTabUrl(tab.Url) ? "New Tab" :
        string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title;

    private static bool IsNewTabUrl(string url) =>
        url.TrimEnd('/').Equals(NewTabUrl, StringComparison.OrdinalIgnoreCase); // CEF may report a trailing slash

    private static bool SameHost(string a, string b)
    {
        try { return new Uri(a).Host.Equals(new Uri(b).Host, StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.Ordinal); }
    }

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch (ObjectDisposedException) { }
        }
        else
        {
            action();
        }
    }

    // ---------- Plain UI events (already on UI thread) ----------

    // ---------- Omnibox behavior: select-all on focus + inline history completion ----------

    private static readonly string[] SeedHosts = new[]
    {
        NewTabUrl,
        "github.com", "youtube.com", "gmail.com", "google.com", "chatgpt.com",
        "twitter.com", "linkedin.com", "wikipedia.org", "reddit.com", "amazon.com",
        "netflix.com", "discord.com", "twitch.tv", "stackoverflow.com", "yahoo.com",
        "microsoft.com", "apple.com", "facebook.com", "instagram.com", "kryptonbrowser.com",
    };
    private readonly List<string> _history = new(SeedHosts);
    private readonly object _historyLock = new();
    private HistoryStore? _historyStore; // SQLite cache; null when unavailable (seeds still work)
    private const int MaxHistory = 100;

    private bool _selectAllArmed = true; // set by Leave: the next focus-in may select all
    private bool _pendingClickSelect;      // focusing click in progress; consumed by MouseUp
    private Point _mouseDownPos;           // distinguishes click (select) from drag (keep)
    private bool _syncingOmnibox;          // programmatic Text set (never complete)
    private bool _completing;              // reentrancy guard for our own Text set
    private bool _completionActive;        // a suggested tail is currently selected
    private int _typedLen;                 // length of the user-typed prefix
    private string _lastText = string.Empty; // full text after last processing

    private void AddressBar_Enter(object? sender, EventArgs e)
    {
        if (addressBar.IsDisposed)
        {
            return;
        }
        if (Control.MouseButtons == MouseButtons.None)
        {
            // Keyboard/programmatic focus: no click in flight, so select
            // immediately. (Posting SelectAll from here loses to the click's
            // own MouseUp, which is why the first click used to need a second
            // one — click-focus goes through MouseDown/MouseUp below instead.)
            if (_selectAllArmed)
            {
                _selectAllArmed = false;
                addressBar.SelectAll();
            }
            return;
        }
        // Click-focus backup: if MouseDown ran while disarmed (focus left for
        // the page without a Leave ever firing), we still genuinely just
        // gained focus to this click — arm the MouseUp selection here using
        // the live cursor position.
        _pendingClickSelect = true;
        _mouseDownPos = addressBar.PointToClient(Control.MousePosition);
    }

    private void AddressBar_Leave(object? sender, EventArgs e)
    {
        _selectAllArmed = true;
        _pendingClickSelect = false;
    }

    private void AddressBar_MouseDown(object? sender, MouseEventArgs e)
    {
        // Focusing click: defer selection to MouseUp (the last event of the
        // click), so nothing after it can collapse the highlight.
        if ((e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) && _selectAllArmed)
        {
            _selectAllArmed = false;
            _pendingClickSelect = true;
            _mouseDownPos = e.Location;
        }
    }

    private void AddressBar_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_pendingClickSelect)
        {
            return;
        }
        _pendingClickSelect = false;
        if (addressBar.IsDisposed)
        {
            return;
        }
        // Pure click (no drag): select all. A drag keeps the user's own
        // selection, and clicks on an already-focused bar only move the caret.
        Size drag = SystemInformation.DragSize;
        if ((e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) &&
            Math.Abs(e.X - _mouseDownPos.X) < drag.Width &&
            Math.Abs(e.Y - _mouseDownPos.Y) < drag.Height)
        {
            addressBar.SelectAll();
        }
    }

    private void SetAddressBarText(string text)
    {
        _syncingOmnibox = true;
        try
        {
            addressBar.Text = text;
        }
        finally
        {
            _syncingOmnibox = false;
        }
        _typedLen = text.Length;
        _lastText = text;
        _completionActive = false;
    }

    private void AddressBar_TextChanged(object? sender, EventArgs e)
    {
        if (_completing || _syncingOmnibox)
        {
            return;
        }
        string text = addressBar.Text;
        if (!addressBar.Focused)
        {
            _typedLen = text.Length;
            _lastText = text;
            _completionActive = false;
            return;
        }
        int caret = addressBar.SelectionStart;
        string oldTyped = _lastText.Substring(0, Math.Min(_typedLen, _lastText.Length));
        if (caret < _typedLen || text == oldTyped)
        {
            // Deletion, caret move, or dismissed tail: track, never complete.
            _typedLen = caret;
            _lastText = text;
            _completionActive = false;
            return;
        }
        _typedLen = caret;
        string typed = text.Substring(0, Math.Min(caret, text.Length));
        string? completed = FindHistoryMatch(typed);
        if (completed == null)
        {
            _lastText = text;
            _completionActive = false;
            return;
        }
        _completing = true;
        try
        {
            addressBar.Text = completed;
            addressBar.Select(typed.Length, completed.Length - typed.Length);
            _lastText = completed;
            _completionActive = true;
        }
        finally
        {
            _completing = false;
        }
    }

    // First history entry that extends the typed prefix (full-text match
    // first, so krypton://new-tab completes; else host match past a scheme).
    private string? FindHistoryMatch(string typed)
    {
        if (typed.Length == 0)
        {
            return null;
        }
        lock (_historyLock)
        {
            string? full = _history.FirstOrDefault(h =>
                h.Length > typed.Length &&
                h.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
            if (full != null)
            {
                return full;
            }
            string needle = typed;
            int scheme = typed.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                needle = typed[(scheme + 3)..];
            }
            if (needle.Length == 0 || needle.Contains(' ') || needle.Contains('/'))
            {
                return null;
            }
            string? hit = _history
                .Where(h => !h.Contains("://"))
                .FirstOrDefault(h =>
                    h.Length > needle.Length &&
                    h.StartsWith(needle, StringComparison.OrdinalIgnoreCase));
            return hit == null ? null : typed.Substring(0, typed.Length - needle.Length) + hit;
        }
    }

    // Called on CEF threads (FrameLoadEnd): the DB UNIQUE constraint absorbs
    // duplicates; the lock serializes memory + store access.
    private void AddToHistory(string host)
    {
        lock (_historyLock)
        {
            try
            {
                _historyStore?.Upsert(host);
            }
            catch { /* memory stays authoritative for completion */ }
            if (_history.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            if (_history.Count >= MaxHistory && _history.Count > SeedHosts.Length)
            {
                _history.RemoveAt(SeedHosts.Length); // drop oldest user entry, keep seeds
            }
            _history.Add(host);
        }
    }

    private static string HistoryDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Krypton", "history.db");

    private static string LegacyHistoryFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Krypton", "history.txt");

    // SQLite history cache: seeds + learned hosts, UNIQUE on domain.
    // Best-effort: any failure leaves the in-memory seeds working.
    private void LoadHistory()
    {
        try
        {
            var store = new HistoryStore(HistoryDbPath);
            lock (_historyLock)
            {
                _historyStore = store;
                store.Seed(SeedHosts);
                MigrateLegacyHistoryFile();
                _history.Clear();
                _history.AddRange(store.LoadAll());
            }
        }
        catch { /* store unavailable -> in-memory seeds stay */ }
    }

    // One-time migration from the old flat file: import, then delete it so we
    // never read it again. Failures leave the file for the next launch.
    // Called with the history lock held.
    private void MigrateLegacyHistoryFile()
    {
        if (_historyStore == null || !File.Exists(LegacyHistoryFilePath))
        {
            return;
        }
        try
        {
            var lines = File.ReadAllLines(LegacyHistoryFilePath)
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.Contains(' '));
            _historyStore.ImportLines(lines);
            File.Delete(LegacyHistoryFilePath);
        }
        catch { /* leave the file for next launch */ }
    }

    private void AddressBar_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        // Tab is a dialog/focus key: the framework consumes it for focus
        // cycling BEFORE KeyDown fires, so without claiming it here the
        // completion-accept branch below never runs and focus leaks out of
        // the omnibox. Only claimed while a suggestion is actually showing —
        // otherwise Tab keeps moving focus normally.
        if (e.KeyCode == Keys.Tab && _completionActive && !addressBar.IsDisposed && addressBar.SelectionLength > 0)
        {
            e.IsInputKey = true;
        }
    }

    private void AddressBar_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Tab && _completionActive && addressBar.SelectionLength > 0)
        {
            addressBar.SelectionStart = addressBar.Text.Length; // accept: collapse to end
            _typedLen = addressBar.Text.Length;
            _lastText = addressBar.Text;
            _completionActive = false;
            e.Handled = true;
            e.SuppressKeyPress = true; // keep focus in the omnibox
            return;
        }
        if (e.KeyCode == Keys.Escape && _completionActive && addressBar.SelectionLength > 0)
        {
            string reverted = addressBar.Text.Substring(0, addressBar.SelectionStart);
            _completing = true;
            try
            {
                addressBar.Text = reverted;
                addressBar.SelectionStart = reverted.Length;
            }
            finally
            {
                _completing = false;
            }
            _typedLen = reverted.Length;
            _lastText = reverted;
            _completionActive = false;
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Enter)
        {
            _completionActive = false; // Text already holds the full (completed) string
            if (NavigateToInput(addressBar.Text) != null)
            {
                FocusPage();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    // F11 toggles fullscreen from anywhere the FORM sees keys (omnibox,
    // toolbar buttons). Page focus belongs to CEF's separate HWND, so the
    // browser gets its own handler (BrowserKeyHandler) for the same keys.
    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Escape && _fullScreen && !addressBar.Focused)
        {
            SetFullScreen(false); // omnibox Esc stays with completion revert
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void BtnBack_Click(object? sender, EventArgs e)
    {
        _activeTab?.Browser.Back();
        FocusPage();
    }

    private void BtnForward_Click(object? sender, EventArgs e)
    {
        _activeTab?.Browser.Forward();
        FocusPage();
    }

    private void BtnRefresh_Click(object? sender, EventArgs e)
    {
        _activeTab?.Browser.Reload();
        FocusPage();
    }
    private void BtnStop_Click(object? sender, EventArgs e)
    {
        var tab = _activeTab;
        if (tab == null)
        {
            return;
        }
        if (!tab.IsLoading)
        {
            // Stale click (the button is disabled when idle): re-sync the
            // buttons but never navigate — a double-clicked Stop must not
            // skip the user back two pages.
            if (ReferenceEquals(tab, _activeTab))
            {
                UpdateNavButtons();
                UpdateLoadingIndicator();
            }
            return;
        }
        tab.Browser.Stop();
        // Fallback redirect: an aborted load leaves a blank/partial canvas.
        // History behind it -> the aborted load wasn't the first page, so go
        // back to the previous rendered page. No history -> the load WAS the
        // first page: pivot to the new-tab dashboard instead of stranding a
        // blank view. The new navigation's own events drive loading state on.
        if (tab.Browser.CanGoBack)
        {
            tab.Browser.Back(); // CefSharp names it Back(), not GoBack()
        }
        else
        {
            tab.Browser.Load(NewTabUrl);
        }
        // Nudge a fresh paint of the view surface after the abort so no
        // half-rendered frame lingers on the canvas.
        tab.Browser.Invalidate();
        // Visibility toggle: forces the full hide/show paint cycle (CefSharp
        // relays it to the renderer via WasHidden), so a dropped frame can't
        // leave a dead canvas. Z-order is preserved; the page stays loaded
        // and interactive — only the load was aborted, not the view.
        tab.Browser.Visible = false;
        tab.Browser.Visible = true;
        FocusPage();
        // CEF doesn't always deliver a final LoadingStateChanged after Stop():
        // without this the tab model stays "loading" forever (stuck indicator,
        // stop never greys out — the "frozen" look). A later real event still
        // corrects the state, so this reset can't desync anything. The address
        // text is deliberately left alone (Chrome keeps in-progress typing too).
        tab.IsLoading = false;
        tab.CanGoBack = tab.Browser.CanGoBack;
        tab.CanGoForward = tab.Browser.CanGoForward;
        if (ReferenceEquals(tab, _activeTab))
        {
            UpdateNavButtons();
            UpdateLoadingIndicator();
        }
    }
    private void BtnHome_Click(object? sender, EventArgs e)
    {
        _activeTab?.Browser.Load(NewTabUrl); // same tab, not a new one
        FocusPage();
    }
    private void BtnNewTab_Click(object? sender, EventArgs e) => CreateTab(NewTabUrl, activate: true);
    private void TabsHost_SizeChanged(object? sender, EventArgs e)
    {
        // Fires during InitializeComponent (tabsHost layout) before the ctor
        // creates captionButtons — guard, like LayoutTabs' own early-outs.
        if (captionButtons != null)
        {
            captionButtons.Width = captionButtons.ButtonWidth * 3; // DPI-correct cluster width
            captionButtons.RefreshState(); // maximize/restore glyph follows window state
        }
        LayoutTabs();
    }

    private void TabsHost_Wheel(object? sender, MouseEventArgs e)
    {
        if (_tabs.Count == 0)
        {
            return;
        }
        int step = Math.Max(40, 3 * (_tabW + TabRightMargin));
        _tabScroll += e.Delta < 0 ? step : -step; // LayoutTabs clamps to the valid range
        LayoutTabs();
    }

    // (Wheel focus-grab lives in the WM_NCMOUSEMOVE branch: the transparent
    // host no longer receives MouseEnter, so focus is grabbed on strip moves.)

    // Called from TabLifeSpanHandler.OnBeforePopup on a CEF thread.
    internal void OpenPopupUrl(string url) => Ui(() => CreateTab(url, activate: true));

    private void StripItem_Click(object? sender, EventArgs e)
    {
        if (sender is Control c && c.Tag is BrowserTab tab && _tabs.Contains(tab))
        {
            ActivateTab(tab);
        }
    }

    private void StripClose_Click(object? sender, EventArgs e)
    {
        if (sender is Control c && c.Tag is BrowserTab tab)
        {
            CloseTab(tab);
        }
    }
}
