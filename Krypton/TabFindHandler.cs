using CefSharp;

namespace Krypton;

// Find-in-page result hook: forwards the live match count to the floating
// find bar. Runs on a CEF thread; the form marshals to the UI thread.
internal sealed class TabFindHandler(MainForm form, BrowserTab tab) : IFindHandler
{
    public void OnFindResult(IWebBrowser chromiumWebBrowser, IBrowser browser,
        int identifier, int count, CefSharp.Structs.Rect selectionRect,
        int activeMatchOrdinal, bool finalUpdate)
    {
        form.ReportFindResult(tab, activeMatchOrdinal, count);
    }
}
