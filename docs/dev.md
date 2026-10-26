# Developing the Object Editor

Tools: .NET 10 SDK, Node 24 with npm, the WebView2 runtime. Commands run from the repository root unless a `cd` says
otherwise.

## First time

```
cd web
npm install
```

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

A single-file exe (`-p:PublishSingleFile=true`) does not build yet: Moonwell's generator code, linked in from wc3-cs,
reads `Assembly.Location`, which is always empty in a single-file app, and warnings are errors.

## Tests and checks

```
dotnet test
cd web && npm test
cd web && npm run check
```

`dotnet test` runs every test project in the solution. `npm run check` is Biome plus svelte-check.
