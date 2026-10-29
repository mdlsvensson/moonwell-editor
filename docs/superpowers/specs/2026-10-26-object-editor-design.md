# Object Editor: design

A desktop app, written in C# on War3Net with a Svelte interface in a WebView2 window, that does the job of World
Editor's Object Editor (units, items, abilities, buffs, upgrades, destructables and doodads) and is faster,
searchable and easier to use.

Renamed **Moonwell Editor** (Sven, 2026-10-26): the app shows that name, the exe is `MoonwellEditor.exe`, and the
projects, namespaces and environment variables are `MoonwellEditor.*` and `MOONWELL_EDITOR_*`. This document and the
plans keep the old `ObjectEditor.*` names as they were written; "the Object Editor" in them is World Editor's.

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
   label, field id and value. Results show as you type. Sven, 2026-10-27: the search box and Ctrl K are removed; the
   same search stays behind the + picker of an object list.
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

Up to `6.1.2-moonwell.3`, War3Net did not round-trip every file exactly:

- a string that is not valid UTF-8 was read with replacement characters and written back with them;
- anything after the two tables (some writers add an empty third table) was dropped;
- `war3map.wts` lost its BOM and CRLF line ends, which World Editor writes.

Sven's fork fixed all three in `6.1.2-moonwell.4` (2026-10-26), which the app uses: an unchanged string keeps its
bytes, trailing tables are kept, and `war3map.wts` keeps its BOM and line ends. No file is known that it cannot write
back exactly, so the guard below is now a safety net for files nobody has met yet.

So, *(agent's calls)*:

- **A round-trip guard.** When the app opens an object file, it writes what War3Net read back to bytes in memory and
  compares them with the file. If they differ, or War3Net cannot read the file, that kind opens **read only** with a
  note saying why ("War3Net would drop 4 bytes at the end of war3map.w3a"). A map is never saved through a lossy read.
- **The strings file has its own small reader and writer** in the app (`WtsFile`), which keeps the BOM, line ends,
  comments and number padding, because names are edited through it on every World Editor map.
- **The War3Net gaps are fixed in the fork** (done: `6.1.2-moonwell.4`). The tests stand in a lossy War3Net through an
  internal write-back hook on `ObjectFile.Read`, so the read-only path stays tested.

### Map archives

A `.w3x`/`.w3m` is an MPQ archive, behind a 512-byte `HM3W` header in World Editor's saves (Blizzard's Reforged
melee maps and Moonwell's builds start with the MPQ header itself). War3Net's `War3Net.IO.Mpq` reads and writes them
(it decompresses every codec, compresses with zlib). A save rewrites only the object and string files in the archive,
keeps the `HM3W` header, and writes to a temporary file that replaces the original only when the write succeeded. The
archive milestone starts with a spike: open Blizzard's `(4)losttemple.w3m` (in the export's `maps/`), Moonwell's
`Sample.w3x` and a World Editor save; replace one file; open the result in World Editor and the game. Protected maps
War3Net cannot read open as an error, not a crash.

Folder maps need no archive code, so they come first.

#### Milestone 4: archives in detail

*The agent's calls* (written 2026-10-26, after milestone 3), for Sven to correct:

- **Opening.** "Open map…" offers a folder or an archive (`.w3x`, `.w3m`); the command line and the recent maps take
  either. A file that does not end `.w3x` or `.w3m` is not opened ("Not a map: …"). The app reads the object files
  and `war3map.wts` out of the archive into memory; everything else in the archive stays where it is, untouched.
- **What a save writes.** A new archive with the same files: every file the save did not change is carried over as
  War3Net's `MpqArchiveBuilder` carries it (from the original archive), the changed object and strings files are
  replaced with the same compression flags they had, a missing one is added with War3Net's default. Whatever comes
  before the MPQ header (World Editor's 512-byte `HM3W` header, when the map has one) is copied byte for byte in
  front. War3Net writes `(listfile)` and `(attributes)` anew, so the archive's own bytes change; the files in it may
  not. A save drops `(signature)` and any bytes after the archive (Blizzard's `NGIS` signature block, or a tool's
  junk): a signature cannot stay valid after an edit. Where War3Net would leave a hash-table slot empty that the
  original filled (a deleted entry, an entry pointing at no block, `(signature)`), the save writes a deleted entry, so
  the game's lookup, which stops at the first empty slot, still reaches every file.
- **Checked before it replaces anything.** The new archive is written to a temporary file beside the map, then opened
  again and every file compared with what it should hold: each changed file its new bytes, every other file (named
  or not) its old stored bytes and flags, and the same number of files. Each file is looked up the way the game looks
  it up (both hash tables read raw: from the slot its name hashes to, forward to the first empty slot; a file with no
  known name from wherever the game may have started for it), so a file the game could no longer find refuses the
  save. Only then does it replace the map (`File.Replace`). A failed check, or a write error, leaves the map as it
  was and says why.
- **Backup.** Before replacing, the whole previous archive is copied to `<map>.backup\<date-time>\<name>.w3x.bak`
  (`.bak`, so the game's map list does not show it), keeping the newest 5 (archives are larger than the object files
  a folder backup copies).
- **Changed on disk.** The archive's write time and size are its stamp, as a folder's files are; World Editor saving
  the map while the app has it open is refused at save, as for folders, and checked again once the save holds the
  map, so a save World Editor makes in between is refused too. A map World Editor holds open cannot be replaced; the
  message says to close it there.
- **Refused or read only.** An archive War3Net cannot open (a protected map, a damaged one) is refused at open with
  the reason, never a crash; so is a protected map whose hash entries share one block of data (War3Net would keep
  one name of each). An archive that opens is read only where a save could not keep it: a kind whose object files
  War3Net cannot write back exactly (the existing guard), and an object file or `war3map.wts` stored under a locale
  other than the neutral one (a save would add a neutral copy beside it; edited text then goes into the object
  files).
- **The spike decides the library.** The milestone starts with a throwaway test of War3Net's MPQ code on Moonwell's
  `Sample.w3x`, Blizzard's `(4)losttemple.w3m` and a World Editor archive save (Sven to save his test map as an
  archive). If War3Net fails it, the fallback is StormLib (MIT, the library most Warcraft tools use) through a small
  P/Invoke layer, decided then with Sven.

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
   backups. Closes only once the app's save of Sven's test map opens right in World Editor and the game. *(Done
   2026-10-26: World Editor check passed, `docs/checks.md`; the game check left out by Sven.)*
3. **Table bulk edit** (goal 4; Sven, 2026-10-26: first after browsing and editing).
4. **Archives:** open and save `.w3x`/`.w3m`.
5. **Standard data from the game install** (reading the game's CASC storage), so a modder needs no exported data
   folder; required before any public release.
6. **Compare** (goal 5), then **used by, components and tooltips** (goal 6). *(Order of these two: agent's call.)*

Later, in the agent's suggested order: icons; "copy as Moonwell C#"; reading and writing Moonwell's C# classes;
`.w3o` import and export.

The first build plan covers milestones 1 and 2; each later milestone gets its own plan. Milestone 3's design is in
"The table (milestone 3)" below.

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
moonwell-editor/
  ObjectEditor.slnx
  Directory.Build.props        net10.0, nullable, warnings as errors (as Moonwell)
  src/
    ObjectEditor.Files/        object files through War3Net with the round-trip guard; WtsFile; MapFolder
    ObjectEditor.GameData/     the game's standard data, read by Moonwell.GameData, cached
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
  CASCExplorer), checking that one reads Warcraft III Reforged. Done 2026-10-28 (see Sven, 2026-10-28).
- **Referencing the generator:** Sven, 2026-10-27: the editor is independent of Moonwell. The reading part of the
  generator is its own package, `Moonwell.GameData` (from 0.3.0-preview.1), which Moonwell's generator and the editor
  both use; the editor no longer compiles anything from the wc3-cs checkout.

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

The tree, as in World Editor: Standard by race, then Melee / Campaign, then Units / Buildings / Heroes / Special for
units, then Custom (this map's new objects) last (corrected 2026-10-27 from World Editor's screen, which had been
read as Custom first). The object's header shows its chain (Unit › Hero › Paladin ›
Champion). *(The "Unreal-style object types plus components" was read as a class chain plus a unit's abilities as
components. Sven, 2026-10-27: that was about Moonwell, not this app. The right panel shows **Uses**, every object
the object's fields name, and **Used by**, every object whose fields name it.)*

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

## The table (milestone 3)

Goal 4, as the mockup's Table view shows it. Sven chose it as the next milestone (2026-10-26); the details below are
*the agent's calls* (written 2026-10-26, after milestone 2), for Sven to correct:

- **One kind at a time.** The kind buttons pick the table's kind, as in the details view.
- **Rows** are that kind's objects that pass the row filter: the tree's filter text (name or id) and the group chips
  "Custom", "Changed" and the tree's top groups (races). The default is the custom objects, or the selected object's
  group when the map has none. Clicking a column header sorts by it (numbers as numbers); a row's name opens it in the
  details view.
- **Columns** are fields the user adds from a picker that searches the kind's fields by label, id and category. A
  per-level field is one column per level (`Cooldown · 1`, `· 2`, ...); the picker adds level 1 or all levels up to the
  most any shown row has. Units start with gold, lumber, hit points, damage base, armor and attack cooldown; items with
  gold, lumber and level; abilities with mana cost, cooldown and range at level 1; destructables with hit points;
  doodads, buffs and upgrades with none. The columns chosen per kind are remembered on this computer (the window's
  own storage).
- **A cell** shows the value as the details view does (choice labels, names of listed objects), yellow when the map
  changes it, red with the warning text when past World Editor's limit, grey when the field does not apply to that
  object (a hero field on a footman) or the object is read only. Enter, F2 or typing edits it with the same editors as
  the details view.
- **Selection** works as in a spreadsheet: click, Shift+click and Shift+arrows select a block; Ctrl A selects all
  shown cells.
- **Bulk changes**, each one step of undo, all or nothing (if any cell refuses, for a read-only object or a value its
  field cannot hold, nothing changes and the message names the first cell):
  - the bar above the table sets the selected cells to a value or an expression: `50` (or `=50`), `+5`, `-5`, `+10%`,
    `-10%`, `*1.1` (also `x1.1`, `×1.1`), `/2`. Whole-number fields round to the nearest whole number, halves away
    from zero; decimal fields round to 4 decimal places. Text and choice fields take a plain value only;
  - Ctrl V pastes a block copied from a spreadsheet (tab-separated rows) starting at the selected cell, going down
    the shown rows and right across the columns; Ctrl C copies the selected cells the same way, with the values as the
    data spells them, so they paste back unchanged;
  - Ctrl D fills the top cell of each selected column down the selection (in the table only; in the details view
    Ctrl D still shows field ids);
  - Delete resets the selected cells to their standard values (in the table only; in the details view Delete still
    deletes the object).
- A bulk change goes through the model as one edit, so undo and the "changed objects" count treat it as one.

## Validation

Each field's rules (`Min`, `Max`, `MaxLength`, `MaxEntries`, `Required`, `NoNegative`, from Moonwell's
`FieldRules`) are checked as the user types and on save. A value past a limit is allowed with a red mark and a line
of text ("Above World Editor's limit of 99999."). Text in a number field is refused.

## Look and feel

- **Theme:** Kanagawa Dragon by default since 2026-10-29 (Sven, after a few hours of Kanagawa Wave): `#0d0c0c`
  background, `#c5c9c5` text, `#8ba4b0` blue-grey accent, `#c4b28a` yellow for changed values, `#c4746e` red for
  warnings. Vitesse Dark (the tokens wc3-dev used) was the default before and is still one of the themes.
- **Fonts:** readable only, never pixel fonts. Since 2026-10-29 (Sven) interface text in Apple's New York and headings
  in SF Pro where Windows has them installed; Apple's licence allows them only for designing for Apple's systems, so
  the editor does not ship them and ships Inter for where they are not (the agent's call). The game's tooltips (the
  side panel's and the tooltip editor's) are in Inter everywhere. Google Sans with Nunito headings before, as in
  steam2fiddy and wc3-dev. Ids, values and code in JetBrains Mono. Inter and JetBrains Mono are bundled through
  fontsource, so no network is needed. *(Agent's call; "JetBrains Mono (Nerd Font)" is read as JetBrains Mono, since
  icons come from Lucide.)*
- **Layout** (see the mockup): a top bar with the map name, the search box and the views (Details, Table, Compare);
  a left tree with the seven kinds and a filter; the details panel in the middle; a right panel with components,
  "used by" and the tooltip preview; a status bar with unsaved changes and the game data version.
- **Sven, 2026-10-27:**
  - The title bar is the page's own, thin as Windows draws a maximized window's (23 px), maximized or not: Windows
    draws the caption of a window that is not maximized 8 px taller and cannot be made thinner. The window keeps its
    frame (resize borders, snapping, shadow, rounded corners); the bar is a WebView2 drag region, and a 4 px strip
    above the page resizes from the top edge while the window is not maximized.
  - The details panel lays fields out as World Editor does: the value starts right after the widest name, at the
    left of a box as wide as the value (a multi-line text takes the row), with ↺ after it. Checkboxes and selects
    have the theme's look.
  - A bar floats at the bottom of the details panel and never moves: New, "Changed only", "Field ids", Collapse all
    and Expand all, as icons named by their tooltips. Copy and Delete stay at the top right.
  - In the tree, Standard opens and closes like Custom, and both headings look the same.
  - A folder lists its units by `unitClass` in `unitui.slk` (`HUnit01`, `HUnit02`...), World Editor's order, and a
    unit's campaign and special come from that table too: the skin text gives the High Elf Archmage (`Haah`)
    campaign 0 against the table's 1, so it is in Human > Campaign > Heroes. World Editor shows a campaign hero by
    its proper name (Haah as Kelen the Seeker), and sorts units of one class (or none) by that name; a class of one
    space (Garek's) comes first.
  - An ability's Effects (`effectList`) are buff ids, as its Buffs are: the editor types them as a buff list
    (Moonwell's reader leaves `effectList` a plain text).
  - The title bar has back and forward at the left (and the mouse's back and forward buttons walk the history too).
    Uses and Used by open and close; the tooltip preview's place is back, for a later version.
  - Button positions get a command card (4 wide, 3 high): a click sets X and Y as one edit; other values are typed.
  - Settings (in the header's menu since 2026-10-28; the gear before): a theme (Kanagawa Wave and Dragon, Vitesse Dark, Gruvbox Dark, Dracula,
    Catppuccin Mocha and Macchiato, Tokyo Night, Nord, One Dark, Rose Pine, Everforest Dark, Solarized Dark),
    remembered in the window's storage, and the open map's details, with its Data Set and Data Version from
    `war3map.w3i` and the tables the game reads under them. The values shown are still the base tables'.
  - The logo is wc3.dev's mark (`wc3-dev/public/favicon.svg`), as the exe's icon, the page's and the title bar's.
  - Icons and models come from the Warcraft III install's CASC storage, read by the editor's own reader (ported from
    a scratch reader; CascLib's .NET wrapper is an alpha with a native DLL; in Moonwell's package since 2026-10-28),
    the map's imports first. The tree shows each object's
    icon; icon fields show theirs and get a picker of the game's command and passive buttons; model fields get a
    picker with a turning preview (war3-model, with the CAMS and LITE chunks of the game's MDX 1800 models left out).
  - The values and objects follow the map's Data Set and Data Version, as World Editor's do: the values of the
    balance set the option reads (Moonwell's reader gives each object's differences per set), and no standard object
    that set leaves out. The Shaman's hit points read 350, 260, 335 and 355 under the four options, as seen in the
    game. Values show as World Editor shows them: True or False, a list by its objects' names, a real with three
    decimals (a real typed as `0.150` is not a change from `0.15`).
  - A graphics mode (Classic, Reforged, Definitive Edition; the default is Definitive Edition, remembered): icons and
    models come from the mode's own tier first (`_HD.w3mod`, `_DE.w3mod`), and a value the game keeps per mode is the
    mode's (the Priest's icon is `BTNPriest` in Classic, `BTNPriestV1` otherwise). The icon picker lists the mode's
    tier too.
  - The right panel opens with the object's model (a team colour to pick from the 24 players') and its tooltip as the
    game shows it, then Uses and Used by; each opens and closes.
  - The title bar: the logo, back and forward (held or right-clicked, a list of the history; going back puts the
    fields' scroll where it was), the map's name from its info file, Folder or Archive, Exp or RoC (an archive by its
    extension, a folder by its info file's format), then its path. Its buttons share one hover and reach the top:
    the window's top edge is a drag region the window hit-tests as its border.
  - Settings in tabs: General (graphics mode, theme as a dropdown with each palette), Hotkeys (every action's keys,
    changed by pressing the new one; remembered), Map, Game data.
  - Changed fields are marked beside the details' scroll bar. Ctrl C copies from the panel last used (a selected
    field in the details, else the object); the tree's selection looks strong only while the tree is in use.
  - The new-object dialog is titled after its base ("New Sorceress hsor") and checks the id as it is typed. The
    button-position card says only "Button Position". The editor opens with an alpha notice (backups; close the map
    in World Editor), its warning sign on the title's row; only Settings → General → Start turns it off (Sven,
    2026-10-28; until then the notice had its own "Don't show this again").
  - The editor shows the data World Editor shows. Its Object Editor (3.0.1, Asset Mode DE) was compared with the
    editor field by field on a copy of `we-heavy-archive`'s map (Peasant, Priest, Sorceress, Paladin, Town Hall,
    Shaman, Claws of Attack +12, Holy Light, Storm Bolt, Devotion Aura's buff, Iron Forged Swords, Ghoul Frenzy, the
    Ashenvale and Summer Tree Walls, Wooden Coffin, Cave City Cliff). What World Editor showed:
    - **The unit tree**: Human, Orc, Undead, Night Elf, then Neutral - Naga, then the rest by `hostilePal` in
      `unitui.slk` (1: Neutral Hostile, 299 units; else Neutral Passive, 197), whatever their race (the tavern
      heroes are Neutral Passive); 926 units, leaving out the two whose `hiddenInEditor` is 1 (the editor files them
      under Hidden in World Editor, as it does the hidden abilities). A unit whose `inEditor` is 0 is shown.
    - **Every other folder** lists by name, compared as written (Call To Arms (Town Hall) before Call to Arms
      (Peasant)): a buff folder the buffs of hero abilities first (`sort`), a destructable or doodad folder by
      `doodClass` first (tree walls, then canopy trees, then the rest; the sunken ruins' props after the other props).
      Buff folders are Buff and Effect; the kind is Destructibles, its folder Trees/Destructibles. A doodad shows no
      suffix (the data's "(Default)" is not shown). Standard Units come before Custom Units.
    - **A field that holds nothing** shows 0 (a whole number), 0.000 (a real), False, None (one value of a list, and a
      file, sound or order field: a model, an icon, a texture, a pathing or shadow texture, a sound, an order string),
      and nothing for text, flags, object lists and lists of models. The editor shows it as the box's dim placeholder,
      so nothing is written until a value is typed.
    - **Reals** show three decimals in every kind, abilities' levels and upgrade effects included (Holy Light 200.000,
      cooldown 5.500); no field showed two.
    - **Lists**: a choice value by its other spelling too (`invu` as Invulnerable, `enemy` as Enemy); tilesets by name
      (All for `*`; its screen gave P Cityscape Ruins, e Lordaeron Capital, R Lordaeron Capital Ruins, u Undercity);
      a destructable's and a doodad's Category by name (Trees/Destructibles, Props); a unit's Normal Skin and Hero
      Skin as abilities, by their names without suffixes (Harvest, Call to Arms, Repair); an upgrade by its first
      level's suffix (Glyph of Fortification (Upgrade 1)).
    - **Upgrade effects**: Effect N's Base, Mod and Code take the label and type the effect gives them in
      `units/upgradeeffectmetadata.slk` (Effect 1 - Attack Dice Bonus - Base and - Increment, whole numbers; Ghoul
      Frenzy's Attack Speed Bonus (%) - Base, a real), and only those the effect uses show (none for an effect of
      None). The editor keeps one that the map changes.
    - **Files by name**: an icon, model, texture, pathing map, shadow or ground texture field shows the names of every
      standard object whose field of that sort holds the same file, in the order of the kind's table (the Paladin's
      icon: Paladin, Admiral Proudmoore, Dagren the Orcslayer, ...), tagged by the field they come from
      (`Archmage <Special>`, `Paladin <Score Screen>`, `Priest <Caster>`, `Holy Light <Normal>` and `<Research>`,
      `Storm Bolt <Missile>`, a buff's `<Target>`, a destructable's or doodad's `<Base>`; a unit's model and projectile
      art untagged), cut with ", ..." once the next name would pass about 128
      characters, and its path when no object holds it (the Town Hall's portrait). Icons are named by units, items
      and abilities, not upgrades (Iron Forged Swords' icon shows the Steel Sword item); a unit's model is not named
      when its Extra Versions is set (the Priest's and the Sorceress's show their paths). The editor shows the path
      while the box is being edited, and on hover. The agent's readings: the tags of the fields not seen (an
      ability's Caster, Special, Effect, Area Effect art), leaving out the abilities Reforged added (version 2: the
      Steel Sword's icon is shared by seventy of them, which World Editor did not list), and the 128.
    - **Differences that stay**: the editor groups fields by category in World Editor's metadata order, where World
      Editor lists them in one alphabetical list ("Art - Model File"); Caster Upgrade Tips show as the data writes them
      (`"- Purge","- Purge|n- Lightning Shield"`, where World Editor drops the quotes); and World Editor names a few
      objects the export lacks (Holy Light (Item) with a Research icon), so the end of a long list of names can
      differ. An object whose strings the export files under another case (`[Boac]` for the Command Aura buff `BOac`)
      had no name until Moonwell.GameData 0.3.0-preview.2 (Sven, 2026-10-27: merged and published), which reads them
      whatever the case, as World Editor does.
  - The fields' editors (later the same day): a value's box has a background and border of its own; a whole number
    or a real is a number box with the browser's arrows (they send the value once they stop; the wheel scrolls the
    panel, never steps a box); an extended tooltip is a box of several lines. A dropdown is the editor's own, as wide
    as its widest choice; a flags field is a box as wide as its widest single choice that says None when nothing is ticked.
    The UI scale has 80, 100 and 120% for now.
  - The game's interface icons (the Human interface's, from the install) for gold, lumber, food and mana on the
    tooltip's cost line. They were on the fields and in the dropdowns too, until Sven took them out of the fields on
    2026-10-28.
  - Dice totals: a unit's Attack 1 and Attack 2 damage and its gold and lumber bounties show as the range the game
    rolls (Attack 1 - Damage 12 — 13: base + dice to base + dice × sides), with the base, number of dice and sides per
    die under it. The lowest and highest typed in set the three as one edit, keeping the number of dice when it splits
    the range evenly, else the most that do; a range of one number is dice of 1 side. *(Agent's call: World Editor has
    no such row; the rule for the dice is the agent's.)* From 2026-10-28 to 2026-10-29 the lowest and highest were two
    boxes with an em dash between them; since 2026-10-29 (Sven) the range is one box again (`10-15`, `10 - 15`,
    `10 to 15` or one number). Since 2026-10-28 (Sven) the arrow that shows the three sits in the gutter left of the names, so the
    total's name lines up with the other fields'. *(Agent's calls: the arrow takes the changed colour in place of the
    row's changed bar.)*
  - Ground Texture is a dropdown of the install's splats (`Splats\UberSplatData.slk`) by World Editor's names for
    them (`WESTRING_UBERSPLAT_<code>`), by name, after None. Shadow Texture (Building) and a destructable's shadow are
    a dropdown of the shadows the standard buildings and destructables use, under Buildings and Destructibles, each
    named by the objects that use it. *(Agent's calls, not seen in World Editor: the order, and naming a shadow by its
    objects.)*
  - Ctrl and the wheel move the model up and down; the side panel's model has a list of its animations. Since
    2026-10-28 (Sven) a button beside "turn again" puts a moved model back at its height, and the list is always there,
    empty while the model loads or when it has no animations.
  - The tooltip editor (an object's context menu, or Edit by the tooltip preview): the tooltip as the game shows it,
    its texts edited in place (coloured text in its colour, a placeholder such as `<AHhb,Dur1>` as a chip named for
    the value with the value it has), each naming its field when hovered. A toolbar colours the selection (a colour
    picker, six of the game's colours, no colour) and puts in an ability's values at the level shown (a percentage
    as `,%`). The icon, hotkey and costs show but are edited in their fields. Save sends the changed texts as one
    edit, a colour as `|cAARRGGBB ... |r` and a line break as the text already breaks its lines (`|n` when it has
    none). An ability's turned-off tooltip shows only when it has one.
- **Sven, 2026-10-28:** the editor reads the game data from the Warcraft III install, with no export. The install
  reader is in Moonwell's package (Sven's choice): `Moonwell.GameData` 0.3.0-preview.3 has the CASC reader,
  `GameTables.OpenInstall` and `GameInstall`, and the editor shares one open install between the game data, the icons
  and the models. The folder is the one chosen in Settings → Game data, else `MOONWELL_WAR3`, else the usual folder;
  without one the first screen asks for it. Settings → Game data shows the folder and the game version, with Change
  folder…. The cache is kept per build of the game (its build key), so an update builds it again. Only the install's
  folder is taken, never a `war3.w3mod` export, which may be incomplete (Sven, later the same day). *(Agent's calls:
  the export chosen before is not carried over, so the install is found at the next start; the first screen asks
  rather than opening the dialog by itself.)*
- **Sven, 2026-10-28, later:**
  - The header: the seven kinds, a divider, then the views (Details, Table, Compare) as icons; at the far right a
    menu (☰) with Open map folder…, Open map archive…, Reload the map from disk, Save and Settings. *(Agent's calls:
    the icons, Compare kept as a greyed icon, Settings in the menu since the menu took the gear's place.)*
  - The status bar shows a turning mark and what is loading in its middle: the game data while it loads, Saving…,
    and Loading… while the app takes more than 150 ms over a command and the window has the focus (so not while its
    Open map dialog waits). *(Agent's calls: the 150 ms and the focus rule.)*
  - The side panel kept shaking on the Footman: at one height its scroll bar came and went, which changed its width,
    the model's size with it, and so its height. The panel keeps the scroll bar's room, and a new size no longer loads
    the model again.
  - A total of dice starts with its three fields hidden. It is selected on its own (not its base): Ctrl C copies its
    range, and Ctrl V or its context menu pastes a copied range into any total (Attack 1's 12 — 13 onto Attack 2).
  - The text and number boxes show only their value, as the tooltip editor's texts do: no border or background, a
    dashed border on hover and the accent's while typing; an empty one shows its placeholder (World Editor's empty
    value, such as 0 or None, else "(empty)"). Dropdowns, flags and object lists keep their boxes.
  - The model kept drawing nothing after the Footman's Decay Flesh: war3-model draws the classic material of an HD
    model (the Footman's material 7, seen only in Decay Flesh) with its HD shader, which then lacks a normal and an ORM
    texture, and throws. Such a material is drawn with flat ones, and a frame that throws no longer stops the drawing.
  - Custom objects sit under Custom in the same folders as the standard ones (Human › Melee › Units...), by their own
    values where the map sets them: a unit's race, campaign, special, building and Display as Neutral Hostile, a hero
    by its id's capital letter; an item's class; a destructable's or doodad's category; an ability's race and Hero or
    Item Ability; a buff's race and Is an Effect; an upgrade's race. Within a folder they go in World Editor's order.
    *(Agent's calls: which fields count, not compared with World Editor field by field.)*
- **Sven, 2026-10-28, the dependency pass:** libraries take over code the editor kept itself, where one does the job
  well; the rest stays the editor's own (the tree's and table's drawing of only the rows in view, the hotkeys, the
  spreadsheet paste, the search).
  - Bits UI: the dropdowns, the menus (the right-click one and ☰), the popovers and the dialogs, which keep their looks.
    They gain typed letters in menus, lists that open above near the window's bottom, a popover that follows its
    button as the page scrolls (it closed before) and hides while the button is out of view, Esc closing one layer at
    a time, and the focus going back to what opened a dialog. *(Agent's calls: the four components keep their names
    and props, so the rest of the page did not change; the scroll behaviour; a menu no longer closes when the window
    loses the focus.)*
  - Pfim: the DDS icons, in place of the editor's own DXT decoder. Its colours differ from the old decoder's by at
    most 2 in 255 (rounding), the alpha not at all, over the install's 1,280 DDS icons.
  - TipTap: the tooltip editor's texts, in place of the editor's own editable-text code; `richText.ts` still reads
    and writes the markup. The texts gain their own undo and redo. *(Agent's calls: copying a text gives the game's
    markup and a paste is read as markup, so colours and placeholders survive both; a paste was plain text before.)*
- **Sven, 2026-10-29:** a number box's up and down arrows are the editor's own (two chevrons in the faint grey, shown
  while the box is hovered or has the cursor), in place of the browser's grey block; a click steps by 1 and the value
  is sent once the clicks stop, as before. *(Agent's call: drawn by the field editor, since the browser's arrows,
  restyled, no longer stepped down from their lower half.)*
- **Sven, 2026-10-29, second batch:**
  - Popups (dialogs, lists, menus) have a small shadow, not the large dark halo.
  - A dialog stays open until the page closes it: the settings had been closed by Bits UI on its own (Ctrl Esc, which
    opens Windows' Start menu, while a hotkey was being pressed) and then could not be opened again. Leaving the window
    also stops the hotkey listening.
  - No cut: Ctrl X and the context menu's Cut are gone for objects (a paste never deletes the object copied) and for
    fields. Text in a box is still cut as anywhere.
  - True or False is the word alone, no checkbox and (later the same day) no box either; a click turns it, and it
    takes as much room for either word.
  - Every box of a field (number, text, True or False, dropdown, flags) shows only its value: a dashed border on hover,
    the accent's with the cursor; a dropdown keeps its arrow. The selected field's boxes show a solid border.
    *(Agent's call: the solid border is the faint grey one, the accent stays for the box with the cursor.)*
  - ↺ on a changed field sits at the row's right end.
  - The table view is an experimental feature, off until Settings, General, Other turns it on. The tree's icon frame
    is always on (its switch is gone). The switches (ask before a reload, the alpha notice, experimental features) are
    together under Other at the bottom of General.
  - A unit is a hero by its own id: a copy of the Peasant with an upper-case `H` id gets the hero fields (Abilities -
    Hero, the attributes) and Hero in its chain, as World Editor does (game facts, 10-28); a copy of a hero with a
    lower-case id loses them. *(Agent's call: the building flag still comes from the base.)*
  - Tooltip editor: the text's field name shows above its box, where it covers nothing. The toolbar has the colours
    the game's own tooltips use (gold for levels and hotkeys, autocast's pale blue, the item grey, green, blue, purple
    and orange, quest yellow, consumable sky blue: counted in the game's ability and item texts), the eraser for no
    colour, a divider, then the user's own colours and a dashed +. The + opens the colour picker (the browser's own,
    which shows the code); a colour picked colours the selection and is kept as a swatch, and a new + follows until
    there are 8. A click on one of them colours the selection and opens the picker to change it. *(Agent's calls: 8
    colours, kept in the window's storage.)* The picker was WebView2's own at first, which could not pick a colour;
    since later the same day it is the editor's own: a square of saturation and brightness over a hue bar, the code
    to read or type, and Add (or Change). *(Agent's calls: its look, and the arrows step it.)*
- **Sven, 2026-10-29, third batch:**
  - A field's dropdown (and flags) is as wide as the choice shown, or its placeholder, not its widest choice
    (reverses the 2026-10-27 decision; the model preview's lists keep the widest).
  - An object list's chips show their objects' ids only while the field ids are shown (Ctrl D).
  - An extended tooltip's box cannot be resized by dragging its corner: it grows with its text.
  - Tooltips take World Editor's lengths: 2048 characters for a tooltip, 8191 for an extended one (any tooltip whose
    name has Extended), measured in World Editor's box (game facts, 10-29). The details panel's boxes stop there, and
    the tooltip editor's texts take no more markup (a text already longer can still be shortened) and show their
    length beside the field name.
  - The page's strings file holds every text the page shows; True, False and None, the 24 player colours and
    Night Elf were the last left in the code.
  - The strings file holds the app's own texts too (Sven's yes, 2026-10-29), under keys that start with `app.`: its
    error messages, World Editor's limit warnings, the tree's folders, the map options' names, the tags after a file's
    object names, the file dialogs' prompts and the window's questions. The file is built into the app
    (`MoonwellEditor.Files.Texts` reads it), so one edit and a rebuild change both. A test checks that every key the
    code asks for is in the file and every `app.` key is asked for. *(Agent's calls: the request checks between the
    page and the app (a request that is not JSON, an argument of the wrong type) and errors only a bug in the code
    could raise stay in the code, in English: they are for whoever fixes the code, not the user.)*
  - A bits field (Extra Versions, Pick Flags...) reads its values as the game's data spells them, each bit's own value
    (1, 2, 4): before, the editor read 6 as bits 1 and 2, so it ticked the wrong entries and wrote the wrong bits. A bit
    World Editor does not list but the standard data uses (Extra Versions' 4, which 20 standard units hold, the Priest's
    6) is not offered or shown, as World Editor shows none, and stays in the value when the others are ticked (Sven's
    choice). What the game does with that bit is not known.
- **Sven, 2026-10-29, fourth batch:**
  - A double click anywhere on a True or False field (or level cell) turns it.
  - A number box is a text box that works out arithmetic when it sends its value: `10*2+5`, `(3+4)/2`, and `*2` or
    `/2` on the value it holds; `+` and `-` in front are a sign. A whole-number field takes the result rounded. The
    arrow keys step it, as its arrows do. *(Agent's calls: rounding, and `*` or `/` in front working on the value.)*
  - The side panel: the tooltip's Edit is as tall as its heading, the counts of Uses and Used by sit at the right
    end, Uses and Used by start closed, and the team colour is a square beside the animations that opens the 24
    colours.
  - Only what is typed in can be selected: names, values, the panels, the tree and the status bar cannot.
  - Esc lets go of the button clicked last (a kind, a view), so its focus ring does not show; a focus ring is the
    accent's colour, not the browser's black.
- **Sven, 2026-10-29, fifth batch:**
  - The team colour square is exactly as tall as the animations' list beside it, and as wide.
  - Settings, General: a default team colour (the 24 colours), which the side panel's model starts in (and follows
    when it changes) and the model picker shows models in.
  - Rename in an object's context menu (and F2 while the tree is where the user is) renames it in its tree row: Enter
    or leaving the box renames, Esc gives up.
  - Copy name in the context menus of an object and of a field puts its name on the clipboard.
  - F10 opens the settings (a hotkey the settings can change).
  - The tooltip editor is titled "Tooltip Editor: name". The text hovered or edited is named at the bottom, over the
    count of changed texts, so the colours sit right above the tooltip. A right click on one of the user's own colours
    opens a menu that removes it.
  - A `/` that starts a word in a tooltip text lists the values the text can show, as slash commands do; the words
    after it narrow the list, the arrows move in it, Enter or Tab puts the placeholder in, Esc closes it. *(Agent's
    calls: what it lists, an ability's own values at the level shown and the values of the abilities a unit's or an
    item's ability lists name; a number typed at the end picks the level (`/dur2`) and a `%` the value ×100.)*
  - After two probes of what the game fills in (game facts, 2026-10-29; Sven asked for the list to follow them):
    - A unit's own values (`realHP`, `regenHP`, `mindmg1`, `maxdmg1`, its first attack's damage) and an item's charges
      (`uses`) are listed and put in too, and the preview fills them.
    - The game rounds a plain placeholder's value down to a whole number, `,.` shows one decimal and `,%` the value
      ×100; the preview and the chips show values so. A value that is not whole is listed with `,.` and with `,%`, and
      a typed `.` or `%` at the end picks one. The insert menu's "Shown as" picks As it is (whole numbers plain, others
      with `,.`), One decimal or ×100. *(Agent's calls: those three choices, and `,.` as what a decimal value gets
      unless the user picks; how `,.` and `,%` round past the values probed is not measured, and the preview rounds
      them down.)*
    - A map's own upgrade's `base` and `mod` show 0 in the game, so upgrades list nothing.
    - A buff's tooltip lists nothing and has no insert menu, and a comma in its extended tooltip shows a warning: the
      game shows it only up to the first comma, so placeholders cannot work in it. *(Agent's call: a warning, not a
      refusal, since World Editor takes the comma; Moonwell's build fails on one in a declared buff.)*
  - The settings open with nothing in them focused (F10 had put the focus on the first checkbox).
  - The model picker's preview stays on the model last under the pointer; each opening starts with the field's.
- **Keys** (first set): Ctrl K search, Ctrl S save, Ctrl Z / Ctrl Y undo and redo, Ctrl D field ids (as in World
  Editor), Ctrl F filter fields, Ctrl Shift N new object from the selected one, F2 rename, Delete delete, Alt ←/→ back
  and forward through visited objects.

## Stack

*(Agent's calls, following Moonwell's and steam2fiddy's habits, within Sven's choice of C# + Svelte + WebView2.)*

- C#: .NET 10, nullable on, warnings as errors, implicit usings, `.editorconfig` as Moonwell's (4 spaces, LF).
  `Moonwell.War3Net.Build.Core` from nuget.org; `Microsoft.Web.WebView2` for the window. xUnit for tests, as Moonwell.
  `War3Net.Drawing.Blp` and Pfim (Sven, 2026-10-28) read the icons.
- Front end: Svelte 5, TypeScript, Vite, Tailwind CSS 4, Lucide, fontsource. Exact versions pinned. Biome (2 spaces,
  100 columns). Vitest, with the bridge mocked. Bits UI for the popups and TipTap for the tooltip texts (Sven,
  2026-10-28); war3-model and dds-parser for the model preview.
- npm (no pnpm), LF line ends.
- Release: a self-contained Windows build (`dotnet publish`), about 60-100 MB; WebView2 is part of Windows 10 and 11.

## How it is tested

- **Round trip, every file of every real map:** the guard's check (War3Net read, written back, same bytes) holds for
  every object file of every fixture; the `WtsFile` reader and writer return the same bytes for every `.wts`.
- **The guard itself:** a file with a trailing table and one with a non-UTF-8 name round-trip exactly (War3Net
  `6.1.2-moonwell.4`); a truncated file, and a file read through a write-back that loses bytes (standing in for a
  lossy War3Net), each open read only with the right note, never a crash.
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
   ships behind a warning ("saving is tested only on small maps"). He saved it the same day (`tests/fixtures/we-heavy`);
   the app's save of it passed his World Editor check, so the warning is gone, and he left out the check in the game.
5. C# on War3Net, with the Svelte interface in a WebView2 window (replacing Tauri and Rust).

## Open questions (Sven's, not blocking the first plan)

1. Licence and repository for the public release (a GitHub repository, pushing and publishing are each his call).
2. ~~Publishing `Moonwell.GameData` to nuget.org.~~ Done: 0.3.0-preview.1, by Moonwell's v0.3.0-preview.1 release
   (2026-10-27); the editor uses 0.3.0-preview.2 (text sections whatever their case).
3. What annoys him most in the Object Editor, if anything beyond the goals above.

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
- ~~"Unreal-style types plus components" read as the class chain plus abilities as components.~~ Corrected by Sven,
  2026-10-27: Uses and Used by instead.
- A Moonwell map is edited in its `map/` source folder, never its build output.
- Stack details: xUnit, Svelte 5, Tailwind 4, Biome, Vitest; New York and SF Pro where installed, Inter, JetBrains
  Mono; Lucide.
