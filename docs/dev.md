# Developing the Object Editor

Tools: .NET 10 SDK, Node 24 with npm, the WebView2 runtime. Commands run from the repository root unless a `cd` says
otherwise.

## First time

```
cd web
npm install
```

The game data is read by Moonwell's `Moonwell.GameData` package, and the map's object files by its `Moonwell.MapFiles`,
both restored from nuget.org like the other packages; `MoonwellVersion` in `Directory.Build.props` sets their version.

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

## The game

The editor reads the game data (fields, standard objects, lists), the tree's icons and the icon and model pickers
from the Warcraft III install's own storage, with Moonwell's reader (`Moonwell.GameData`). It uses the folder chosen
in Settings → Game data, else the folder in `MOONWELL_WAR3`, else `C:\Program Files (x86)\Warcraft III`. Without one
it asks for the folder. It takes only the install's folder (the one with `.build.info`), never a `war3.w3mod` export,
which may be incomplete.

The game data is cached in `%LOCALAPPDATA%\MoonwellEditor\gamedata`, once per build of the game (its build key in
`.build.info`), so an update of the game builds it again.

The tests that need the game read the install the same way and check nothing when there is none. Only the
map-archive test that opens Blizzard's `(4)losttemple.w3m` takes it from a folder, `maps` in the folder in
`MOONWELL_EDITOR_GAME_DATA`.

## The map view

The Terrain Editor button shows the whole map, read only (design: wc3-cs `docs/plans/2026-10-29-map-view-design.md`).
`MoonwellEditor.Map` reads the map's `war3map.w3e`, `war3map.doo`, `war3mapUnits.doo`, `war3map.w3r`, `war3map.w3c`
and `war3map.w3i` with War3Net, and works out the corner heights, the tiles' texture pieces, the cliff and ramp
models and each placed thing's model and trigger name (`gg_unit_hfoo_0001`). The page asks for it with the `mapScene`
command (a graphics mode), then fetches the terrain's bytes from `https://game.moonwelleditor/terrain?mode=` (their
layout is in `TerrainBytes.cs`). It draws with its own WebGL2 code in `web/src/lib/map/`, war3-model only reading the
models, and picks by drawing each thing's number into a hidden picture. The page logs `[timing] map scene`, `map
ground` and `map models` lines.

It lights the map as World Editor's Lighting: Noon does: `DayLights.cs` reads the tileset's day and night light
models (version 1800 lights, read by hand) at the middle of their day, and HD and Definitive Edition add the
environment map's colours as the sky (`gl/lighting.ts` has the shading and its scales, set by eye against World
Editor). HD and Definitive Edition cast the sun's shadows from a depth map drawn around the camera's target before
each frame (`gl/shadows.ts`); Classic darkens the ground by the map's baked `war3map.shd` and lays each unit's shadow
picture under it (`gl/unitShadows.ts`). Each model's Stand animation is sampled into textures the shaders read
(`map/animation.ts`); while anything moves the view draws every frame, each pass only the copies it can see
(`gl/culling.ts`).

To look into the page of a running exe (its console, its WebGL calls), start it with WebView2's debugging port open
and attach Chrome's DevTools or a script to `http://127.0.0.1:9333`:

```
WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--remote-debugging-port=9333" src/MoonwellEditor.App/bin/Release/net10.0-windows/win-x64/publish/MoonwellEditor.exe "C:\Maps\Arena.w3x"
```

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
