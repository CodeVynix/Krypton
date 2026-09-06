using CefSharp;
using System.Diagnostics;

namespace Krypton;

// Behavior: tab management + the 6 features, each wired to its CefSharp hook
// (hook locations marked below). CEF events arrive on non-UI threads, so every
// handler marshals through Ui() before touching controls.
partial class MainForm : Form
{
    private const string DefaultUrl = "https://www.google.com";
    private const string SearchUrl = "https://www.google.com/search?q=";

    private static readonly Image DefaultFavicon;
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
                using var icon16 = new Icon(AppIcon, 16, 16);
                DefaultFavicon = icon16.ToBitmap();
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
    }

    public MainForm()
    {
        InitializeComponent();
        if (AppIcon != null)
        {
            Icon = (Icon)AppIcon.Clone(); // window/taskbar icon matches the exe icon
        }
        faviconBox.Image = DefaultFavicon;
        FormClosing += (_, _) => DisposeAllTabs();
        CreateTab(DefaultUrl, activate: true);
    }

    // ---------- Tabs (Feature 6) ----------

    private BrowserTab CreateTab(string url, bool activate)
    {
        var tab = new BrowserTab(url);
        // Hook: FaviconUrlChanged (CEF thread) -> Feature 5 favicon. Sole reason
        // TabDisplayHandler exists; address/title/loading use the events below.
        tab.Browser.DisplayHandler = new TabDisplayHandler(this, tab);
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
        tabsFlow.Controls.Add(tab.StripItem);
        tabsFlow.Controls.SetChildIndex(btnNewTab, tabsFlow.Controls.Count - 1); // + stays right of last tab
        tab.StripIcon.Image = DefaultFavicon;
        _tabs.Add(tab);

        if (activate)
        {
            ActivateTab(tab);
        }
        return tab;
    }

    private void BuildStripItem(BrowserTab tab)
    {
        var item = new Panel
        {
            Width = 200,
            Height = 26,
            Margin = new Padding(0, 0, 4, 0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Control,
            Tag = tab,
        };
        var icon = new PictureBox
        {
            Size = new System.Drawing.Size(16, 16),
            Location = new System.Drawing.Point(6, 5),
            SizeMode = PictureBoxSizeMode.Zoom,
            Tag = tab,
        };
        var title = new Label
        {
            Location = new System.Drawing.Point(28, 4),
            Size = new System.Drawing.Size(138, 18),
            Text = "New Tab",
            AutoEllipsis = true,
            Tag = tab,
        };
        var close = new TabCloseButton
        {
            Size = new System.Drawing.Size(22, 22),
            Location = new System.Drawing.Point(172, 2),
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
    }

    private void ActivateTab(BrowserTab tab)
    {
        _activeTab = tab;
        foreach (var t in _tabs)
        {
            bool active = ReferenceEquals(t, tab);
            t.Browser.Visible = active;
            t.StripItem.BackColor = active ? System.Drawing.Color.White : SystemColors.Control;
        }
        tab.Browser.BringToFront();

        addressBar.Text = tab.Url;
        UpdateNavButtons();
        UpdateLoadingIndicator();
        RefreshActiveTitleFavicon();
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
        tabsFlow.Controls.Remove(tab.StripItem);
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

    private void NavigateToInput(string input)
    {
        if (_activeTab == null)
        {
            return;
        }
        string? url = ResolveInputToUrl(input);
        if (url != null)
        {
            _activeTab.Browser.Load(url);
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
            s.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
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
                addressBar.Text = e.Address; // Feature 4: sync on link clicks etc.
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
        tab.Title = string.IsNullOrWhiteSpace(e.Title) ? tab.Url : e.Title;
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

    // Called from TabDisplayHandler.OnFaviconUrlChange on a CEF thread.
    internal void OnFaviconUrls(BrowserTab tab, IList<string> urls)
    {
        var httpUrls = urls
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Take(3)
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
        RequestFavicon(tab, []); // no reported urls -> derive from page origin
    }

    private void RequestFavicon(BrowserTab tab, List<string> urls)
    {
        string key = urls.Count > 0 ? urls[0] : ("origin:" + tab.Url);
        if (tab.FaviconSource == key)
        {
            return; // already fetched/fetching for this source
        }
        tab.FaviconSource = key;
        Debug.WriteLine($"[Krypton] favicon fetch: page={tab.Url} source={key}");
        _ = FetchFaviconAsync(tab, urls);
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

        foreach (string url in candidates.Distinct())
        {
            try
            {
                byte[] bytes = await FaviconHttp.GetByteArrayAsync(url).ConfigureAwait(false);
                Image? img = DecodeImage(bytes);
                if (img != null)
                {
                    ApplyFavicon(tab, img);
                    return;
                }
                Debug.WriteLine($"[Krypton] favicon decode failed: {url} ({bytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Krypton] favicon download failed: {url} ({ex.Message})");
            }
        }
        ApplyFavicon(tab, null); // total failure -> generic default icon, never blank
    }

    private static Image? DecodeImage(byte[] bytes)
    {
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
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    private void ApplyFavicon(BrowserTab tab, Image? img)
    {
        Ui(() =>
        {
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

    private void UpdateNavButtons()
    {
        btnBack.Enabled = _activeTab?.CanGoBack == true;
        btnForward.Enabled = _activeTab?.CanGoForward == true;
        btnRefresh.Enabled = _activeTab != null;
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
        string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title;

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

    private void AddressBar_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            NavigateToInput(addressBar.Text);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void BtnBack_Click(object? sender, EventArgs e) => _activeTab?.Browser.Back();
    private void BtnForward_Click(object? sender, EventArgs e) => _activeTab?.Browser.Forward();
    private void BtnRefresh_Click(object? sender, EventArgs e) => _activeTab?.Browser.Reload();
    private void BtnNewTab_Click(object? sender, EventArgs e) => CreateTab(DefaultUrl, activate: true);

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
