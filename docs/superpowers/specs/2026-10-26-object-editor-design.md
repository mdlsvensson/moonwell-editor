# Object Editor: design

A desktop app, written in C# on War3Net with a Svelte interface in a WebView2 window, that does the job of World
Editor's Object Editor (units, items, abilities, buffs, upgrades, destructables and doodads) and is faster,
searchable and easier to use.

Status: **draft, not approved.** Sven skipped the open design questions on 2026-10-26 ("skip"), so every choice below
is the agent's call unless it says otherwise. Sven's own decisions are marked **Sven, 2026-10-26**:

- four answers after a reviewer read the first draft (what it edits, who it is for, what comes after editing, a test
  map);
- after the first draft (Tauri with a Rust back end) was approved and Task 1 had started, he chose to switch to **C#
  on War3Net, with the Svelte interface in a WebView2 window**. This document is the redo for that.

The agent's calls are listed together at the end ("Calls to check") so they can be read and corrected in one place.

The clickable mockup is `docs/mockups/editor.html` (open it in a browser). It is unchanged by the switch.

## Why

World Editor's Object Editor is slow to move around in, has weak search, shows one object at a time, hides field ids
until you toggle them, makes per-level ability fields a long list, and cannot change one field on many objects at once
or compare two objects. *(The list is the agent's guess at the usual complaints; Sven has not named his own.)*

Why C# (Sven, 2026-10-26, from the agent's comparison): Sven knows C# better than Rust; Moonwell is C#, so its
generator gives the app the game's standard data with no port; War3Net (Sven's fork) already reads and writes the
object files, the strings file and MPQ archives; and the later stage of reading and writing Moonwell's C# object
classes needs Roslyn, which only .NET has.

## Goals

1. **Find anything fast.** One search box (Ctrl K) over every object of every kind, by name, id, editor suffix, field
   label, field id and value. Results show as you type.
2. **Read an object at a glance.** A details panel grouped by category (Stats, Combat, Art...), a field filter, a
   "changed only" switch, field ids on demand, changed fields marked, and per-level fields as a grid with the levels
   across.
3. **Edit safely.** The right editor per field type (number, text, yes/no, a choice list, flags, an object list with a
   picker), World Editor's limits shown as warnings (a value past them still saves, as Shift+Enter allows in World
   Editor), reset a field to its base value, undo and redo for everything, and a backup before every save.
4. **Edit many at once.** A table view: pick objects (rows) and fields (columns), edit cells, set a column for all
   picked rows with a number or an expression (`+5`, `-10%`, `× 1.1`), paste a column from a spreadsheet, fill down.
5. **Compare.** Two objects side by side, an object against its base, the map against its last saved state, or the
   map against another map, showing only the fields that differ.
6. **See how objects connect.** For the selected object: its abilities as "components" you can click into, what uses
   it ("used by"), and a tooltip preview with colour codes and `<A000,DataA1>` references filled in.
7. **Keep maps working.** Read and write the map's own object files exactly, so World Editor and the game open the
   map as before; an unchanged file is written back byte for byte.
8. **Keyboard first.** Every action has a key; the mouse is never required.

## Non-goals (first version)

- Terrain, triggers, regions, imports, map options: World Editor still does those.
- Icons and models (a later stage; War3Net has a BLP reader, `War3Net.Drawing.Blp`).
- Reading or writing Moonwell's C# object classes (a later stage, with Roslyn).
- Locales other than English. Campaign files (`.w3n`).
- Classic (1.26 and older) maps: the target is 3.0 (Reforged) maps; older object file versions are read if War3Net
  reads them, but not tested.
- macOS and Linux for the first version: WebView2 is Windows only. The libraries under the window stay
  platform-neutral so another host can be added later. *(Agent's call.)*

## Anti-goals (would make it a failure even if it works)

- A map that World Editor or the game no longer opens, or opens with a value silently changed.
- Being slower than World Editor to open a map or to jump to an object.
- Pixel fonts, or any font that is hard to read.
- Changing a field the user did not touch (rewriting strings into `war3map.wts`, reordering objects, dropping a field
  the app does not know).

## Who it is for

**Sven and other modders** (Sven, 2026-10-26). Sven uses it first, while it is built; other modders use it once it is
released. So the released app may not ship Blizzard's data: it reads the game's standard data from the user's own
game files (see "The game's standard data"), and it may not need a wc3-cs checkout.

## What it edits

**A map's own object files** (Sven, 2026-10-26), straight in a map: a map folder (World Editor 3.0 can save a map as
a folder) or a `.w3x`/`.w3m` archive.

| Kind | Main | Skin | Has levels |
|---|---|---|---|
| Units | `war3map.w3u` | `war3mapSkin.w3u` | no |
| Items | `war3map.w3t` | `war3mapSkin.w3t` | no |
| Destructables | `war3map.w3b` | `war3mapSkin.w3b` | no |
| Doodads | `war3map.w3d` | `war3mapSkin.w3d` | variations |
| Abilities | `war3map.w3a` | `war3mapSkin.w3a` | levels |
| Buffs | `war3map.w3h` | `war3mapSkin.w3h` | no |
| Upgrades | `war3map.w3q` | `war3mapSkin.w3q` | levels |
| Strings | `war3map.wts` | | |

World Editor 3.0 writes every object into both the main and the skin file, with the skin side holding the fields whose
metadata row has `netsafe` = 1 (wc3-cs `docs/plans/2026-10-18-objects.md`, from the fixture's README). The app keeps
that split by Moonwell's `Field.Skin` flag, which Moonwell's build already writes by: an edit to a skin field goes to
the skin file, any other to the main file. The fixture has one case that does not fit a plain `netsafe` reading (the
doodad's `dnam` is in the main file), so the first file task checks the rule against every record of the fixture, and
of Sven's World Editor save when it arrives, before the writer relies on it.

**Moonwell maps:** the app edits the map's source folder (`map/` in a Moonwell project). The built map under `build/`
is rewritten by every build; the app says so when the opened folder is a Moonwell build output.

### Reading and writing the files

**War3Net** reads and writes the object files (`War3Net.Build.Core`: `UnitObjectData`, `AbilityObjectData` and the
others, with `BaseUnits`/`NewUnits` lists of modifications), through the published packages of Sven's fork
(`Moonwell.War3Net.Build.Core`, the same fork Moonwell builds with). The layout, for reference (checked against a hex
dump of World Editor 3.0's output, wc3-cs `moonwell/tests/Moonwell.Build.Tests/Fixtures/WorldEditorObjects`):

```
int32 version                         1, 2 or 3 (3 since 1.33)
int32 count; Object[count]            changes to standard objects
int32 count; Object[count]            custom objects (old id = base, new id = the object's)
Object: char[4] oldId; char[4] newId; version 3: int32 setCount, then setCount int32 set flags (World Editor: 1, 0);
        int32 count; Mod[count]
Mod:    char[4] fieldId; int32 type (0 int, 1 real, 2 unreal, 3 string);
        .w3a .w3q .w3d only: int32 level (variation for doodads), int32 dataPointer;
        value (int32 | float32 | float32 | NUL-terminated string); int32 end marker (kept as read)
```

War3Net does not round-trip every file exactly. Known gaps (from reading its code):

- strings are decoded as strict UTF-8, so a name with other bytes fails to read;
- anything after the two tables (some writers add an empty third table) is dropped;
- `war3map.wts` loses its BOM and CRLF line ends, which World Editor writes.

So, *(agent's calls)*:

- **A round-trip guard.** When the app opens an object file, it writes what War3Net read back to bytes in memory and
  compares them with the file. If they differ, or War3Net cannot read the file, that kind opens **read only** with a
  note saying why ("War3Net would drop 4 bytes at the end of war3map.w3a"). A map is never saved through a lossy read.
- **The strings file has its own small reader and writer** in the app (`WtsFile`), which keeps the BOM, line ends,
  comments and number padding, because names are edited through it on every World Editor map.
- **The War3Net gaps are fixed in the fork**, as separate work outside this repository (Sven's fork, his call), after
  which the guard simply stops firing.

### Map archives

A `.w3x`/`.w3m` is an MPQ archive behind a 512-byte `HM3W` header. War3Net's `War3Net.IO.Mpq` reads and writes them
(it decompresses every codec, compresses with zlib). A save rewrites only the object and string files in the archive,
keeps the `HM3W` header, and writes to a temporary file that replaces the original only when the write succeeded. The
archive milestone starts with a spike: open Blizzard's `(4)losttemple.w3m` (in the export's `maps/`), Moonwell's
`Sample.w3x` and a World Editor save; replace one file; open the result in World Editor and the game. Protected maps
War3Net cannot read open as an error, not a crash.

Folder maps need no archive code, so they come first.

### Real maps to test with

- World Editor 3.0's own output: the `WorldEditorObjects` fixture (every kind, one new object each, main and skin
  files, `.wts` with BOM and CRLF). Small.
- Moonwell's output: `MyMap/build/MyMap/` (every kind) and `Sample.w3x` (an archive).
- Blizzard's `(4)losttemple.w3m` for the archive (not committed).
- **Sven's World Editor test map** (Sven, 2026-10-26: he will make one): every tab, abilities with levels, doodad
  variations, upgrade levels, edited standard units, `TRIGSTR` names. Milestone 2 does not close without it.

## Milestones

1. **Browse:** open a map folder; tree, details panel, field filter, search. Read only.
2. **Edit and save** a map folder: every field editor, reset, undo and redo, new and copied objects, validation,
   backups. Closes only once the app's save of Sven's test map opens right in World Editor and the game.
3. **Table bulk edit** (goal 4; Sven, 2026-10-26: first after browsing and editing).
4. **Archives:** open and save `.w3x`/`.w3m`.
5. **Standard data from the game install** (reading the game's CASC storage), so a modder needs no exported data
   folder; required before any public release.
6. **Compare** (goal 5), then **used by, components and tooltips** (goal 6). *(Order of these two: agent's call.)*

Later, in the agent's suggested order: icons; "copy as Moonwell C#"; reading and writing Moonwell's C# classes;
`.w3o` import and export.

The first build plan covers milestones 1 and 2; each later milestone gets its own plan.

## How the app is put together

Two halves in one program:

- **The C# side** owns the data. It reads and writes the files with War3Net, keeps the loaded map and the game's
  standard data in memory, and answers questions. Think of it as one big Godot autoload that holds the state.
- **The web side** is the window's contents, written in TypeScript with Svelte and drawn by WebView2 (Windows'
  built-in browser engine, the same one Tauri uses on Windows). It shows things and sends what the user did to the C#
  side.

The window is a small WinForms program holding one WebView2 control *(agent's call: WinForms is the lightest
Microsoft-supported host for WebView2; the window has no other controls)*. In development it shows the Vite dev server
(live reload); in a release it shows the built web files from the app folder.

They talk two ways, over WebView2's message channel:

- **Commands**: the web side sends a request and awaits the answer, like `await` on a GDScript coroutine. For example
  `await call("object", { kind: "unit", id: "H000" })` posts `{ "id": 7, "command": "object", "args": { ... } }`;
  the C# `Bridge` finds the `object` command, runs it and posts back `{ "id": 7, "ok": true, "result": { ... } }`.
- **Events**: the C# side tells the web side that something happened, like emitting a Godot signal the UI is
  connected to. After every edit it posts `{ "event": "sessionChanged", "payload": { "dirty": true } }` and the
  status bar, which subscribed with `on("sessionChanged", ...)`, updates.

The `Bridge` takes a JSON string and returns a JSON string, so it is tested without a window. Commands run off the
window's thread, one at a time.

The C# side does the heavy work (parsing, search, bulk edits, comparing), so the web side only holds what is on
screen: lists are virtualised and a search returns the top results.

### Projects and folders

```
wc3-object-editor/
  ObjectEditor.slnx
  Directory.Build.props        net10.0, nullable, warnings as errors (as Moonwell)
  src/
    ObjectEditor.Files/        object files through War3Net with the round-trip guard; WtsFile; MapFolder
    ObjectEditor.GameData/     the game's standard data, made by Moonwell's generator, cached
    ObjectEditor.Model/        the edit model: resolved values, edits, undo, save, search
    ObjectEditor.App/          the window (WinForms + WebView2) and the Bridge
  web/                         the Svelte front end (Vite)
  tests/
    ObjectEditor.Files.Tests/  ObjectEditor.GameData.Tests/  ObjectEditor.Model.Tests/  ObjectEditor.App.Tests/
    fixtures/                  real map files (Sven's and Moonwell's only)
  docs/
```

`Files`, `GameData` and `Model` are plain libraries with no window, so the tests are plain `dotnet test`.

## The game's standard data

The app needs every standard object's values and every field's label, category, type, limits and choices. Moonwell's
generator already works all of this out from the game's data files (`war3.w3mod`): `ExportTables` merges the tables,
func, skin and string files per object; `Fields.Of` gives each kind's fields with their type, store type
(`int`/`real`/`unreal`/`string`), per-level flag, data pointer, skin flag, the abilities a field belongs to and World
Editor's limits; `StandardObjects.Of` gives each standard object's values keyed `fieldId` or `fieldId:level`, its
race, campaign flag, hero flag and balance sets. All are public in `Moonwell.Generator`.

**The app calls them directly** (agent's call): `ObjectEditor.GameData` runs `ExportTables`, `Fields` and
`StandardObjects` on a game data folder at start, adds what the editor needs that `Field` lacks (`category` and `sort`
from the metadata rows, labels from `WESTRING_` keys, choice labels from `ui/uniteditordata.txt`, per-graphics-mode
values), and caches the result in `%LOCALAPPDATA%\ObjectEditor\gamedata\` keyed by the folder and its files' dates,
so later starts are fast. Nothing of Blizzard's is shipped or committed.

- **Until milestone 5** the game data folder is an exported folder such as Sven's
  `C:\Users\mdlsvensson\Downloads\Work\war3.w3mod`, chosen once and kept in the app's settings.
- **Milestone 5** reads the same files from the user's Warcraft III install (CASC storage), so modders need no export.
  `War3Net.IO.Casc` is empty; the milestone starts with a spike of the C# CASC readers (for example CASCLib from
  CASCExplorer), checking that one reads Warcraft III Reforged.
- **Referencing the generator:** during development a project reference into the wc3-cs checkout
  (`Moonwell.Generator` is not a package today, `IsPackable` false). Before a public release it must be a package
  others can restore; publishing it is Sven's call, asked then.

Base values for a map's balance set (`melee_v0`, `custom_v0`, `custom_v1`): milestones 1 and 2 show the base tables
(the status bar says so); a switch, and reading the map's own setting from `war3map.w3i`, come later. *(Agent's call.)*

## The map's data and how a value is shown

The model keeps War3Net's objects as read (every record, in file order, unknown fields and end markers included) and
resolves a value on demand:

- A **standard object** (`hfoo`): the map's change to it if any, else the standard value.
- A **custom object** (`h001`, based on `hfoo`): its own change if any, else the **standard** base's value, not the
  map's change to the base: the game takes unset fields from the game's standard object (wc3-cs
  `docs/game-facts.md`, probe `moonwell-0.2.3-inherit`).
- **Strings** that read `TRIGSTR_012` are shown from `war3map.wts`; editing one changes the `.wts` entry and keeps the
  reference. A string that was inline stays inline; a new string is written inline (Moonwell writes strings inline
  and the game reads them). *(Agent's call.)*

The tree, as in World Editor: Custom (this map's new objects) first, then Standard by race, then Melee / Campaign,
then Units / Buildings / Heroes / Special for units. The object's header shows its chain (Unit › Hero › Paladin ›
Champion). *(The "Unreal-style object types plus components" Sven likes is read as: a class chain on every object,
and a unit's abilities shown as components you click into. Agent's reading; Sven to confirm.)*

## Editing

- Every edit is a small operation the model can undo: set a field (kind, object, field, level, new value, old value),
  reset a field, create, copy or delete an object, change an object's id. Undo and redo walk the list.
- Setting a field to its base value removes the modification instead of writing a same-value record (as World Editor
  does), but only when the base is the same in every graphics mode *(agent's call)*: the game keeps some standard
  values per graphics mode (`:sd`, `:hd`, `:de`; in mode 2 it reads `:de`, else `:hd`, per wc3-cs
  `docs/game-facts.md`) and `StandardObjects` resolves those to one value, so the game data keeps the per-mode values
  where they differ, and a record whose value matches only some modes is kept.
- New objects take World Editor's ids (`h000` for a unit, `H000` for a hero, `I000`, `B000` for destructables and
  buffs, `D000`, `A000`, `R000`), the lowest free counting `0-9` then `A-Z`, or an id the user types; ids are checked
  for clashes per kind.
- Saving writes only the files whose bytes changed, after copying their previous versions to a backup folder next to
  the map (`<map>.backup/<date-time>/`), keeping the last 10. *(Agent's call.)*
- If the map changed on disk since it was opened (World Editor saved it), saving stops and offers to reload.

## Validation

Each field's rules (`Min`, `Max`, `MaxLength`, `MaxEntries`, `Required`, `NoNegative`, from Moonwell's
`FieldRules`) are checked as the user types and on save. A value past a limit is allowed with a red mark and a line
of text ("Above World Editor's limit of 99999."). Text in a number field is refused.

## Look and feel

- **Theme:** Vitesse Dark (the tokens wc3-dev used: `#121212` background, `#dbd7ca` text, `#4d9375` green accent,
  `#e6cc77` yellow for changed values, `#cb7676` red for warnings).
- **Fonts:** readable only, never pixel fonts. Interface text in Google Sans (Nunito for headings), as in steam2fiddy
  and wc3-dev; ids, values and code in JetBrains Mono. Bundled through fontsource, so no network is needed. *(Agent's
  call; "JetBrains Mono (Nerd Font)" is read as JetBrains Mono, since icons come from Lucide.)*
- **Layout** (see the mockup): a top bar with the map name, the search box and the views (Details, Table, Compare);
  a left tree with the seven kinds and a filter; the details panel in the middle; a right panel with components,
  "used by" and the tooltip preview; a status bar with unsaved changes and the game data version.
- **Keys** (first set): Ctrl K search, Ctrl S save, Ctrl Z / Ctrl Y undo and redo, Ctrl D field ids (as in World
  Editor), Ctrl F filter fields, Ctrl Shift N new object from the selected one, F2 rename, Delete delete, Alt ←/→ back
  and forward through visited objects.

## Stack

*(Agent's calls, following Moonwell's and steam2fiddy's habits, within Sven's choice of C# + Svelte + WebView2.)*

- C#: .NET 10, nullable on, warnings as errors, implicit usings, `.editorconfig` as Moonwell's (4 spaces, LF).
  `Moonwell.War3Net.Build.Core` from nuget.org; `Microsoft.Web.WebView2` for the window. xUnit for tests, as Moonwell.
- Front end: Svelte 5, TypeScript, Vite, Tailwind CSS 4, Lucide, fontsource. Exact versions pinned. Biome (2 spaces,
  100 columns). Vitest, with the bridge mocked.
- npm (no pnpm), LF line ends.
- Release: a self-contained Windows build (`dotnet publish`), about 60-100 MB; WebView2 is part of Windows 10 and 11.

## How it is tested

- **Round trip, every file of every real map:** the guard's check (War3Net read, written back, same bytes) holds for
  every object file of every fixture; the `WtsFile` reader and writer return the same bytes for every `.wts`.
- **The guard itself:** a file with a trailing table, a non-UTF-8 name and a truncated file each open read only with
  the right note, never a crash.
- **Edits:** an edit, saved, then read again, shows the new value and nothing else changed (every other record
  equal).
- **Game data:** known values (`hfoo` 420 life, `Amls` cooldown 30, hero fields only on heroes) and counts per kind.
- **The bridge:** each command's JSON in and out, without a window.
- **In World Editor and the game:** a map edited and saved by the app opens in World Editor with the changes, and
  plays with them (wc3-cs `test-map` skill), before the first release and whenever the writer changes.
- **The window:** run the app and check it with screenshots.

## What is left to the builder

Names inside the code, the exact JSON shapes of the commands, the search ranking, how the tree is cached, the cache
file's format, the backup folder's exact name, and any detail the plan does not fix, as long as the goals and
anti-goals hold.

## Sven's answers (2026-10-26)

1. The first version edits the map's object files.
2. It is for Sven and other modders.
3. Table bulk edit comes first after browsing and editing.
4. Sven will save a World Editor test map with many object edits; milestone 2 waits for it. Until it exists, saving
   ships behind a warning ("saving is tested only on small maps").
5. C# on War3Net, with the Svelte interface in a WebView2 window (replacing Tauri and Rust).

## Open questions (Sven's, not blocking the first plan)

1. Licence and repository for the public release (a GitHub repository, pushing and publishing are each his call).
2. Publishing `Moonwell.Generator` as a package, needed before a public release.
3. Fixing War3Net's round-trip gaps in his fork (strings, trailing tables, `.wts`).
4. What annoys him most in the Object Editor, if anything beyond the goals above.

## Calls to check

Every call below is the agent's:

- No project format of its own; folder maps before archives.
- The window: WinForms holding one WebView2; Windows only for now, libraries kept platform-neutral.
- The game data: Moonwell's generator called at start on an exported folder, cached; the install's CASC storage in
  milestone 5.
- A round-trip guard: a kind War3Net cannot read back exactly opens read only.
- The strings file read and written by the app's own `WtsFile`, not War3Net.
- Custom objects inherit from the standard base, not the map's change to it (as the game does).
- Strings: `TRIGSTR` entries edited in `war3map.wts`, inline strings stay inline, new strings inline.
- A field set back to its base is removed, only when the base is the same in every graphics mode.
- Backups of the last 10 saves next to the map.
- The base tables only in the first version; the balance set is a later switch.
- "Unreal-style types plus components" read as the class chain plus abilities as components.
- A Moonwell map is edited in its `map/` source folder, never its build output.
- Stack details: xUnit, Svelte 5, Tailwind 4, Biome, Vitest; Google Sans, Nunito, JetBrains Mono; Lucide.
