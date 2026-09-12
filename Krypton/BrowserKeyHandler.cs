using CefSharp;

namespace Krypton;

// CEF-side keys the WinForms message loop never sees: once the page has
// focus it owns its own HWND, so shortcuts must be caught here. Classify
// synchronously (OnPreKeyEvent must answer now), run marshalled (the action
// must touch controls on the UI thread). KeyUp only, so auto-repeat can
// never double-fire a tab-closing or navigation action.
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
        Keys keys = (Keys)windowsKeyCode;
        if ((modifiers & CefEventFlags.ControlDown) != 0)
        {
            keys |= Keys.Control;
        }
        if ((modifiers & CefEventFlags.ShiftDown) != 0)
        {
            keys |= Keys.Shift;
        }
        if ((modifiers & CefEventFlags.AltDown) != 0)
        {
            keys |= Keys.Alt;
        }
        if (!MainForm.IsShortcutKey(keys))
        {
            return false;
        }
        // Bare Esc belongs to the page (video fullscreen, dialogs) unless the
        // app will actually act on it — decided from plain fields only, never
        // controls (no handle creation off the UI thread).
        if (keys == Keys.Escape && !owner.WantsPageEscape())
        {
            return false;
        }
        owner.PostShortcut(keys);
        return true;
    }

    public bool OnKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser,
        KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers,
        bool isSystemKey)
    {
        return false;
    }
}
