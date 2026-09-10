namespace Krypton;

// Host panel for the tab strip. A plain Panel is not Selectable, so it can
// never hold focus and would never receive MouseWheel — this one can be
// focused programmatically (TabStop stays false so Tab-key navigation skips
// it), which lets the wheel scroll overflowed tabs Chrome-style.
// Plain Panel (no AutoScroll) also means overflow never spawns a scrollbar
// that would steal strip height and clip the tabs: excess tabs simply clip
// horizontally at full height and scroll back via wheel/selection.
internal sealed class TabStripHost : Panel
{
    public TabStripHost()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
    }

    // Transparent to hit-testing: WM_NCHITTEST goes to the topmost child
    // first, and a panel answering HTCLIENT would swallow every empty-strip
    // point before MainForm's mapping ever runs (no drag, no caption codes —
    // exactly the dead-strip report). HTTRANSPARENT forwards the message to
    // the form, whose screen-space mapping answers drag/buttons/resize.
    // Tab/+/× children stay opaque, so their clicks keep working.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) // WM_NCHITTEST
        {
            m.Result = (IntPtr)(-1); // HTTRANSPARENT: ask my parent instead
            return;
        }
        base.WndProc(ref m);
    }
}
