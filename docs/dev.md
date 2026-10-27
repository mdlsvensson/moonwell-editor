# Developing the Object Editor

Tools: .NET 10 SDK, Node 24 with npm, the WebView2 runtime. Commands run from the repository root unless a `cd` says
otherwise.

## First time

```
cd web
npm install
```

The game data is read by Moonwell's `Moonwell.GameData` package, restored from nuget.org like the other packages.

## Run the app with live reload

Two terminals. The first runs the Vite dev server (it stays on port 5173):

```
cd web
npm run dev
```

The second starts the window and points it at Vite (Debug build only):

```
# PowerShell
$env:MOONWELL_EDITOR_DEV_URL = "http://localhost:5173"
dotnet run --project src/MoonwellEditor.App

# Git Bash
MOONWELL_EDITOR_DEV_URL=http://localhost:5173 dotnet run --project src/MoonwellEditor.App
```

Without `MOONWELL_EDITOR_DEV_URL` the window shows the built page instead: run `npm run build` in `web/` first, then
`dotnet build` copies `web/dist` next to the exe as `wwwroot`. (`dotnet build` does not run npm.)

## Open a map at start

The app takes a map, a folder or an archive (`.w3x`, `.w3m`), as its first command-line argument and opens it once the
game data is ready, as if it had been picked with "Open map…" (handy for checking the window without the dialogs, and
for "open with"):

```
dotnet run --project src/MoonwellEditor.App -- "C:\Maps\Arena"
dotnet run --project src/MoonwellEditor.App -- "C:\Maps\Arena.w3x"
src/MoonwellEditor.App/bin/Debug/net10.0-windows/MoonwellEditor.exe "C:\Maps\Arena"
```

The page asks for it with the `startup` command, which gives the map once; a page loaded again (live reload) does
not open it again.

The page logs how long opening a map, a kind's tree and an object take to the console, as `[timing] …` lines.

## Build the exe

Build the page first, then publish a Release build:

```
cd web && npm run build
dotnet publish src/MoonwellEditor.App -c Release -r win-x64 --self-contained false
```

The app is then `src/MoonwellEditor.App/bin/Release/net10.0-windows/win-x64/publish/MoonwellEditor.exe`, about
6 MB with the DLLs and the page (`wwwroot`) next to it: copy the whole `publish` folder to move it. It needs the .NET
10 Desktop Runtime (the SDK includes it); `--self-contained true` bundles .NET instead, for a PC without it, at about
70 MB more.

A single-file exe (`-p:PublishSingleFile=true`) builds, but it is not one file yet: the page (`wwwroot`), WebView2's
`WebView2Loader.dll` and `runtimes` stay beside it. It has not been run.

## Icons and models

The tree's icons and the icon and model pickers read the Warcraft III install's own storage, from
`C:\Program Files (x86)\Warcraft III`, or the folder in `MOONWELL_EDITOR_GAME`. Without an install the editor works,
with no icons. `MoonwellEditor.Assets.Tests` reads the install too, and checks nothing when there is none.

## Interface text

Every text the page writes itself (buttons, tooltips, headings, dialogs, notices, errors) is in
`web/src/text/strings.json`, one key per text, named for where it shows (`titleBar.back`, `settings.tab.hotkeys`).
Change the text after the colon and keep each `{name}`, which the page fills in; a count has two keys, `.one` for 1
and `.other` for any other. Rebuild with `npm run build` (in `web`). The code reads them with `t(key)` from
`web/src/lib/text.ts`, and `npm test` checks that each call fills the placeholders its text has.

## Tests and checks

```
dotnet test
cd web && npm test
cd web && npm run check
```

`dotnet test` runs every test project in the solution. `npm run check` is Biome plus svelte-check.
