# Contributing to Krypton

Thanks for taking a look — Krypton is a small, solo-maintained project, so this doc is short on purpose.

## Before you write any code

Krypton is deliberately scoped small: a native WinForms shell around CefSharp/Chromium, nothing more. A few hard rules:

- The app's own UI (toolbar, tabs, window frame) is native WinForms only. No HTML/CSS/JS anywhere in the app shell itself — the only web content in this repo is the new-tab page, which is rendered by the browser engine, not part of the shell.
- The feature set is intentionally minimal — address bar, back/forward/refresh, tabs, that's the core of it. If you're planning something bigger (bookmarks, settings, extensions, etc.), open an issue first to check it's something this project actually wants before investing time in a PR.
- CefSharp is x64-only. Don't switch the project to AnyCPU or add single-file publishing — CEF can't load its native libraries from a single-file bundle and the app won't start.

## Reporting bugs

Open an issue. Include your Windows version, what you did, what you expected, and what actually happened. Screenshots help a lot for UI issues.

## Pull requests

1. Fork the repo, branch off `main`.
2. Keep PRs focused — one change per PR, not a bundle of unrelated fixes.
3. Make sure `dotnet build` passes cleanly before opening the PR.
4. Open the PR against `main` and describe what changed and why.

This is a one-person project, so review may take a little while — I'll get to it, but there's no dedicated team behind this.

## License

By contributing, you agree your contribution is licensed under the project's [MIT License](./LICENSE).