namespace Krypton;

// Static UI shell: toolbar (nav buttons, favicon, address bar), loading
// strip under the toolbar, tab strip, content area. All behavior lives in
// MainForm.cs; all per-tab data lives in BrowserTab.
partial class MainForm
{
    private Panel toolbarPanel = null!;
    private NavButton btnBack = null!;
    private NavButton btnForward = null!;
    private NavButton btnRefresh = null!;
    private NavButton btnStop = null!;
    private NavButton btnHome = null!;
    private PictureBox faviconBox = null!;
    private TextBox addressBar = null!;
    private Panel loadingStrip = null!;
    private TabStripPanel tabStripPanel = null!;
    private TabStripHost tabsHost = null!;
    private TabPlusButton btnNewTab = null!;
    private Panel contentPanel = null!;

    private void InitializeComponent()
    {
        SuspendLayout();

        // ---- Content (Fill; added first so Top-docked bars stack above it) ----
        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.White,
        };

        // ---- Tab strip (Chrome-style: dark, flush under the absorbed caption) ----
        // Hit-transparent container (see TabStripPanel): NCHITTEST chains up
        // to the form's screen-space mapping instead of dying as HTCLIENT.
        tabStripPanel = new TabStripPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(43, 48, 56), // == MainForm.StripBack: dark Chrome frame
            Padding = new Padding(4, 0, 4, 0),
        };
        // Borderless owner-drawn + (Chrome-style): no button chrome, white
        // hover disc. Size comes from the control (32x26); LayoutTabs
        // positions it right after the last tab and pins it visible.
        btnNewTab = new TabPlusButton
        {
            Margin = new Padding(2, 3, 0, 3),
            Location = new System.Drawing.Point(2, 3),
        };
        btnNewTab.Click += BtnNewTab_Click;
        // Plain host panel, NOT a FlowLayoutPanel: LayoutTabs positions every
        // tab manually (Chrome-style shrink + wheel scroll). No AutoScroll, so
        // overflow never spawns a scrollbar that would clip the strip height.
        tabsHost = new TabStripHost
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(43, 48, 56), // == MainForm.StripBack
        };
        tabsHost.Controls.Add(btnNewTab);
        tabsHost.SizeChanged += TabsHost_SizeChanged; // window resize re-shares width across tabs
        tabsHost.MouseWheel += TabsHost_Wheel; // scroll overflowed tabs like Chrome
        tabStripPanel.Controls.Add(tabsHost);

        // ---- Loading indicator: 3px bar just beneath the address bar ----
        loadingStrip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 3,
            BackColor = System.Drawing.Color.DodgerBlue,
            Visible = false,
        };

        // ---- Toolbar ----
        toolbarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(4),
            BackColor = Color.FromArgb(32, 33, 36), // == MainForm.ToolbarBack: dark Chrome toolbar
        };
        // Borderless owner-drawn nav buttons (Chrome-style): geometric glyphs,
        // circular hover, greyed when disabled. Visual order left-to-right is
        // back, forward, refresh, stop, home — Dock-Left stacks in reverse of
        // adding, so add back last.
        btnBack = new NavButton { Kind = NavKind.Back };
        btnForward = new NavButton { Kind = NavKind.Forward };
        btnRefresh = new NavButton { Kind = NavKind.Refresh };
        btnStop = new NavButton { Kind = NavKind.Stop };
        btnHome = new NavButton { Kind = NavKind.Home };
        btnBack.Click += BtnBack_Click;
        btnForward.Click += BtnForward_Click;
        btnRefresh.Click += BtnRefresh_Click;
        btnStop.Click += BtnStop_Click;
        btnHome.Click += BtnHome_Click;

        faviconBox = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 24,
            Height = 24,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(4, 0, 4, 0),
            BackColor = Color.FromArgb(32, 33, 36), // match the dark toolbar
        };
        addressBar = new TextBox
        {
            Dock = DockStyle.None, // positioned by LayoutToolbar: fixed height, vertically centered
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            PlaceholderText = "Search or enter address",
            AutoCompleteMode = AutoCompleteMode.None,
            BorderStyle = BorderStyle.FixedSingle, // flat 1px edge, closer to Chrome's omnibox than sunken 3D
            BackColor = Color.FromArgb(48, 49, 54), // dark omnibox (pairs with ToolbarBack above)
            ForeColor = Color.FromArgb(232, 234, 237), // light omnibox text
        };
        addressBar.KeyDown += AddressBar_KeyDown;
        addressBar.PreviewKeyDown += AddressBar_PreviewKeyDown; // claim Tab for completion accept
        addressBar.Enter += AddressBar_Enter; // Chrome-style select-all on focus
        addressBar.Leave += AddressBar_Leave;
        addressBar.MouseDown += AddressBar_MouseDown; // first-click select-all (sync on MouseUp)
        addressBar.MouseUp += AddressBar_MouseUp;
        addressBar.TextChanged += AddressBar_TextChanged; // inline history completion

        toolbarPanel.Controls.Add(addressBar);
        toolbarPanel.Controls.Add(faviconBox);
        toolbarPanel.Controls.Add(btnHome);
        toolbarPanel.Controls.Add(btnStop);
        toolbarPanel.Controls.Add(btnRefresh);
        toolbarPanel.Controls.Add(btnForward);
        toolbarPanel.Controls.Add(btnBack);
        toolbarPanel.Resize += ToolbarPanel_Resize; // keep the omnibox centered/full-width

        // Dock-Top stacking: last-added Top ends up topmost. Add bottom-up
        // (content, loading, toolbar, tabStrip) so the final order top to
        // bottom is: tabStrip, toolbar (nav + address), loading strip just
        // beneath the address bar — all below the native OS title bar.
        Controls.Add(contentPanel);
        Controls.Add(loadingStrip);
        Controls.Add(toolbarPanel);
        Controls.Add(tabStripPanel);

        Text = "Krypton";
        KeyPreview = true; // form sees toolbar keys (F11/Esc) before focused controls do
        KeyDown += MainForm_KeyDown;
        FormBorderStyle = FormBorderStyle.Sizable; // standard OS frame: native
        // title bar, caption buttons, resize edges, snap, and shadow — all from Windows.
        ShowIcon = true; // Kr logo in the native title bar, taskbar, and Alt-Tab.
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(600, 400);
        Size = new System.Drawing.Size(1200, 800);

        ResumeLayout(false);
    }
}
