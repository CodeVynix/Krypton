# Krypton

I'm building the browser I always wanted: something that looks and feels like Chrome, without the weight that comes with it. Krypton is Windows-only, written in C# (.NET 8) with WinForms for the whole UI and CefSharp (real Chromium) doing the actual page rendering.

No bookmarks bar managers, no extensions store, no accounts, no sync. Just fast tabs and an address bar that gets out of your way.

## Where things stand

**v1.0.0** is done. It ended up including a few fixes I couldn't leave alone: tab title spacing, an annoying CapsLock casing bug in autocomplete (`mM`), and some crash hardening with a `crash.log` so silent deaths leave evidence (`%LocalAppData%\Krypton\crash.log`).

If you find anything broken in v1.0.0, [open an issue](https://github.com/CodeVynix/Krypton/issues) and it'll land in v1.1.0.

**v1.1.0** is done too: a proper right-click menu (back/forward/reload, print, save as, view source, inspect, link and image actions), a bunch of UI polish, a re-themed new tab page, full keyboard shortcuts with a floating find bar, and a built-in docs page — hit the Docs link on the new tab page, it lives at `krypton://docs`.

**v1.2.0** is the big one and it's still on the drawing board: settings page (`krypton://settings`, Alt+F then S), history (`krypton://history`, Ctrl+H), downloads (Ctrl+J), bookmarks that grow as you browse, Incognito mode (Ctrl+Shift+N — and yeah, it's called Incognito, full stop), address bar suggestions, and experimental extensions. (Docs already shipped in v1.1.0.)

## What it can do

- **Address bar**: type an address to go there (`https://` gets added if you skip it), type anything else to Google it. It completes hostnames as you type — `Tab` accepts, `Esc` reverts, `Enter` goes.
- **Toolbar**: back / forward / refresh / stop / home, buttons grey out when they can't do anything. Hitting stop on a first load takes you somewhere sane instead of a blank page.
- **Loading bar** under the address bar, only for the active tab.
- **Tabs**: `+` opens, `×` closes, they shrink when there are many and you can scroll them with the wheel. Closing the last tab quits the app.
- **Window**: dark tab strip flush with the top, custom minimize/maximize/close, normal drag/resize/snap, `F11` for real fullscreen.

Things it deliberately doesn't have yet: bookmarks, history UI, downloads, settings, extensions, private mode. That's the v1.2.0 list above.

## What you need

- Windows 10/11, 64-bit
- [Git](https://git-scm.com/download/win) (to clone the repo)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [VC++ 2022 x64 runtime](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) (Chromium won't start without it)
- [Inno Setup](https://jrsoftware.org/isdl.php) (only needed if you want to build the installer from `Krypton.iss`)
- Heads up: the app isn't code-signed yet, so Smart App Control may block it — easiest is to turn SAC off for now. I've applied for free signing through [SignPath Foundation](https://signpath.org), so this goes away once that's approved.

## Building it

```powershell
git clone https://github.com/CodeVynix/Krypton.git
cd Krypton
dotnet build
dotnet run --project Krypton\Krypton.csproj
```

Two things that will bite you if you don't know them: the project has to stay on x64 (`<PlatformTarget>x64</PlatformTarget>` + `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`) because that's all CefSharp supports — AnyCPU simply won't run. And always close Krypton before rebuilding, the running exe locks its own files and the build fails.

## Making a release build

```powershell
dotnet publish Krypton\Krypton.csproj -c Release -r win-x64 --self-contained true -o publish
```

Ship the entire `publish` folder, `Krypton.exe` is the entry point. To make the actual installer, compile `Krypton.iss` with Inno Setup (`iscc Krypton.iss`) — you'll need Inno installed for that step. Don't try single-file publishing — CefSharp needs its native DLLs sitting next to the exe and single-file breaks that. Run the published exe once yourself (open a page, minimize/restore it) before handing it to anyone.

## What's where

- `Krypton/` — the actual browser. `Program.cs` starts up CEF, `MainForm` is tabs/toolbar/frame, plus a handful of owner-drawn controls and the bundled `newtab.html` / `docs.html`.
- `tools/StripProbe/` — a little offscreen test that opens a bunch of tabs and checks the strip layout, popups, hit-testing and window states. `dotnet run --project tools/StripProbe`, exit code 0 means all good, screenshots land in `%TEMP%\krypton-stripprobe`.

## Notes

- Your stuff lives under `%LocalAppData%\Krypton` — the `history.db` autocomplete cache and the `CEF-Cache` browser profile.
