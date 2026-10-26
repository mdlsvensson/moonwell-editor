# Object Editor: design

A desktop app, made with Tauri, that does the job of World Editor's Object Editor (units, items, abilities, buffs,
upgrades, destructables and doodads) and is faster, searchable and easier to use.

Status: **draft, not approved.** Sven skipped the design questions on 2026-10-26 ("skip"), so every choice below is
the agent's call unless it says otherwise. Nothing here is Sven's decision until he confirms it. The calls are listed
together at the end ("Calls to check") so they can be read and corrected in one place.

The clickable mockup is `docs/mockups/editor.html` (open it in a browser).

## Why

World Editor's Object Editor is slow to move around in, has weak search, shows one object at a time, hides field ids
until you toggle them, makes per-level ability fields a long list, and cannot change one field on many objects at once
or compare two objects. Sven makes maps and built Moonwell, which already reads the game's data tables and knows every
field's type and limits; that knowledge can drive a better editor. *(The list of annoyances is the agent's guess at
the usual complaints; Sven has not named his own.)*

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
6. **See how objects connect.** For the selected object: its abilities (and an item's, a unit's upgrades) as
   "components" you can click into, what uses it ("used by": which units have this ability, which building trains this
   unit), and a tooltip preview with colour codes and `<A000,DataA1>` references filled in.
7. **Keep maps working.** Read and write the map's own object files exactly, so World Editor and the game open the
   map as before; an unchanged file is written back byte for byte.
8. **Keyboard first.** Every action has a key; the mouse is never required.

## Non-goals (first version)

- Terrain, triggers, regions, imports, map options: World Editor still does those.
- Icons and models: the game's art lives in its CASC storage as `.blp`/`.mdx`; reading it is a later stage.
- Reading or writing Moonwell's C# object classes (a later stage: "copy as Moonwell C#" is the first step).
- Locales other than English.
- Campaign files (`.w3n`).
- Classic Warcraft III (1.26 and older) maps: the target is 3.0 (Reforged) maps; older object file versions are read
  if the format allows it, but not tested.

## Anti-goals (would make it a failure even if it works)

- A map that World Editor or the game no longer opens, or opens with a value silently changed.
- Being slower than World Editor to open a map or to jump to an object.
- Pixel fonts, or any font that is hard to read.
- Changing a field the user did not touch (for example rewriting strings into `war3map.wts`, reordering objects, or
  dropping a field the app does not know).

## Who it is for

Sven first. Nothing in it needs Moonwell to run: a modder with a map and the app can use it. *(Agent's call; Sven has
not said whether other modders are a target. Making it work without Moonwell costs nothing extra, because the game's
standard data ships with the app.)*

## What it edits

**A map's own object files** (option (a) of the brief), straight in a map: a map folder (World Editor 3.0 can save a
map as a folder) or a `.w3x`/`.w3m` archive. *(Agent's call, and the one most worth Sven's check: see "Open
questions".)*

The files are the seven kinds' main and skin files and the strings file:

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

`war3map.w3o` (all kinds in one file, as World Editor exports and imports them) is read and written as a separate
"Import / Export all objects" action, not as the map's storage.

World Editor 3.0 writes every object into both the main and the skin file, with the skin side holding the fields whose
metadata row has `netsafe` = 1 (wc3-cs `docs/plans/2026-10-18-objects.md`, from the fixture's README). The app keeps
that split by Moonwell's `Field.Skin` flag, which Moonwell's build already writes by: an edit to a skin field goes to
the skin file, any other to the main file. The fixture has one case that does not fit a plain `netsafe` reading (the
doodad's `dnam` is in the main file), so the codec task checks the rule against every record of the fixture, and of
Sven's World Editor save when it arrives, before the writer relies on it.

**Moonwell maps:** the app edits the map's source folder (`map/` in a Moonwell project). The built map under `build/`
is rewritten by every build, so editing it is pointless; the app says so when the opened folder is a Moonwell build
output (a `build/` folder beside a `.csproj`).

### The file formats

From War3Net (`src/War3Net.Build.Core/Serialization/Binary/Object/` in Sven's fork, wc3-cs `war3net`), checked against
HiveWE and a hex dump of World Editor 3.0's own output (wc3-cs `moonwell/tests/Moonwell.Build.Tests/Fixtures/WorldEditorObjects`).
Little-endian throughout; an id is 4 bytes in file order.

```
int32 version                         1, 2 or 3 (3 since 1.33; 1 and 2 are the same layout)
int32 count; Object[count]            changes to standard objects (new id 0000)
int32 count; Object[count]            custom objects (old id = base, new id = the object's)
Object:
  char[4] oldId; char[4] newId
  version 3: int32 setCount; then per set: int32 setFlags     World Editor writes 1, 0
  int32 count; Mod[count]
Mod:
  char[4] fieldId; int32 type         0 int, 1 real, 2 unreal, 3 string
  .w3a .w3q .w3d only: int32 level (variation for doodads); int32 dataPointer
  value: int32 | float32 | float32 | NUL-terminated bytes
  int32 end                           kept as read; 0 for a new record (and the object's new id in some writers)
```

- Whether a record has a level and data pointer depends on the file (`.w3a`, `.w3q`, `.w3d`), not on the field.
- Strings are kept as **raw bytes**, not decoded and re-encoded, so a map with non-UTF-8 text still round-trips.
  They are shown as UTF-8 (with replacement marks where invalid) and written back unchanged unless edited.
- Anything after the two tables (some writers add an empty third table) is kept and written back.
- A version 3 object with a set count other than 1 is kept as raw bytes and shown read-only, with a note: no tool
  documents what more than one set means.
- `.w3o`: `int32 version`, then for unit, item, destructable, doodad, ability, buff, upgrade in that order an `int32`
  present flag (0 or 1) and, when 1, a whole per-kind file. No skin files.
- `war3map.wts`: `STRING n`, optional `// comment` lines, `{`, the text, `}`. World Editor writes a UTF-8 BOM and CRLF
  line ends; both are kept, as are blank lines, comments and the number's zero padding. (War3Net loses the BOM and
  the line ends, so the app writes `.wts` itself.)

### Map archives

A `.w3x`/`.w3m` is an MPQ archive behind a 512-byte `HM3W` header. Sven pointed at the `wc3` crate
(`jonathanvdc/wc3`, MIT/Apache, MPQ v1-v4): its `mpq::ArchiveWriter` edits a copy of an existing archive and copies
unchanged entries without recompressing them, which is what a save needs, and its `blp` module (feature
`blp-decode`) will draw icons in a later stage. *(Taking it as the archive library is the agent's reading of his
link.)* The `HM3W` header is copied from the opened map unchanged. A save rewrites only the object and string files
in the archive and keeps every other file's bytes; the new archive is written to a temporary file first (the crate
warns that a failed write leaves partial output). Protected maps (damaged tables, as map protectors make them) are
opened read-only, with a note, if `wc3` cannot read them; `war3-mpq` (a reader hardened for them) is the fallback to
try. The crate is young (3 stars, no stated support for the `HM3W` header or protected maps), so the first archive
task is a spike: open Blizzard's `(4)losttemple.w3m` (in the export's `maps/`), Moonwell's `Sample.w3x`, and a World
Editor save; replace one file; open the result in World Editor and the game. If `wc3` fails that, the fallbacks are
`wow-mpq` (MIT/Apache, mature, writes) and then War3Net's MPQ through a .NET helper.

Folder maps need no archive code, so they come first.

### Real maps to test with

- World Editor 3.0's own output: the `WorldEditorObjects` fixture (every kind, one new object each, main and skin
  files, `.wts` with BOM and CRLF). Small.
- Moonwell's output: `MyMap/build/MyMap/` (every kind, written by War3Net) and `Sample.w3x` (an archive).
- Blizzard's `(4)losttemple.w3m` for the archive.
- **Missing:** a World Editor save with many changes across all kinds (levels, data pointers, doodad variations,
  `TRIGSTR` strings, changes to standard objects). Sven to provide one; until then the writer is not trusted on maps
  with heavy edits.

Later stages, in the agent's suggested order: (2) "copy as Moonwell C#" for a custom object; (3) icons from the game
install; (4) reading the base data from the game install instead of the shipped export; (5) reading and writing
Moonwell's C# classes.

## How the app is put together

Tauri makes a desktop app from two halves that talk to each other:

- **The Rust side** (the "backend", in `src-tauri/`): a normal program that owns the data. It reads and writes the
  files, keeps the loaded map and the game's standard data in memory, and answers questions. Think of it as one big
  Godot autoload that holds the state.
- **The web side** (the "front end", in `src/`): the window's contents, written in TypeScript with Svelte, drawn by
  Windows' built-in browser engine (WebView2). It shows things and sends what the user did to the Rust side.

They talk two ways:

- **Commands**: the web side calls a Rust function by name and awaits its answer, like an `await` on a GDScript
  coroutine. For example the web side runs `await invoke("get_object", { kind: "unit", id: "H000" })` and the Rust
  function marked `#[tauri::command] fn get_object(kind: Kind, id: String) -> ObjectView` runs and returns the object
  as JSON.
- **Events**: the Rust side tells the web side that something happened, like emitting a Godot signal that the UI is
  connected to. For example after a save Rust emits `map-saved`, and the status bar, which called
  `listen("map-saved", ...)`, updates.

The Rust side does the heavy work (parsing, search, bulk edits, comparing) so the web side only ever holds what is on
screen: lists are virtualised (only the visible rows exist), and a search returns the top results, not everything.

### Crates (Rust packages) and folders

```
wc3-object-editor/
  crates/
    objdata/        reads and writes the object files (.w3u ... .w3q, skin files, .w3o) and war3map.wts
    mapfile/        opens a map: a folder or an MPQ archive (.w3x/.w3m); lists, reads and replaces files
    basedata/       loads the game's standard data bundle (fields, choices, standard objects)
    model/          the edit model: map + base data -> resolved values, edits, undo, search, compare, references
  src-tauri/        the Tauri app: commands and events over `model`
  src/              the web side (Svelte 5, TypeScript)
  tools/BaseData/   .NET 10 console app that writes the standard data bundle with Moonwell's generator
  tools/Oracle/     .NET 10 console app that dumps object files to JSON with War3Net, for tests
  base-data/        the bundle the app ships (one per game version)
  docs/
```

The file crates (`objdata`, `mapfile`) know nothing about Tauri or the UI, so their tests are plain `cargo test`.

## The game's standard data

The app needs every standard object's values, every field's label, category, type, limits and choices. Moonwell's
generator already works all of this out from the game's export (`war3.w3mod`): `ExportTables` merges the tables,
func, skin and string files per object; `Fields.Of` gives each kind's fields with their type, store type
(`int`/`real`/`unreal`/`string`), per-level flag, data pointer, the abilities a field belongs to and World Editor's
limits; `StandardObjects.Of` gives each standard object's values keyed `fieldId` or `fieldId:level`, its race,
campaign flag, hero flag and the balance sets it is in. All three are public in `Moonwell.Generator`.

**The agent's call:** a small .NET tool (`tools/BaseData`) references `Moonwell.Generator` and writes one bundle,
`base-data/<game version>.json`, which the app ships and loads at start. Moonwell already writes much of this as
`src/Moonwell.Objects/Generated/Values/<kind>.json` (fields with type, store, per-level, data pointer and skin flag;
each object's values): 5.7 MB for all kinds, so plain JSON is small enough, and the bundle adds labels, categories,
limits and choices on top. The alternatives weighed:

| Way | Cost | Risk |
|---|---|---|
| **Bundle from Moonwell's generator (chosen)** | Small: ~200 lines of C#, run once per game patch. The app needs no .NET. | Depends on `Moonwell.Generator`'s public API; a Moonwell change can break the tool (caught when it is run). |
| Port `ExportTables`, `Fields` and `StandardObjects` to Rust | Large: ~1,500 lines of subtle rules (per-level columns, `appendIndex`, quoted lists, `:hd` keys, use flags) to rewrite and re-test. | Two copies of the rules that drift apart. |
| Read Moonwell's generated C# (`src/Moonwell.Objects/Generated`) | Parsing C#. | Fragile; loses labels and categories. |

Each field in the bundle carries the metadata the editor needs that `Field` does not: `category` and `sort` (the
order World Editor shows fields in), and the label resolved from `WESTRING_` keys. Each choice type carries its
entries and labels (`ui/uniteditordata.txt`). The bundle records the game version it came from, shown in the status
bar.

Base values for a map's game data set: a map may use a balance set (`melee_v0`, `custom_v0`, `custom_v1`), which
changes standard values. The bundle carries each object's per-set differences (`StandardObject.Sets`). First version:
the base tables, with a status-bar switch to view another set. Reading the map's own setting from `war3map.w3i` is a
later task. *(Agent's call.)*

## The map's data and how a value is shown

The model keeps the object files as parsed (every record, in file order, unknown fields included), and resolves a
value on demand:

- A **standard object** (`hfoo`): the map's change to it if any, else the standard value.
- A **custom object** (`h001`, based on `hfoo`): its own change if any, else the **standard** base's value. Not the
  map's change to the base: the game takes unset fields from the game's standard object (wc3-cs `docs/game-facts.md`,
  probe `moonwell-0.2.3-inherit`: a new unit based on a changed `hfoo` has the standard 420 life).
- **Strings** that read `TRIGSTR_012` are shown from `war3map.wts`; editing one changes the `.wts` entry and keeps
  the reference. A string that was inline stays inline; a new string is written inline (Moonwell writes strings
  inline and the game reads them). *(Agent's call.)*

The tree, as in World Editor: Custom (this map's new objects) first, then Standard by race, then Melee / Campaign,
then Units / Buildings / Heroes / Special for units. The object's header shows its chain (Unit › Hero › Paladin ›
Champion). *(The "Unreal-style object types plus components" Sven likes is read here as: a class chain shown on every
object, and the abilities a unit has shown as components you click into. Agent's reading; Sven to confirm.)*

## Editing

- Every edit is a small operation the model can undo: set a field (kind, object, field, level, new value, old value),
  reset a field, create, copy or delete an object, change an object's id. Undo and redo walk the list.
- Setting a field to its base value removes the modification instead of writing a same-value record (as World
  Editor does), but only when the base is the same in every graphics mode. *(Agent's call.)* The game keeps some
  standard values per graphics mode (`modelScale:sd`, `:hd`, `:de`; in mode 2 it reads `:de`, else `:hd`, never
  `:sd`, per wc3-cs `docs/game-facts.md`), and `StandardObjects` resolves those to one value. The bundle therefore
  keeps the per-mode values of a field where they differ, the details panel shows the mode values on hover, and a
  record whose value matches only some modes is kept.
- New objects take the next free id the way World Editor does (`h000`, `H000`, `A000`, ... keeping the base's first
  letter's case for units, heroes upper case) or an id the user types; ids are checked for clashes per kind.
- Saving writes only the files whose content changed, after copying the previous versions to a backup folder next to
  the map (`<map>.backup/<date-time>/`), keeping the last 10. A `.w3x` is written to a temporary file and moved over
  the original only when the write succeeded. *(Agent's call.)*
- If the map changed on disk since it was opened (World Editor saved it), the app says so before saving and offers to
  reload.

## Validation

Each field's rules from the bundle (`Min`, `Max`, `MaxLength`, `MaxEntries`, `Required`, `NoNegative`) are checked
as the user types and on save. A value past a limit is allowed with a red mark and a line of text ("Above World
Editor's limit of 99999"), matching Moonwell's `MW0009` wording where it can. Values the game cannot read at all
(text in a number field) are refused.

## Look and feel

- **Theme:** Vitesse Dark (the tokens wc3-dev used, `#121212` background, `#dbd7ca` text, `#4d9375` green accent,
  `#e6cc77` yellow for changed values, `#cb7676` red for warnings).
- **Fonts:** readable only, never pixel fonts. Interface text in Google Sans (with Nunito for headings), as in
  steam2fiddy and wc3-dev; ids, values and code in JetBrains Mono. Both bundled with the app through fontsource, so
  no network is needed. *(Agent's call; "JetBrains Mono (Nerd Font)" is read as JetBrains Mono, since the app draws
  its icons with Lucide and needs no Nerd Font glyphs.)*
- **Layout** (see the mockup): a top bar with the map name, the search box and the views (Details, Table, Compare);
  a left tree with the seven kinds and a tree filter; the details panel in the middle; a right side panel with
  components, "used by" and the tooltip preview; a status bar with unsaved changes and the game data version.
- **Keys** (first set): Ctrl K search, Ctrl S save, Ctrl Z / Ctrl Y undo and redo, Ctrl D show field ids (as in World
  Editor), Ctrl F filter fields, Ctrl Shift N new object from the selected one, F2 rename, Delete delete, Alt ←/→
  back and forward through visited objects.

## Stack

*(Agent's calls, following steam2fiddy's habits.)*

- Tauri 2, Rust stable (1.99 installed), edition 2024.
- Front end: Svelte 5, TypeScript, Vite, Tailwind CSS 4, Lucide icons, fontsource fonts. Exact versions pinned.
- Biome for formatting and linting the front end (2-space indent, 100 columns), `rustfmt` and `clippy` for Rust.
- Tests: `cargo test` for the crates; Vitest for front-end logic; a Playwright smoke test against the Vite page with
  Tauri's IPC mocked (`@tauri-apps/api/mocks`).
- npm (no pnpm), LF line ends (`.gitattributes`: `* text=auto eol=lf`).
- Windows first; nothing Windows-only in the crates.

## How it is tested

- **Round trip, every file of every real map:** read each object file and `.wts` of each test map (the maps under
  wc3-cs `moonwell/tests`, and maps saved from World Editor when Sven provides them) and write it back; the bytes must
  be identical. Same for an MPQ archive's file list and contents (the archive bytes may differ; the files in it may
  not).
- **Against War3Net:** `tools/Oracle` reads the same files with War3Net and dumps them to JSON; the Rust reader's
  dump must match. This catches a reader that round-trips by keeping raw bytes but misreads values.
- **Edits:** an edit, saved, then read by War3Net, shows the new value and nothing else changed.
- **Base data:** known values (`hfoo` 420 life, `Amls` cooldown 30, Paladin hero fields only on heroes) and counts
  per kind.
- **In the game and World Editor:** a map edited and saved by the app opens in World Editor with the changes, and
  plays with them (wc3-cs `test-map` skill), before the first release and whenever the writer changes.
- **The app itself:** run the Tauri dev window and check it with screenshots.

## What is left to the builder

Names inside the code, the exact JSON shape of the bundle and of the command results, the search ranking, how the
tree is cached, the backup folder's exact name, and any detail the plan does not fix, as long as the goals and
anti-goals hold.

## Open questions (Sven's)

1. **What the app edits.** The agent chose (a), the map's object files. Moonwell's decision 3 says a Moonwell modder
   never opens the Object Editor, so for Moonwell maps the app is mostly a fast browser of the standard data; the
   editing serves World Editor maps (and the objects a Moonwell map keeps in its `map/` folder). If Sven's real use is
   Moonwell, (d) a viewer and search with "copy as Moonwell C#" may be the better first version, and much smaller.
2. **What annoys him most** and **what he does most** in the Object Editor: this decides which of goals 4-6 come
   first.
3. **Other modders:** if yes, the base data bundle holds Blizzard's data, which matters for a public repository and
   release (Moonwell's public repository already commits the same kind of data in `Generated/Values/*.json`). Also,
   `tools/BaseData` builds only beside a wc3-cs checkout, because `Moonwell.Generator` is not a package; fine for Sven
   alone, not for outside contributors.
4. **The World Editor save with heavy edits:** until it exists, the plan has saving ship behind a warning ("saving is
   tested only on small maps") and the save milestone does not close.

## Calls to check

Every call below is the agent's, made because the questions were skipped:

- Edit the map's own object files, folder or `.w3x`; no project format of its own.
- Works without Moonwell; the standard data ships as a bundle made by Moonwell's generator.
- Read and write the formats exactly; unchanged files byte for byte.
- Custom objects inherit from the standard base, not the map's change to it (as the game does).
- Strings: `TRIGSTR` entries edited in `war3map.wts`, inline strings stay inline, new strings inline.
- A field set back to its base value is removed, not written.
- Backups of the last 10 saves next to the map.
- First version shows the base tables; the balance set is a switch.
- "Unreal-style types plus components" read as the class chain plus abilities as components.
- Stack: Tauri 2, Svelte 5, TypeScript, Tailwind 4, Biome, Vitest; Google Sans, Nunito, JetBrains Mono; Lucide.
- Parsing in Rust, with War3Net only as a test oracle.
- Archives through the `wc3` crate Sven linked (fallback `wow-mpq`); folder maps first.
- A Moonwell map is edited in its `map/` source folder, never its build output.
- A field set back to its base is removed only when the base is the same in every graphics mode.
- Strings kept as raw bytes; `.wts` BOM, line ends and layout kept.
