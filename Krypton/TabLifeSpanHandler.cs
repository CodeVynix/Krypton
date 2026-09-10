using CefSharp;

namespace Krypton;

// Keeps popups (window.open, target=_blank links) inside Krypton's own tab
// strip instead of spawning separate native windows outside our chrome.
// Runs on CEF threads: tab creation is marshaled to the UI thread.
internal sealed class TabLifeSpanHandler : ILifeSpanHandler
{
    private readonly MainForm _owner;

    public TabLifeSpanHandler(MainForm owner)
    {
        _owner = owner;
    }

    public bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
    {
        newBrowser = null!;
        // Blank popups land on the new-tab page; anything else opens as-is.
        string url = string.IsNullOrWhiteSpace(targetUrl) ||
            targetUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            ? MainForm.NewTabUrl
            : targetUrl;
        _owner.OpenPopupUrl(url);
        return true; // cancel the native popup: we opened a tab instead
    }

    public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser) => false;

    public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }
}
