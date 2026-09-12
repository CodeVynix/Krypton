using CefSharp;

namespace Krypton;

// Page right-click menu. CEF builds a native model we don't want (Back,
// Forward, Print, View-source only — the "low options" look), so the model
// is cleared and a WinForms menu is shown instead: full navigation set plus
// link/image actions when the click actually hit one. Runs on a CEF thread;
// the form marshals to the UI thread.
internal sealed class TabContextMenuHandler(MainForm owner, BrowserTab tab) : IContextMenuHandler
{
    public void OnBeforeContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser,
        IFrame frame, IContextMenuParams parameters, IMenuModel model)
    {
        model.Clear(); // suppress the native menu entirely
        // Snapshot everything the menu needs: parameters dies with this call,
        // and the tab's plain model fields are safe to read off-thread.
        var snapshot = new PageMenuState(
            tab.CanGoBack, tab.CanGoForward, tab.Url,
            parameters.LinkUrl ?? string.Empty,
            parameters.SourceUrl ?? string.Empty,
            parameters.HasImageContents);
        owner.PostPageMenu(tab, snapshot);
    }

    public bool RunContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser,
        IFrame frame, IContextMenuParams parameters, IMenuModel model,
        IRunContextMenuCallback callback)
    {
        return true; // display handled above (empty native model stays hidden)
    }

    public bool OnContextMenuCommand(IWebBrowser chromiumWebBrowser, IBrowser browser,
        IFrame frame, IContextMenuParams parameters, CefMenuCommand commandId,
        CefEventFlags eventFlags)
    {
        return false; // no native items left to command
    }

    public void OnContextMenuDismissed(IWebBrowser chromiumWebBrowser, IBrowser browser,
        IFrame frame)
    {
    }
}

// Plain-data snapshot: safe to ferry from the CEF thread to the UI thread.
internal sealed record PageMenuState(
    bool CanGoBack, bool CanGoForward, string PageUrl,
    string LinkUrl, string ImageUrl, bool HasImage);
