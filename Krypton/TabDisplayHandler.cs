using CefSharp;
using CefSharp.Handler;

namespace Krypton;

// Display-state hook for favicon only. ChromiumWebBrowser exposes
// AddressChanged / TitleChanged / LoadingStateChanged as .NET events
// (wired in MainForm), but favicon URLs arrive solely through
// IDisplayHandler.OnFaviconUrlChange, so this adapter forwards just that.
// Runs on a CEF thread; MainForm marshals to the UI thread.
internal sealed class TabDisplayHandler : DisplayHandler
{
    private readonly MainForm _form;
    private readonly BrowserTab _tab;

    public TabDisplayHandler(MainForm form, BrowserTab tab)
    {
        _form = form;
        _tab = tab;
    }

    protected override void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
    {
        _form.OnFaviconUrls(_tab, urls);
    }
}
