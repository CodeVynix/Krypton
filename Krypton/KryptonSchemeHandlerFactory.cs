using CefSharp;

namespace Krypton;

// Serves the bundled pages (plus new-tab art) for the custom `krypton`
// scheme. Registered once in Program before Cef.Initialize (CEF requires
// scheme registration before init). Created on CEF IO threads, so this must
// stay thread-safe and never touch WinForms controls: assets load once into
// statics and every request is answered from memory.
internal sealed class KryptonSchemeHandlerFactory : ISchemeHandlerFactory
{
    private static readonly string NewTabHtml = LoadNewTabHtml();
    private static readonly byte[] GmailDialPng = LoadGmailDialPng();
    private static readonly string DocsHtml = LoadDocsHtml();

    public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
    {
        // Exact-asset match first: page art is bundled (an <img> hotlink to
        // the live site's .ico does NOT render — Chromium <img> cannot paint
        // ICO, PNG-compressed frames least of all — so the M ships as PNG).
        // Anything else under krypton://new-tab (including the automatic
        // /favicon.ico fetch) gets the page.
        if (request.Url.EndsWith("/gmail-dial.png", StringComparison.OrdinalIgnoreCase)
            && GmailDialPng.Length > 0)
        {
            return ResourceHandler.FromByteArray(GmailDialPng, mimeType: "image/png");
        }
        // v1.1.0 Docs page (static, no JS). Unknown hosts fall back to NTP.
        try
        {
            if (new Uri(request.Url).Host.Equals("docs", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceHandler.FromString(DocsHtml, mimeType: Cef.GetMimeType("html"));
            }
        }
        catch { /* malformed URL -> NTP below */ }
        return ResourceHandler.FromString(NewTabHtml, mimeType: Cef.GetMimeType("html"));
    }

    private static byte[] LoadGmailDialPng()
    {
        try
        {
            using var s = typeof(KryptonSchemeHandlerFactory).Assembly
                .GetManifestResourceStream("Krypton.gmail-dial.png");
            if (s != null)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch { /* fall through to empty below; the page gets the HTML anyway */ }
        return [];
    }

    private static string LoadNewTabHtml()
    {
        try
        {
            using var s = typeof(KryptonSchemeHandlerFactory).Assembly
                .GetManifestResourceStream("Krypton.newtab.html");
            if (s != null)
            {
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
        }
        catch { /* fall through to the inline fallback below */ }
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>New Tab</title></head>"
            + "<body style=\"background:#343842;color:#e8eaed;font-family:sans-serif\">"
            + "<h1 style=\"text-align:center;margin-top:20vh\">New Tab</h1></body></html>";
    }

    private static string LoadDocsHtml()
    {
        try
        {
            using var s = typeof(KryptonSchemeHandlerFactory).Assembly
                .GetManifestResourceStream("Krypton.docs.html");
            if (s != null)
            {
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
        }
        catch { /* fall through to the inline fallback below */ }
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Krypton Docs</title></head>"
            + "<body style=\"background:#343842;color:#e8eaed;font-family:sans-serif\">"
            + "<h1 style=\"text-align:center;margin-top:20vh\">Krypton Docs</h1></body></html>";
    }
}
