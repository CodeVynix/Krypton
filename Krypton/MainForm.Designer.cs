namespace Krypton;

// Static UI shell: toolbar (nav buttons, favicon, address bar), loading
// strip under the toolbar, tab strip, content area. All behavior lives in
// MainForm.cs; all per-tab data lives in BrowserTab.
partial class MainForm
{
    private Panel toolbarPanel = null!;
    private Button btnBack = null!;
    private Button btnForward = null!;
    private Button btnRefresh = null!;
    private PictureBox faviconBox = null!;
    private TextBox addressBar = null!;
    private Panel loadingStrip = null!;
    private Panel tabStripPanel = null!;
    private FlowLayoutPanel tabsFlow = null!;
    private Button btnNewTab = null!;
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

        // ---- Tab strip ----
        tabStripPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = SystemColors.Control,
            Padding = new Padding(4, 2, 4, 2),
        };
        btnNewTab = new Button
        {
            Width = 32,
            Height = 26,
            Margin = new Padding(2, 0, 0, 0),
            Text = "+",
            Font = new Font("Segoe UI", 11f, FontStyle.Regular),
            FlatStyle = FlatStyle.System,
        };
        btnNewTab.Click += BtnNewTab_Click;
        tabsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true, // overflow scrolls instead of clipping tab close buttons
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        // The + button lives inside the flow as the last item, so it always
        // sits immediately right of the last tab (CreateTab keeps it last).
        tabsFlow.Controls.Add(btnNewTab);
        tabStripPanel.Controls.Add(tabsFlow);

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
            BackColor = SystemColors.Control,
        };
        var navFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        btnBack = new Button { Width = 36, Dock = DockStyle.Left, Text = "\u2190", Font = navFont, FlatStyle = FlatStyle.System };
        btnForward = new Button { Width = 36, Dock = DockStyle.Left, Text = "\u2192", Font = navFont, FlatStyle = FlatStyle.System };
        btnRefresh = new Button { Width = 36, Dock = DockStyle.Left, Text = "\u21bb", Font = navFont, FlatStyle = FlatStyle.System };
        btnBack.Click += BtnBack_Click;
        btnForward.Click += BtnForward_Click;
        btnRefresh.Click += BtnRefresh_Click;

        faviconBox = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 24,
            Height = 24,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(4, 0, 4, 0),
        };
        addressBar = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            PlaceholderText = "Search or enter address",
            AutoCompleteMode = AutoCompleteMode.None,
        };
        addressBar.KeyDown += AddressBar_KeyDown;

        toolbarPanel.Controls.Add(addressBar);
        toolbarPanel.Controls.Add(faviconBox);
        toolbarPanel.Controls.Add(btnRefresh);
        toolbarPanel.Controls.Add(btnForward);
        toolbarPanel.Controls.Add(btnBack);

        // Dock-Top stacking (added Fill first, Top bars after => toolbar ends up topmost).
        Controls.Add(contentPanel);
        Controls.Add(tabStripPanel);
        Controls.Add(loadingStrip);
        Controls.Add(toolbarPanel);

        Text = "Krypton";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(600, 400);
        Size = new System.Drawing.Size(1200, 800);

        ResumeLayout(false);
    }
}
