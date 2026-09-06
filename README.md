# Krypton

Minimal Windows-only web browser built with C# (.NET 8), WinForms, and CefSharp (Chromium). Native WinForms UI only — no web tech in the app shell itself.

## Features

1. Address-bar navigation (missing scheme gets `https://`, bare terms go to Google)
2. Back / Forward / Refresh with history-aware button states
3. Loading indicator under the address bar (active tab only)
4. Address-bar auto-sync on in-page navigation
5. Favicon + page title per tab and next to the address bar
6. Tabs: open (`+` next to the last tab) / close (per-tab `×`); closing the last tab exits

Nothing else — no bookmarks, history, downloads, settings, or extensions by design.

## Requirements

- Windows 10/11, x64
- .NET 8 SDK ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- VC++ 2022 x64 runtime (required by CEF)

## Build / run

```powershell
dotnet build
dotnet run --project Krypton\Krypton.csproj -c Debug
```

CefSharp requires x64, so the project pins `<PlatformTarget>x64</PlatformTarget>` + `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` — don't switch it to AnyCPU. See `AGENTS.md` for contributor notes.
