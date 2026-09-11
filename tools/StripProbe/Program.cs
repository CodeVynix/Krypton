// StripProbe: offscreen regression probe for Krypton's tab strip.
// Opens 14 tabs in a hidden (off-screen) MainForm and asserts the Chrome-like
// layout contract: shared/shrunk widths, full strip height, dynamic close
// buttons, + pinned inside the strip, and the krypton:// scheme loading.
// Run: dotnet run --project tools/StripProbe  (Windows x64 only, like Krypton)
// Exit code 0 = all checks passed.
using System.Drawing;
using System.Reflection;

// NOTE: all CefSharp interaction here is by reflection on purpose, so this
// probe needs no CefSharp compile-time reference (it resolves CefSharp.dll
// from its output directory at runtime, next to Krypton.dll).

internal static class StripProbe
{
    private static int _failures;

    [STAThread]
    private static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("PROBE-ERROR " + ex);
            return 3;
        }
    }

    private static int Run()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "krypton-stripprobe");
        Directory.CreateDirectory(outDir);

        Assembly[] cefAsms = new[]
        {
            Assembly.Load("CefSharp"),
            Assembly.Load("CefSharp.Core"),
            Assembly.Load("CefSharp.Core.Runtime"),
            Assembly.Load("CefSharp.WinForms"),
        };
        object settings = CreateSettings(cefAsms, outDir);
        RegisterKryptonScheme(cefAsms, settings);

        Type settingsType = CefType(cefAsms, "CefSharp.WinForms.CefSettings");
        Type cefType = CefType(cefAsms, "CefSharp.Cef");
        Type bphType = CefType(cefAsms, "CefSharp.IBrowserProcessHandler");
        bool initialized = (bool)cefType
            .GetMethod("Initialize", new[] { settingsType, typeof(bool), bphType })!
            .Invoke(null, new object?[] { settings, true, null })!;
        if (!initialized)
        {
            Console.WriteLine("FAIL cef-initialize");
            return 2;
        }

        try
        {
            return ProbeLayout(outDir);
        }
        finally
        {
            cefType.GetMethod("Shutdown", Type.EmptyTypes)!.Invoke(null, null);
        }
    }

    // CefSharp splits the CefSharp namespace across several assemblies —
    // look in all of them.
    private static Type CefType(Assembly[] cefAsms, string name)
    {
        foreach (Assembly a in cefAsms)
        {
            Type? t = null;
            try { t = a.GetType(name); } catch { }
            if (t != null)
            {
                return t;
            }
        }
        throw new InvalidOperationException("CefSharp type not found: " + name);
    }

    private static object CreateSettings(Assembly[] cefAsms, string outDir)
    {
        // NOTE: the WinForms-specific subclass (auto BrowserSubprocessPath),
        // same one Program.cs uses via using CefSharp.WinForms.
        Type settingsType = CefType(cefAsms, "CefSharp.WinForms.CefSettings");
        object settings = Activator.CreateInstance(settingsType)!;
        string cache = Path.Combine(outDir, "StripProbe-Cache");
        settingsType.GetProperty("CachePath")!.SetValue(settings, cache);
        settingsType.GetProperty("RootCachePath")!.SetValue(settings, cache);
        Type severityType = CefType(cefAsms, "CefSharp.LogSeverity");
        settingsType.GetProperty("LogSeverity")!
            .SetValue(settings, Enum.Parse(severityType, "Disable"));
        return settings;
    }

    private static void RegisterKryptonScheme(Assembly[] cefAsms, object settings)
    {
        Assembly krypton = KryptonAssembly();
        object factory = Activator.CreateInstance(
            krypton.GetType("Krypton.KryptonSchemeHandlerFactory")!, nonPublic: true)!;
        Type schemeType = CefType(cefAsms, "CefSharp.CefCustomScheme");
        object scheme = Activator.CreateInstance(schemeType)!;
        schemeType.GetProperty("SchemeName")!.SetValue(scheme, "krypton");
        schemeType.GetProperty("DomainName")!.SetValue(scheme, "new-tab");
        schemeType.GetProperty("SchemeHandlerFactory")!.SetValue(scheme, factory);
        schemeType.GetProperty("IsStandard")!.SetValue(scheme, true);
        schemeType.GetProperty("IsLocal")!.SetValue(scheme, true);
        schemeType.GetProperty("IsSecure")!.SetValue(scheme, true);
        settings.GetType().GetMethod("RegisterScheme", new[] { schemeType })!
            .Invoke(settings, new[] { scheme });
    }

    private static Assembly KryptonAssembly()
    {
        try
        {
            return Assembly.Load("Krypton");
        }
        catch
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "Krypton");
        }
    }

    private static int ProbeLayout(string outDir)
    {
        ApplicationConfiguration.Initialize();
        Assembly krypton = KryptonAssembly();
        Type formType = krypton.GetType("Krypton.MainForm")!;

        var form = (Form)Activator.CreateInstance(formType, nonPublic: true)!;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000, -3000); // off-screen: no visible flash
        form.Show();
        Pump(10);

        MethodInfo createTab = formType.GetMethod("CreateTab", BindingFlags.Instance | BindingFlags.NonPublic)!;
        const string newTabUrl = "krypton://new-tab";
        for (int i = 0; i < 14; i++)
        {
            createTab.Invoke(form, [newTabUrl, true]);
            if (i % 4 == 3)
            {
                Pump(2);
            }
        }
        Pump(30); // let CEF address/title/loading events marshal back

        // ---- read strip state ----
        var tabs = (System.Collections.IList)formType
            .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        Check(tabs.Count == 15, $"tab-count count={tabs.Count}"); // 1 startup + 14 created

        var host = (Control)formType
            .GetField("tabsHost", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        var plus = (Control)formType
            .GetField("btnNewTab", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        int hostW = host.ClientSize.Width;
        Console.WriteLine($"info hostW={hostW} tabs={tabs.Count}");
        Check(hostW > 600, $"host-width hostW={hostW}");

        var widths = new List<int>();
        var rights = new List<int>();
        bool activeCloseVisible = false;
        bool inactiveCloseHidden = false;
        bool titlesOk = true;
        foreach (object? t in tabs)
        {
            Type tt = t!.GetType();
            var item = (Control)tt.GetProperty("StripItem")!.GetValue(t)!;
            var close = (Control)tt.GetProperty("StripClose")!.GetValue(t)!;
            var title = (Control)tt.GetProperty("StripTitle")!.GetValue(t)!;
            widths.Add(item.Width);
            rights.Add(item.Right);
            if (title.Text != "New Tab")
            {
                titlesOk = false;
            }
            var active = (object?)formType
                .GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form);
            bool isActive = ReferenceEquals(t, active);
            if (isActive && close.Visible)
            {
                activeCloseVisible = true;
            }
            if (!isActive && !close.Visible)
            {
                inactiveCloseHidden = true;
            }
            Check(item.Top == 3 && item.Height == 26, $"tab-geometry top={item.Top} h={item.Height} w={item.Width}");
        }
        Check(widths.Distinct().Count() == 1, $"shared-width [{string.Join(",", widths.Distinct())}]");
        int w = widths[0];
        Check(w is >= 56 and <= 200, $"shrunk-width w={w}");
        Check(titlesOk, "titles-new-tab");
        Check(activeCloseVisible, "active-close-visible");
        Check(inactiveCloseHidden, "inactive-close-hidden-when-narrow");

        int lastRight = rights.Max();
        int plusNeed = plus.Width + 2; // width + left margin
        // 15 tabs at min width legitimately overflow: the contract is that the
        // active (last) tab stays fully visible next to the pinned +, i.e. the
        // scroll state — not zero overflow — is correct.
        Check(lastRight <= hostW - plusNeed + 1, $"active-visible lastRight={lastRight} hostW={hostW}");
        Check(plus.Left >= 0 && plus.Right <= hostW, $"plus-pinned left={plus.Left} right={plus.Right} hostW={hostW}");
        Check(plus.Top == 3, $"plus-top top={plus.Top}");

        // Active tab must still carry the krypton:// URL (scheme pipeline alive).
        var activeTab = formType
            .GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        string url = (string)activeTab.GetType().GetProperty("Url")!.GetValue(activeTab)!;
        Check(url.StartsWith("krypton://", StringComparison.OrdinalIgnoreCase), $"scheme-url url={url}");

        // Popup interception: the browser must carry our TabLifeSpanHandler,
        // and invoking its OnBeforePopup with a blank URL must open a
        // Krypton new tab (not a native window).
        object popupBrowser = activeTab.GetType().GetProperty("Browser")!.GetValue(activeTab)!;
        object? lifeSpan = popupBrowser.GetType().GetProperty("LifeSpanHandler")!.GetValue(popupBrowser);
        Check(lifeSpan != null && lifeSpan.GetType().Name == "TabLifeSpanHandler", "lifespan-wired");
        if (lifeSpan != null)
        {
            Type handlerType = lifeSpan.GetType();
            var onBeforePopup = handlerType.GetMethod("OnBeforePopup")!;
            var ps = onBeforePopup.GetParameters();
            object?[] popupArgs = new object?[ps.Length];
            for (int i = 0; i < popupArgs.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                popupArgs[i] = pt.IsByRef
                    ? (pt.GetElementType()!.IsValueType ? Activator.CreateInstance(pt.GetElementType()!) : null)
                    : (pt.IsValueType ? Activator.CreateInstance(pt) : null);
            }
            popupArgs[0] = popupBrowser; // chromiumWebBrowser
            popupArgs[3] = "about:blank"; // targetUrl
            object? popupResult = onBeforePopup.Invoke(lifeSpan, popupArgs);
            Check(popupResult is true, "popup-cancelled");
            Pump(10);
        }
        int afterPopup = ((System.Collections.IList)formType
            .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!).Count;
        Check(afterPopup == 16, $"popup-to-tab count={afterPopup}");

        // ---- explicit brand overrides (pure rule, no network) ----
        // Gmail-brand pages resolve to the bundled M; everything else falls
        // through to the network pipeline (null). One entry per product call.
        var brandFn = krypton.GetType("Krypton.MainForm")!
            .GetMethod("BrandOverrideArt", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((string?)brandFn.Invoke(null, [new Uri("https://workspace.google.com/intl/en-US/gmail/")]) == "Krypton.gmail-dial.png", "brand-gmail-path");
        Check((string?)brandFn.Invoke(null, [new Uri("https://mail.google.com/mail/")]) == "Krypton.gmail-dial.png", "brand-gmail-host");
        Check(brandFn.Invoke(null, [new Uri("https://www.google.com/search?q=x")]) == null, "brand-google-search-none");
        Check(brandFn.Invoke(null, [new Uri("https://github.com/")]) == null, "brand-github-none");

        // Focus-select (keyboard path): text + Focus() must select all.
        // Reset focus first: the box may already hold it from form-show
        // (first tab stop), in which case Focus() is a no-op by design.
        // Retried: a CEF page finishing load can steal focus mid-assert, so a
        // single attempt flakes; re-focusing must also select all.
        form.GetType().GetProperty("ActiveControl")?.SetValue(form, null);
        Pump(2);
        formType.GetMethod("SetAddressBarText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { "https://www.youtube.com/" });
        var addressBar = (Control?)formType
            .GetField("addressBar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form);
        bool barFocused = false;
        int selLen = -1, txtLen = -1;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                form.GetType().GetProperty("ActiveControl")?.SetValue(form, null);
                Pump(2);
            }
            addressBar?.Focus();
            Pump(3);
            barFocused = addressBar?.Focused == true;
            selLen = (int?)addressBar?.GetType().GetProperty("SelectionLength")?.GetValue(addressBar) ?? -1;
            txtLen = ((string?)addressBar?.GetType().GetProperty("Text")?.GetValue(addressBar))?.Length ?? -1;
            Console.WriteLine($"info focus-state attempt={attempt} focused={barFocused} sel={selLen} len={txtLen}");
            if (barFocused && txtLen > 0 && selLen == txtLen)
            {
                break;
            }
        }
        Check(barFocused && txtLen > 0 && selLen == txtLen, "focus-select-all");

        // Enter-commit: KeyDown Enter on about:blank navigates without network
        // and exercises the refactored commit path (including FocusPage).
        if (addressBar != null)
        {
            addressBar.GetType().GetProperty("Text")?.SetValue(addressBar, "about:blank");
            formType.GetMethod("AddressBar_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, new object?[] { addressBar, new KeyEventArgs(Keys.Enter) });
            Pump(30);
        }
        var currentActive = formType
            .GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        string commitUrl = (string)currentActive.GetType().GetProperty("Url")!.GetValue(currentActive)!;
        Check(commitUrl == "about:blank", $"enter-commit url={commitUrl}");

        // ---- caption/drag hit-test accuracy (REAL OS routing) ----
        // WindowFromPoint finds the topmost child first, exactly like a real
        // mouse: transparent strip children must forward (HTTRANSPARENT) to
        // the form, opaque tab/+/× children must answer HTCLIENT themselves.
        // Sending straight to the form would bypass all of that and lie.
        var capCtl = (Control)formType
            .GetField("captionButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        Rectangle capScreen = capCtl.RectangleToScreen(capCtl.ClientRectangle);
        Console.WriteLine($"info capScreen={capScreen} hostScreen-pending");
        int third = Math.Max(1, capScreen.Width / 3);
        int midY = capScreen.Top + capScreen.Height / 2;
        int gotMin = NcHitReal(capScreen.Left + third / 2, midY);
        Check(gotMin == 8, $"hit-min got={gotMin}");
        int gotMax = NcHitReal(capScreen.Left + third + third / 2, midY);
        Check(gotMax == 9, $"hit-max got={gotMax}");
        int gotClose = NcHitReal(capScreen.Left + 2 * third + third / 2, midY);
        Check(gotClose == 20, $"hit-close got={gotClose}");
        // Empty strip (left pad, below the tabs, clear of the top grip) drags.
        Rectangle hostScreen = host.RectangleToScreen(host.ClientRectangle);
        Console.WriteLine($"info hostScreen={hostScreen}");
        int gotDrag = NcHitReal(hostScreen.Left + 1, hostScreen.Top + hostScreen.Height - 2);
        Check(gotDrag == 2, $"hit-drag got={gotDrag}");
        // Top edge of empty strip (above the tabs) resizes.
        int gotTop = NcHitReal(hostScreen.Left + 1, hostScreen.Top + 2);
        Check(gotTop == 12, $"hit-top got={gotTop}");
        // Opaque children keep their clicks: active tab center, +, tab ×.
        var activeCtl = (Control)activeTab.GetType().GetProperty("StripItem")!.GetValue(activeTab)!;
        Rectangle tabScreen = activeCtl.RectangleToScreen(activeCtl.ClientRectangle);
        int gotTab = NcHitReal(tabScreen.Left + tabScreen.Width / 2, tabScreen.Top + tabScreen.Height / 2);
        Check(gotTab == 1, $"hit-tab-client got={gotTab}");
        var plusScreen = plus.RectangleToScreen(plus.ClientRectangle);
        int gotPlus = NcHitReal(plusScreen.Left + plusScreen.Width / 2, plusScreen.Top + plusScreen.Height / 2);
        Check(gotPlus == 1, $"hit-plus-client got={gotPlus}");
        var closeCtl = (Control)activeTab.GetType().GetProperty("StripClose")!.GetValue(activeTab)!;
        Rectangle closeScreen = closeCtl.RectangleToScreen(closeCtl.ClientRectangle);
        int gotX = NcHitReal(closeScreen.Left + closeScreen.Width / 2, closeScreen.Top + closeScreen.Height / 2);
        Check(gotX == 1, $"hit-closex-client got={gotX}");

        // ---- owned caption gestures (DOWN + UP performs the action) ----
        // Maximizing briefly shows the offscreen form fullscreen; it is
        // restored immediately after (and closed at the end regardless).
        // Points are stale after the first flip, which is fine: the gesture
        // is wParam-driven, exactly like the real DOWN/UP messages.
        Point maxPt = new Point(capScreen.Left + third + third / 2, midY);
        NcClick(form.Handle, 9, maxPt);
        Pump(3);
        Check(form.WindowState == FormWindowState.Maximized, $"click-maximize state={form.WindowState}");
        NcClick(form.Handle, 9, maxPt);
        Pump(3);
        Check(form.WindowState == FormWindowState.Normal, $"click-restore state={form.WindowState}");
        SendMessage(form.Handle, 0xA3, 9, PackPoint(maxPt.X, maxPt.Y));
        Pump(3);
        Check(form.WindowState == FormWindowState.Maximized, $"dblclick-maximize state={form.WindowState}");
        NcClick(form.Handle, 9, maxPt);
        Pump(3);
        Check(form.WindowState == FormWindowState.Normal, $"dblclick-restore state={form.WindowState}");
        if (form.WindowState != FormWindowState.Normal)
        {
            form.WindowState = FormWindowState.Normal; // keep later steps + the screen deterministic
            Pump(3);
        }

        // ---- minimize/restore: geometry + paint must survive ----
        // A poisoned NCCALCSIZE rect from the iconic transition used to come
        // back as a blank white window (clamp + forced recalc prevent it).
        // Strip/caption are pure WinForms, so their pixels are deterministic
        // here regardless of CEF renderer suspend/resume timing.
        form.WindowState = FormWindowState.Minimized;
        Pump(5);
        form.WindowState = FormWindowState.Normal;
        Pump(10);
        Check(form.ClientSize.Width > 800 && form.ClientSize.Height > 500,
            $"restore-client {form.ClientSize.Width}x{form.ClientSize.Height}");
        var stripPanel = (Control)formType
            .GetField("tabStripPanel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        Check(stripPanel.Top == 0 && stripPanel.Height == 32,
            $"restore-strip top={stripPanel.Top} h={stripPanel.Height}");
        int topNC = (int)formType
            .GetField("_lastTopNC", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        Check(topNC is > 0 and <= 128, $"restore-topnc topNC={topNC}");
        var stripBack = (Color)krypton.GetType("Krypton.MainForm")!
            .GetField("StripBack", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        Check(SamplePixel(stripPanel, 2, 2).ToArgb() == stripBack.ToArgb(), "restore-strip-paint");
        var capAfter = (Control)formType
            .GetField("captionButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        Check(SamplePixel(capAfter, capAfter.Width / 2, capAfter.Height / 2).ToArgb() == stripBack.ToArgb(),
            "restore-caption-paint");

        // ---- maximize/restore: no native caption may return ----
        // Un-maximize once mixed a stale live rect with the proposed client,
        // rejected the measurement, and stuck the OS title bar back on with
        // no recalc following. topNC is now a learned constant, so the flush
        // top must survive the cycle: strip geometry, client size, and paint.
        Size preMax = form.ClientSize;
        form.WindowState = FormWindowState.Maximized;
        Pump(5);
        form.WindowState = FormWindowState.Normal;
        Pump(10);
        Check(form.ClientSize == preMax, $"maxrestore-client {form.ClientSize} vs {preMax}");
        Check(stripPanel.Top == 0 && stripPanel.Height == 32,
            $"maxrestore-strip top={stripPanel.Top} h={stripPanel.Height}");
        Check(SamplePixel(stripPanel, 2, 2).ToArgb() == stripBack.ToArgb(), "maxrestore-strip-paint");

        // ---- true fullscreen (covers the taskbar, unlike OS maximize) ----
        // Briefly takes the real screen; restored immediately after (and the
        // form closes at the end regardless). No handle recreation involved,
        // so renderers survive — the page underneath keeps working.
        var setFull = formType.GetMethod("SetFullScreen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var isFullProp = formType.GetProperty("IsFullScreen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Rectangle beforeFull = form.Bounds;
        setFull.Invoke(form, [true]);
        Pump(3);
        Check((bool)isFullProp.GetValue(form)!, "fullscreen-flag");
        Rectangle monBounds = Screen.FromControl(form).Bounds;
        Check(form.Bounds == monBounds, $"fullscreen-fill {form.Bounds} vs {monBounds}");
        using (var capFull = new Bitmap(capCtl.Width, capCtl.Height))
        {
            capCtl.DrawToBitmap(capFull, new Rectangle(0, 0, capFull.Width, capFull.Height));
            string capFullPng = Path.Combine(outDir, "probe-caption-fullscreen.png");
            capFull.Save(capFullPng);
            Console.WriteLine($"info caption-fullscreen-png={capFullPng}");
        }
        setFull.Invoke(form, [false]);
        Pump(3);
        Check(!(bool)isFullProp.GetValue(form)!, "fullscreen-exit-flag");
        Check(form.Bounds == beforeFull, $"fullscreen-restore {form.Bounds} vs {beforeFull}");

        // ---- maximized fill: the window must cover the working area ----
        // No resolution detection code is needed: OS maximize fills whatever
        // monitor/DPI is live (1080p or otherwise). Assert the maximized
        // frame contains the full working area — a broken custom NCCALCSIZE
        // would inset it instead — and render the maximized caption cluster
        // as glyph evidence (restore overlap state).
        form.WindowState = FormWindowState.Maximized;
        Pump(5);
        Rectangle workArea = Screen.FromControl(form).WorkingArea;
        Check(form.DesktopBounds.Contains(workArea),
            $"maximize-fills desktop={form.DesktopBounds} work={workArea} {workArea.Width}x{workArea.Height}");
        Check(stripPanel.Top == 0 && stripPanel.Height == 32,
            $"maximize-strip top={stripPanel.Top} h={stripPanel.Height}");
        // Screen-space: the strip must start exactly at the monitor top while
        // maximized — absorbing the full frame constant here pushes it above
        // the screen and clips tab titles + caption glyphs (the reported bug).
        Rectangle stripMax = stripPanel.RectangleToScreen(stripPanel.ClientRectangle);
        Check(stripMax.Top == workArea.Top,
            $"maximize-flush stripTop={stripMax.Top} workTop={workArea.Top}");
        using (var capMax = new Bitmap(capCtl.Width, capCtl.Height))
        {
            capCtl.DrawToBitmap(capMax, new Rectangle(0, 0, capMax.Width, capMax.Height));
            string capMaxPng = Path.Combine(outDir, "probe-caption-maximized.png");
            capMax.Save(capMaxPng);
            Console.WriteLine($"info caption-max-png={capMaxPng}");
        }
        form.WindowState = FormWindowState.Normal;
        Pump(5);

        // ---- render evidence (in-process DrawToBitmap: no foreground needed) ----
        string stripPng = Path.Combine(outDir, "probe-strip-14tabs.png");
        using (var bmp = new Bitmap(host.Width, host.Height))
        {
            host.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(stripPng);
        }
        Console.WriteLine($"info strip-png={stripPng}");
        RenderIcons(krypton, outDir);

        form.Close();
        Console.WriteLine(_failures == 0 ? "PROBE-PASS" : $"PROBE-FAIL failures={_failures}");
        return _failures == 0 ? 0 : 1;
    }

    private static void RenderIcons(Assembly krypton, string outDir)
    {
        // Big renders of the owner-drawn nav glyphs for visual review.
        Type btnType = krypton.GetType("Krypton.NavButton")!;
        Type kindType = krypton.GetType("Krypton.NavKind")!;
        foreach (string name in new[] { "Back", "Refresh", "Stop", "Home" })
        {
            var btn = (Control)Activator.CreateInstance(btnType, nonPublic: true)!;
            btnType.GetProperty("Kind")!.SetValue(btn, Enum.Parse(kindType, name));
            btn.Size = new Size(96, 96);
            using var bmp = new Bitmap(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(SystemColors.Control);
            }
            btn.DrawToBitmap(bmp, new Rectangle(0, 0, 96, 96));
            string path = Path.Combine(outDir, $"probe-icon-{name.ToLowerInvariant()}.png");
            bmp.Save(path);
            Console.WriteLine($"info icon-{name}={path}");
            btn.Dispose();
        }
    }

    private static void Pump(int rounds)
    {
        for (int i = 0; i < rounds; i++)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }
    }

    // In-process render sample: no foreground needed, deterministic for
    // pure-WinForms controls (no CEF renderer timing involved).
    private static Color SamplePixel(Control c, int x, int y)
    {
        using var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp.GetPixel(Math.Clamp(x, 0, bmp.Width - 1), Math.Clamp(y, 0, bmp.Height - 1));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetParent(nint hWnd);

    // Raw WM_NCHITTEST at a screen point. Packs a properly sign-extended
    // LPARAM (negative coords must survive intact on x64).
    private static int NcHit(nint hwnd, int x, int y)
    {
        long packed = ((long)y << 16) ^ ((long)x & 0xFFFF);
        return (int)SendMessage(hwnd, 0x84, 0, (nint)packed);
    }

    // True end-to-end routing: find the topmost window like the OS does and
    // follow HTTRANSPARENT forwarding exactly as real input would.
    private static int NcHitReal(int x, int y)
    {
        nint hwnd = WindowFromPoint(new Point(x, y));
        for (int i = 0; i < 4 && hwnd != 0; i++)
        {
            int r = NcHit(hwnd, x, y);
            if (r != -1)
            {
                return r;
            }
            hwnd = GetParent(hwnd);
        }
        return -999;
    }

    private static nint PackPoint(int x, int y) => (nint)(((long)y << 16) ^ ((long)x & 0xFFFF));

    // Owned caption gesture: DOWN + UP on the same button (the app performs
    // the action itself; DefWindowProc tracking is cut out by design).
    private static void NcClick(nint hwnd, int code, Point pt)
    {
        nint lp = PackPoint(pt.X, pt.Y);
        SendMessage(hwnd, 0xA1, code, lp);
        SendMessage(hwnd, 0xA2, code, lp);
    }

    private static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok)
        {
            _failures++;
        }
    }
}
