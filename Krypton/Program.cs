using CefSharp;
using CefSharp.WinForms;

namespace Krypton;

internal static class Program
{
    // CEF lifecycle lives here and nowhere else: init once before any
    // ChromiumWebBrowser exists, shutdown once after the message loop ends.
    [STAThread]
    private static void Main()
    {
        var settings = new CefSettings
        {
            // Persist cache/cookies under LocalAppData. RootCachePath must be
            // set alongside CachePath on current CEF (Chrome bootstrap),
            // otherwise multi-process/singleton init can fail.
            CachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Krypton", "CEF-Cache"),
            LogSeverity = LogSeverity.Disable,
        };
        settings.RootCachePath = settings.CachePath;

        // Custom new-tab scheme. MUST register before Cef.Initialize —
        // CEF ignores scheme registrations afterwards. Standard + secure +
        // local so krypton://new-tab behaves like an integrated page.
        settings.RegisterScheme(new CefCustomScheme
        {
            SchemeName = "krypton",
            DomainName = "new-tab",
            SchemeHandlerFactory = new KryptonSchemeHandlerFactory(),
            IsStandard = true,
            IsLocal = true,
            IsSecure = true,
        });

        if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
        {
            MessageBox.Show("Cef.Initialize failed. Check VC++ 2022 x64 runtime and x64 build config.",
                "Krypton", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());

        Cef.Shutdown();
    }
}
