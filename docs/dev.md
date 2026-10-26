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
$env:OBJECT_EDITOR_DEV_URL = "http://localhost:5173"
dotnet run --project src/ObjectEditor.App

# Git Bash
OBJECT_EDITOR_DEV_URL=http://localhost:5173 dotnet run --project src/ObjectEditor.App
```

Without `OBJECT_EDITOR_DEV_URL` the window shows the built page instead: run `npm run build` in `web/` first, then
`dotnet build` copies `web/dist` next to the exe as `wwwroot`. (`dotnet build` does not run npm.)

## Open a map at start

The app takes a map folder as its first command-line argument and opens it once the game data is ready, as if it had
been picked with "Open map…" (handy for checking the window without the folder dialog, and for "open with"):

```
dotnet run --project src/ObjectEditor.App -- "C:\Maps\Arena"
src/ObjectEditor.App/bin/Debug/net10.0-windows/ObjectEditor.App.exe "C:\Maps\Arena"
```

The page asks for it with the `startup` command, which gives the folder once; a page loaded again (live reload) does
not open it again.

The page logs how long opening a map, a kind's tree and an object take to the console, as `[timing] …` lines.

## Tests and checks

```
dotnet test
cd web && npm test
cd web && npm run check
```

`dotnet test` runs every test project in the solution. `npm run check` is Biome plus svelte-check.
