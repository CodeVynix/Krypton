# AGENTS.md — Krypton

Minimal Windows-only web browser. C# (.NET 8) + WinForms + CefSharp (Chromium).
App's own UI is native WinForms only — no HTML/CSS/JS files.

## Scope (hard limit)

In scope: address-bar navigation, back/forward/refresh, loading indicator
around the address bar, address-bar auto-sync on in-page navigation,
favicon + title display, basic tab open/close.
Out of scope: everything else (bookmarks, history, downloads, settings,
extensions, private mode, etc.). Do not add it.

## Build / run

- Windows + .NET 8 SDK only. No cross-platform support.
- Must target x64: `<PlatformTarget>x64</PlatformTarget>` + `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
  — CefSharp does not work with `AnyCPU`. Without the RID, the native CEF
  binaries (`libcef.dll`, etc.) do not land next to the exe and the app
  won't start. `x86` is not supported here.
- Packages: `CefSharp.WinForms.NETCore` (pulls `CefSharp.Common.NETCore` +
  `chromiumembeddedframework.runtime.win-x64`). Keep all CefSharp packages
  on the same version. (`CefSharp.WinForms` without `.NETCore` is .NET
  Framework-only and will not restore for this project.)
- Commands:
  - `dotnet build`
  - `dotnet run --project <Krypton.csproj>`

## CefSharp lifecycle (do not get wrong)

- `Cef.Initialize(settings)` exactly once in
  `Program.Main` (`[STAThread]`) before creating any `ChromiumWebBrowser`.
  (No `EnableHighDPISupport` — removed in current CefSharp.)
- `Cef.Shutdown()` once on app exit.
- Never instantiate a browser before init completes; check
  `Cef.IsInitialized` if init order is in doubt.

## Threading + event wiring

- `AddressChanged`, `TitleChanged`, `LoadingStateChanged` fire on CEF
  threads, NOT the WinForms UI thread. Update controls only via
  `BeginInvoke`/`Invoke`.
- Address bar: `Enter` → normalize URL (prepend `https://` if no scheme) →
  `browser.Load(url)`.
- Auto-sync: `AddressChanged` → set address-bar text (via `BeginInvoke`).
- Loading: `LoadingStateChanged.IsLoading` → toggle the indicator around
  the address bar.
- Nav buttons: `Back()`/`Forward()`/`Reload()`, enable via
  `CanGoBack`/`CanGoForward` from `LoadingStateChanged`.
- Title: `TitleChanged` → tab text / title display (via `BeginInvoke`).
- Favicon: CefSharp.WinForms has no direct favicon event — derive from the
  current address/navigation state; do not pull in a new browser engine or
  web stack for it.

## Tabs

- One `ChromiumWebBrowser` per `TabPage` in a `TabControl`.
- On tab close: remove the page, then `browser.Dispose()`. Leaking
  undisposed browsers leaks CEF subprocesses.
