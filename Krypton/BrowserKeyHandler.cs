using CefSharp;

namespace Krypton;

// CEF-side keys the WinForms message loop never sees: once the page has
// focus it owns its own HWND, so F11/Escape must be caught here. F11 toggles
// fullscreen, Escape exits it. Everything else passes through untouched
// (KeyUp only, so auto-repeat can never double-toggle).
internal sealed class BrowserKeyHandler(MainForm owner) : IKeyboardHandler
{
    public bool OnPreKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser,
        KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers,
        bool isSystemKey, ref bool isKeyboardShortcut)
    {
        if (type != KeyType.KeyUp)
        {
            return false;
        }
        if (windowsKeyCode == (int)Keys.F11)
        {
            owner.ToggleFullScreen();
            return true;
        }
        if (windowsKeyCode == (int)Keys.Escape && owner.IsFullScreen)
        {
            owner.SetFullScreen(false);
            return true;
        }
        return false;
    }

    public bool OnKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser,
        KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers,
        bool isSystemKey)
    {
        return false;
    }
}
