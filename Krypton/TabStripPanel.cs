namespace Krypton;

// Outer strip container. Transparent to hit-testing like TabStripHost:
// HTTRANSPARENT chains upward (buttons -> host -> this panel -> form), and
// any opaque link would swallow the message as HTCLIENT before MainForm's
// screen-space mapping ever runs. This panel has no interactive children of
// its own (only the host), so forwarding unconditionally is safe.
internal sealed class TabStripPanel : Panel
{
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
