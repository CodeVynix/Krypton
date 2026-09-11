# Krypton

A minimal Windows-only web browser — C# (.NET 8) + WinForms + CefSharp (Chromium) — with a Chrome-style dark UI drawn entirely in native WinForms. No web tech in the app shell itself.

## Releases

### v1.0.0 — completely finished

Shipped fixes: tab favicon→title spacing (Chrome-tight 5px gap), omnibox
completion preserves typed casing (CapsLock `mM` fix), crash hardening
(`%LocalAppData%\Krypton\crash.log` + safe UI marshal + favicon disposed
guards for page navigations like Google → YouTube).
Bug fixes or any GitHub issues filed against v1.0.0 will be added in v1.1.0.
If you find any issues with the browser, add an issue in
[https://github.com/CodeVynix/Krypton/issues](https://github.com/CodeVynix/Krypton/issues).

### v1.1.0 — in progress

Deferred: page context-menu expansion, Chrome-parity UI ultra-polish,
`newtab.html` re-theme (keep 4 dials, no JS).

## Features (v1.0.0)

- **Omnibox**: `https://` prepended when the scheme is missing, bare terms go to Google; inline completion from browsed hosts; Chrome-style select-all, `Tab` to accept, `Esc` to revert, `Enter` to go
- **Toolbar**: back / forward / refresh / stop / home with history-aware states; Stop aborts and falls back instead of stranding a blank page
- **Loading indicator** under the address bar (active tab only)
- **Address-bar auto-sync** on in-page navigation; **favicon + title** per tab and next to the bar (largest-decodable icon wins)
- **Tabs**: open (`+`), close (per-tab `×`), shrink-to-fit with wheel scroll on overflow; closing the last tab exits
- **New-tab page** (`krypton://new-tab`): wordmark, search box, shortcut dials
- **Chrome-style frame**: flush dark tab strip, owner-drawn minimize / maximize / close, drag / resize / snap all native; `F11` true fullscreen, `Esc` exits

Deliberately *not* included: bookmarks, history UI, downloads, settings, extensions, private mode.

## Requirements

- Windows 10/11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- VC++ 2022 x64 runtime (required by CEF)
- Smart App Control (SAC): recommended to disable — the app is currently
  unsigned and may be blocked. A signing application has been sent to
  [SignPath Foundation](https://signpath.org).

## Build & run

```powershell
dotnet build
dotnet run --project Krypton\Krypton.csproj
```

CefSharp only works on x64, so the project pins `<PlatformTarget>x64</PlatformTarget>` + `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` — don't switch to AnyCPU. Always close a running Krypton before rebuilding (the exe locks its output files).

## Release build (for signing / distribution)

```powershell
dotnet publish Krypton\Krypton.csproj -c Release -r win-x64 --self-contained true -o publish
```

Ship the whole `publish` folder; `Krypton.exe` is the signable entry point. Do **not** add single-file publishing — CefSharp can't load its native libraries from a single-file bundle and the app won't start. Smoke-test the published exe (launch, navigate, minimize/restore) before signing.

## Repository layout

- `Krypton/` — the browser (`Program.cs` CEF lifecycle, `MainForm` tabs/toolbar/frame, owner-drawn controls, bundled `newtab.html`)
- `tools/StripProbe/` — offscreen regression probe: tab-strip layout, popups, omnibox commit, hit-test routing, state cycles. `dotnet run --project tools/StripProbe`; exit 0 = pass, evidence PNGs in `%TEMP%\krypton-stripprobe`

## Notes

- Per-user data lives under `%LocalAppData%\Krypton` (`history.db` completion cache, `CEF-Cache` browser profile).
