using CefSharp.WinForms;

namespace Krypton;

// Per-tab state. Each tab owns exactly one ChromiumWebBrowser whose history,
// loading state, title, favicon and URL are independent from other tabs.
// The browser instance stays alive while switching tabs (visibility only).
internal sealed class BrowserTab : IDisposable
{
    public ChromiumWebBrowser Browser { get; }

    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = "New Tab";
    public Image? Favicon { get; set; }
    // Dedupes favicon fetches: key of the last fetch issued for this tab.
    // Reset when the host changes so the new page gets its own icon.
    public string FaviconSource { get; set; } = string.Empty;
    public bool IsLoading { get; set; }
    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }

    // Strip UI owned by MainForm (favicon box, title label, close button row).
    public Panel StripItem { get; set; } = null!;
    public PictureBox StripIcon { get; set; } = null!;
    public Label StripTitle { get; set; } = null!;

    public BrowserTab(string url)
    {
        Url = url;
        Browser = new ChromiumWebBrowser(url)
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
    }

    public void Dispose()
    {
        Browser.Dispose();
    }
}
