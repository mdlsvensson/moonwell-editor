# Object Editor, milestones 1 and 2 (browse, then edit and save a map folder) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Tauri desktop app that opens a Warcraft III map folder, shows every object of the seven kinds against the
game's standard data with fast search, and edits and saves the map's object files exactly.

**Architecture:** A Cargo workspace of small crates: `objdata` (the binary object files and `war3map.wts`),
`basedata` (the standard data bundle), `mapfile` (reading and writing a map's files), `model` (resolved values,
edits, undo, save, search). `src-tauri` exposes `model` as Tauri commands; the Svelte front end in `src/` shows only
what is on screen. A .NET tool makes the development bundle with Moonwell's generator; a second .NET tool dumps object
files with War3Net as a test oracle.

**Tech Stack:** Rust 1.99 (edition 2024), Tauri 2, Svelte 5, TypeScript, Vite, Tailwind CSS 4, Lucide, fontsource
(Google Sans, Nunito, JetBrains Mono), Biome, Vitest, .NET 10 for the two tools.

**Spec:** `docs/superpowers/specs/2026-10-26-object-editor-design.md`. Mockup: `docs/mockups/editor.html`.

## Global Constraints

- No agent attribution anywhere (commits, files). Commit as the configured git identity. Never create or edit a
  `README.md`.
- Nothing is pushed and no GitHub repository is created; that is Sven's call, asked separately.
- `C:\Users\mdlsvensson\Repo\wc3-cs` and everything under it is read only.
- Blizzard's data is never committed: `base-data/` is git-ignored; fixtures hold only map files made by Sven or
  Moonwell.
- LF line ends (`.gitattributes` is in place; object files and `.wts` are binary / `-text`).
- Pin exact versions in `package.json` (no `^`); front end formatted by Biome (2-space indent, 100 columns); Rust by
  `rustfmt`, `cargo clippy --workspace -- -D warnings` clean.
- An unchanged object or strings file is written back byte for byte; a field the user did not touch is never changed.
- Object strings are raw bytes end to end in `objdata`; text is decoded as UTF-8 (lossy) only for display.
- Theme: Vitesse Dark tokens (from the mockup's `:root`): background `#121212`, surface `#181818`, text `#bfbaaa`,
  bright text `#dbd7ca`, muted `#959da5`, border `#262626`, accent `#4d9375` / `#80a665`, changed `#e6cc77`,
  warning `#cb7676`. Fonts: Google Sans (UI), Nunito (headings), JetBrains Mono (ids, values). No pixel fonts.
- Rust code stays small and plain (Sven is new to Rust): no macros of our own, no unsafe, `thiserror` for errors,
  doc comments on public items.

## Review Focus

1. A map folder with only some object files (no `.wts`, no skin files, one kind missing) opens, and saving creates
   only the files that now have content. Test: `model` Task 7 `opens_partial_folder`, Task 9
   `save_creates_missing_skin_file`.
2. A truncated or corrupt object file is reported with the file name and offset, the other kinds still open, and the
   damaged kind is read only. Test: `objdata` Task 2 `truncated_file_reports_offset`; `model` Task 7
   `corrupt_kind_is_read_only`.
3. The map changed on disk since it was opened (World Editor saved it): save refuses and says so. Test: Task 9
   `save_refuses_when_changed_on_disk`.
4. Floats: a value typed as `0.1` and the standard `0.100` compare equal, and an unedited float is written back with
   its exact bits. Test: Task 8 `float_equal_to_base_removes_record`; Task 2 `float_bits_round_trip`.
5. Non-UTF-8 bytes in a name round-trip unchanged and display with replacement marks. Test: Task 2
   `non_utf8_string_round_trips`; Task 7 `non_utf8_name_displays`.

---

## File Structure

```
Cargo.toml                    workspace: crates/*, src-tauri
package.json  vite.config.ts  svelte.config.js  tsconfig.json  biome.json  index.html
crates/objdata/src/lib.rs        Kind, RawId, re-exports
crates/objdata/src/objects.rs    ObjectFile read/write
crates/objdata/src/strings.rs    Strings (.wts) read/get/set/write, trigstr()
crates/objdata/tests/fixtures/we-3.0/      World Editor 3.0 fixture (from wc3-cs moonwell tests, WorldEditorObjects)
crates/objdata/tests/fixtures/moonwell-mymap/  Moonwell's output (MyMap/build/MyMap object files and .wts)
crates/objdata/tests/round_trip.rs, oracle.rs
crates/basedata/src/lib.rs       BaseData, KindData, FieldDef, StdObject, Choice
crates/mapfile/src/lib.rs        MapStore trait, FolderMap
crates/model/src/lib.rs          Session
crates/model/src/{resolve,edit,save,search,ids}.rs
src-tauri/src/{main,commands}.rs
src/lib/api.ts                   typed wrappers over invoke()
src/lib/{Tree,Details,FieldEditor,LevelGrid,Palette,StatusBar}.svelte
src/App.svelte  src/app.css
tools/BaseData/  (BaseData.csproj, Program.cs)     writes base-data/<game>.json
tools/Oracle/    (Oracle.csproj, Program.cs)       dumps an object file as JSON with War3Net
tools/Directory.Build.props      Wc3Cs path property, default ..\..\wc3-cs relative to the repo
```

---

### Task 1: Project skeleton that opens a window

**Files:**
- Create: `Cargo.toml`, `src-tauri/` (Tauri 2 app: `Cargo.toml`, `tauri.conf.json`, `build.rs`, `src/main.rs`,
  `capabilities/default.json`, icons), `package.json`, `vite.config.ts`, `svelte.config.js`, `tsconfig.json`,
  `biome.json`, `index.html`, `src/main.ts`, `src/App.svelte`, `src/app.css`, empty crates `crates/objdata`,
  `crates/basedata`, `crates/mapfile`, `crates/model` (each `Cargo.toml` + `src/lib.rs`), `src/lib/theme.test.ts`.
- Modify: `.gitignore` (add `base-data/`).

**Interfaces:**
- Produces: `npm run dev` (= `tauri dev`), `npm run check` (`biome check . && svelte-check && cargo clippy --workspace
  -- -D warnings`), `npm test` (`vitest run && cargo test --workspace`), `npm run format`.

- [ ] **Step 1:** Scaffold with `npm create tauri-app@latest` choices TypeScript / npm / Svelte into a scratch folder,
  then move its files in (keeping our `.gitattributes`, `.gitignore`, `docs/`). Product name "Object Editor",
  identifier `dev.wc3.objecteditor`, window 1400×900, min 1000×640, title "Object Editor".
- [ ] **Step 2:** Add Tailwind 4 (`@tailwindcss/vite`), Biome (config as steam2fiddy's `biome.json`, ignoring
  `docs/**/*.html`), Vitest, `@lucide/svelte`, `@fontsource/google-sans`, `@fontsource-variable/nunito`,
  `@fontsource-variable/jetbrains-mono`; pin every version exactly.
- [ ] **Step 3:** Write `src/lib/theme.test.ts`: reads `src/app.css` and asserts it defines `--bg: #121212`,
  `--accent: #4d9375`, `--changed: #e6cc77`, `--warn: #cb7676`, and that no `font-family` names a pixel font
  (`/pixel|press start|silkscreen/i`). Run `npx vitest run`; expect FAIL.
- [ ] **Step 4:** Write `src/app.css` with the tokens from the mockup and the fonts; `App.svelte` shows the empty
  layout: top bar ("Open map…" button), left tree pane, details pane, right pane, status bar (as the mockup).
- [ ] **Step 5:** `npm test` and `npm run check` pass. `npm run dev` opens the window; take a screenshot and compare
  with the mockup's colours and fonts.
- [ ] **Step 6:** Commit `Project skeleton: Tauri 2, Svelte 5, Tailwind 4, Biome, Vitest`.

### Task 2: Object files (`objdata`)

**Files:**
- Create: `crates/objdata/src/{lib,objects}.rs`, `crates/objdata/tests/round_trip.rs`, fixtures (copy unchanged:
  `C:\Users\mdlsvensson\Repo\wc3-cs\moonwell\tests\Moonwell.Build.Tests\Fixtures\WorldEditorObjects\*` except
  `README.md` into `tests/fixtures/we-3.0/`, plus a `SOURCE.md` saying where they came from; the `war3map*.w3?` and
  `war3map.wts` files of `C:\Users\mdlsvensson\Repo\MyMap\build\MyMap\` into `tests/fixtures/moonwell-mymap/`).

**Interfaces:**
- Produces:
  ```rust
  pub struct RawId(pub [u8; 4]);           // Display/FromStr as 4 ASCII chars; RawId::NONE = [0; 4]
  pub enum Kind { Unit, Item, Destructable, Doodad, Ability, Buff, Upgrade }
  impl Kind { pub const ALL: [Kind; 7]; pub fn extension(self) -> &'static str;   // "w3u", "w3t", "w3b", "w3d", "w3a", "w3h", "w3q"
              pub fn has_levels(self) -> bool; pub fn key(self) -> &'static str }  // has_levels: Doodad, Ability, Upgrade; key: "unit", "item", ...
  pub enum Value { Int(i32), Real(f32), Unreal(f32), Str(Vec<u8>) }               // file types 0, 1, 2, 3
  pub struct Modification { pub field: RawId, pub level: i32, pub pointer: i32, pub value: Value, pub end: [u8; 4] }
  pub struct ObjectRecord { pub base: RawId, pub id: RawId, pub sets: Vec<i32>, pub mods: Vec<Modification> }
  pub struct ObjectFile { pub version: i32, pub original: Vec<ObjectRecord>, pub custom: Vec<ObjectRecord>, pub trailing: Vec<u8> }
  pub fn read(kind: Kind, bytes: &[u8]) -> Result<ObjectFile, ReadError>;
  pub fn write(kind: Kind, file: &ObjectFile) -> Vec<u8>;
  pub enum ReadError { Truncated { offset: usize }, Version(i32), ValueType { offset: usize, value_type: i32 } }
  ```
  `level` and `pointer` are 0 and not written for kinds without levels. `sets` is empty for versions 1 and 2; for
  version 3 it is the set flags (the count is `sets.len()`), read as War3Net does. `id` is kept as stored, also in
  the original table (normally `RawId::NONE` there).

- [ ] **Step 1:** Tests in `round_trip.rs`: `every_fixture_file_round_trips` (each `.w3?` in both fixture folders:
  `write(read(b)) == b`); `we_fixture_values` (`war3mapSkin.w3a`: version 3, one custom record base `ANab` id `A000`,
  sets `[0]`, one mod `anam` level 0 pointer 0 value `Str(b"TRIGSTR_016")`; `war3mapSkin.w3q`'s `gnam` has level 1);
  `float_bits_round_trip` (a hand-built `.w3u` with `Real(f32::from_bits(0x3dcccccd))` and a NaN payload writes the
  same bits); `non_utf8_string_round_trips` (a `Str(vec![0xff, 0x41])`); `trailing_bytes_kept` (fixture bytes + `0u32`
  round-trip); `truncated_file_reports_offset` (each fixture cut at every length gives `Err(Truncated { offset })`
  with `offset <= len`, never a panic); `old_value_types_rejected` (type 4 gives `ValueType { value_type: 4, .. }`).
- [ ] **Step 2:** `cargo test -p objdata`; expect FAIL (not defined).
- [ ] **Step 3:** Implement with a small cursor over `&[u8]` (`i32::from_le_bytes`), no parsing crates.
- [ ] **Step 4:** `cargo test -p objdata`; expect PASS.
- [ ] **Step 5:** Commit `objdata: read and write object files`.

### Task 3: Strings file (`war3map.wts`)

**Files:** Create `crates/objdata/src/strings.rs`, tests in `crates/objdata/tests/strings.rs`.

**Interfaces:**
- Produces:
  ```rust
  pub struct Strings { /* the file as pieces, so untouched bytes are written back as read */ }
  impl Strings {
      pub fn read(bytes: &[u8]) -> Result<Strings, StringsError>;
      pub fn get(&self, number: u32) -> Option<&str>;
      pub fn set(&mut self, number: u32, text: &str) -> bool;   // false when no such entry
      pub fn write(&self) -> Vec<u8>;
  }
  pub fn trigstr(value: &[u8]) -> Option<u32>;   // b"TRIGSTR_016" -> Some(16); case-sensitive prefix, digits only
  ```
- [ ] **Step 1:** Tests: `wts_round_trips` (both fixtures byte-identical); `reads_entries` (we-3.0: `get(16)` is the
  ability's name from the fixture, read the file to pin the text); `set_keeps_bom_and_crlf` (after `set(16, "Line
  one\nLine two")` the output still starts `EF BB BF`, every line end is CRLF, the new text's lines are CRLF-joined,
  and every other byte is unchanged); `comments_and_padding_kept`; `trigstr_parses` (`TRIGSTR_016` 16, `TRIGSTR_`
  None, `trigstr_016` None).
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement (line-based; an entry is `STRING n`, optional `//` lines,
  `{`, text, `}`; detect CRLF vs LF from the first line end; a file with no line ends uses CRLF). **Step 4:** Run;
  expect PASS.
- [ ] **Step 5:** Commit `objdata: war3map.wts`.

### Task 4: War3Net oracle

**Files:** Create `tools/Directory.Build.props` (`<Wc3Cs Condition="'$(Wc3Cs)' == ''">$(MSBuildThisFileDirectory)..\..\wc3-cs</Wc3Cs>`),
`tools/Oracle/Oracle.csproj` (net10.0, `ProjectReference` to `$(Wc3Cs)\war3net\src\War3Net.Build.Core\War3Net.Build.Core.csproj`),
`tools/Oracle/Program.cs`; `crates/objdata/tests/oracle.rs`; a `dump` function in the test (not the crate).

**Interfaces:**
- Produces: `dotnet run --project tools/Oracle -- <kind> <file>` prints `{"version":3,"original":[...],"custom":[...]}`;
  a record is `{"base":"ANab","id":"A000","sets":[0],"mods":[{"field":"anam","type":3,"level":0,"pointer":0,"value":"TRIGSTR_016"}]}`;
  floats printed with `"R"` round-trip format, strings as UTF-8 text.

- [ ] **Step 1:** Test `oracle_agrees_on_every_fixture`: for every fixture object file, the Rust reader's dump (same
  JSON shape, built in the test with `serde_json`) equals the oracle's. If `dotnet` is not on PATH or the wc3-cs
  checkout is missing, the test prints why and returns (it is the only test allowed to skip).
- [ ] **Step 2:** Run; expect FAIL (no tool). **Step 3:** Write the tool (read the file with War3Net's
  `BinaryReader` extension for the kind, e.g. `ReadUnitObjectData(fromCampaign: false)`, ability with
  `ReadAbilityObjectData`; serialise with `System.Text.Json`). **Step 4:** Run; expect PASS.
- [ ] **Step 5:** Commit `Oracle: War3Net dump of object files for tests`.

### Task 5: Standard data bundle (`tools/BaseData`)

**Files:** Create `tools/BaseData/BaseData.csproj` (net10.0, `ProjectReference` to
`$(Wc3Cs)\moonwell\generator\Moonwell.Generator\Moonwell.Generator.csproj`; copy the NuGet source for
`Moonwell.War3Net.IO.Slk` from moonwell's `nuget.config` if restore needs it), `tools/BaseData/Program.cs`.

**Interfaces:**
- Consumes: `ExportTables.Read`, `Fields.Of`, `Fields.WithStandardNegatives`, `StandardObjects.Of`, `Choices.All`,
  `ExportTables.Meta` (for `category`, `sort`, `notSpecific`, use flags), `ExportTables.Objects` (for per-mode keys).
- Produces: `dotnet run --project tools/BaseData -- <war3.w3mod folder> <game version> <out file>` writes:
  ```json
  { "game": "3.0.1.24370",
    "choices": { "attackType": { "flags": false, "store": "string",
                 "entries": [ { "value": "normal", "label": "Normal" } ] } },
    "kinds": { "unit": { "levelField": null, "fields": [ ... ], "objects": [ ... ] }, "ability": { "levelField": "alev", ... } } }
  ```
  field: `{ "id", "label", "category", "sort", "type", "store", "listOf", "perLevel", "pointer", "skin", "specific",
  "notSpecific", "unit", "hero", "building", "min", "max", "maxLength", "maxEntries", "required", "noNegative",
  "canBeEmpty", "choice" }` (`type` lower camel of `FieldType`; `choice` the metadata type name or null).
  object: `{ "id", "name", "suffix", "race", "campaign", "hero", "building", "special", "code", "values", "modes",
  "sets", "missingFrom" }`; `values` keyed `fid` or `fid:level` as `StandardObject.Values`; `modes` only for keys
  whose column has `:sd`/`:hd`/`:de` values that differ: `{ "umdl": { "sd": "...", "hd": "...", "de": "..." } }`.
  Kind keys as `Kind::key`. The level field: the field named `LevelCount` (`alev`, `glvl`).
- [ ] **Step 1:** Run it on `C:\Users\mdlsvensson\Downloads\Work\war3.w3mod` with game `3.0.1.24370` into
  `base-data/3.0.1.24370.json`. Check by hand: `hfoo` has `uhpm` `420`; `Amls` has `acdn:1` `30`; `Hpal` has `ustr`,
  `hfoo` has not; `attackType` has `normal`. Note the file size in the commit message.
- [ ] **Step 2:** Commit `BaseData: the standard data bundle from Moonwell's generator` (the tool, not the bundle).

### Task 6: Load the bundle (`basedata`)

**Files:** Create `crates/basedata/src/lib.rs`, `crates/basedata/tests/load.rs`.

**Interfaces:**
- Consumes: `objdata::{Kind, RawId}`; the bundle from Task 5.
- Produces:
  ```rust
  pub struct BaseData { pub game: String, pub choices: HashMap<String, ChoiceType>, /* kinds */ }
  impl BaseData { pub fn load(path: &Path) -> Result<BaseData, LoadError>; pub fn kind(&self, kind: Kind) -> &KindData;
                  pub fn dev_bundle() -> Option<PathBuf> }   // newest base-data/*.json beside the workspace, or $OBJECT_EDITOR_BASE_DATA
  pub struct KindData { pub level_field: Option<RawId>, pub fields: Vec<FieldDef>, pub objects: Vec<StdObject> }
  impl KindData { pub fn field(&self, id: RawId) -> Option<&FieldDef>; pub fn object(&self, id: RawId) -> Option<&StdObject>;
                  pub fn applies(&self, field: &FieldDef, object: &StdObject) -> bool; pub fn levels(&self, object: &StdObject) -> u32 }
  pub struct FieldDef { /* the bundle's field, ids as RawId */ }
  pub struct StdObject { /* the bundle's object */ pub fn value(&self, field: RawId, level: u32) -> Option<&str> }
  ```
  `applies`: units by `building` / `hero` / `unit` flags (`Field.Applies` in Moonwell's `StandardObjects`), abilities
  by `specific` / `notSpecific` against `code`, every other kind true.
- [ ] **Step 1:** Tests (skip with a message when `dev_bundle()` is None): `known_values` (the four checks of Task 5);
  `counts` (each kind has more than 0 objects and fields; units more than 400); `hero_fields_only_on_heroes`
  (`ustr` applies to `Hpal`, not `hfoo`); `ability_levels` (`levels(AHhb) == 3`); `loads_fast` (under 1.5 s in
  release, `cargo test --release`).
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement with `serde` + `serde_json`. **Step 4:** PASS.
- [ ] **Step 5:** Commit `basedata: load the standard data bundle`.

### Task 7: Open a map folder and resolve values (`mapfile`, `model`)

**Files:** Create `crates/mapfile/src/lib.rs`, `crates/model/src/{lib,resolve}.rs`, `crates/model/tests/open.rs`.

**Interfaces:**
- Produces:
  ```rust
  // mapfile
  pub trait MapStore { fn read(&self, name: &str) -> io::Result<Option<Vec<u8>>>;
                       fn write(&mut self, files: &[(String, Option<Vec<u8>>)]) -> io::Result<()>;   // None deletes
                       fn stamp(&self) -> io::Result<u64>; fn describe(&self) -> String; }        // stamp: newest mtime of the object files and .wts
  pub struct FolderMap; impl FolderMap { pub fn open(path: &Path) -> io::Result<FolderMap> }
  // model
  pub struct Session; impl Session { pub fn open(store: Box<dyn MapStore>, base: Arc<BaseData>) -> Session }
  pub fn tree(&self, kind: Kind) -> Vec<TreeEntry>;      // TreeEntry { id, base: Option<String>, name, suffix, path: Vec<String>, changed: bool }
  pub fn object(&self, kind: Kind, id: &str) -> Option<ObjectView>;
  // ObjectView { kind, id, base: Option<String>, name, chain: Vec<(String, String)>, levels: u32, read_only: Option<String>, fields: Vec<FieldView> }
  // FieldView { id, label, category, sort, field_type, choice: Option<String>, level: u32, value: String, base: String,
  //             changed: bool, skin: bool, warning: Option<String>, modes: Option<BTreeMap<String, String>> }
  pub fn problems(&self) -> Vec<String>;                  // files that failed to read, per kind; and the build-output note
  ```
  When the folder is a Moonwell build output (its parent is `build/` and the folder above has a `.csproj`),
  `problems()` includes "This is Moonwell's build output, rewritten by every build: edit the project's map/ folder".
  Tree paths: custom objects `["Custom"]`; standard units `[race, "Melee" | "Campaign", "Units" | "Buildings" |
  "Heroes" | "Special"]`; other kinds `[race]` when the race is set, else `["Other"]`. Values: map record, else
  standard (for a custom object, its standard base; never the map's change to the base). A `TRIGSTR_n` value shows
  the `.wts` text. Floats show with Rust's shortest `{}` form. A kind whose file failed to read, or an object whose
  `sets.len() != 1` in a version 3 file, is read only with the reason in `read_only`.
- [ ] **Step 1:** Tests on copies of the fixtures in a temp folder: `custom_unit_from_we` (we-3.0 `h000`: base
  `hpea`, name from `.wts`, `uhpm` equals the standard `hpea` value, not changed); `standard_object_unchanged`
  (`hfoo` not in the map: every value is the standard one, `changed` false); `custom_inherits_standard_base_not_map_change`
  (a hand-written `war3map.w3u` changing `hfoo` `uhpm` to 500 and adding `h001` based on `hfoo`: `h001` shows 420);
  `opens_partial_folder` (only `war3map.w3u`); `corrupt_kind_is_read_only` (a 5-byte `war3map.w3a`: abilities read
  only, `problems()` names the file, units still open); `non_utf8_name_displays` (a `unam` of `[0xff, 0x41]` shows
  `"\u{fffd}A"`); `moonwell_mymap_opens` (every custom object of the MyMap fixture has a name and a chain);
  `moonwell_build_output_warns` (a temp `proj/App.csproj` + `proj/build/Map/` folder).
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `model: open a map folder and resolve values`.

### Task 8: Edits and undo (`model`)

**Files:** Create `crates/model/src/{edit,ids}.rs`, `crates/model/tests/edit.rs`.

**Interfaces:**
- Produces:
  ```rust
  pub fn set(&mut self, kind: Kind, id: &str, field: &str, level: u32, value: &str) -> Result<(), EditError>;
  pub fn reset(&mut self, kind: Kind, id: &str, field: &str, level: u32) -> Result<(), EditError>;
  pub fn create(&mut self, kind: Kind, base: &str, id: Option<&str>) -> Result<String, EditError>;   // returns the new id
  pub fn copy(&mut self, kind: Kind, from: &str, id: Option<&str>) -> Result<String, EditError>;     // custom copy keeps the base and the records
  pub fn delete(&mut self, kind: Kind, id: &str) -> Result<(), EditError>;
  pub fn undo(&mut self) -> bool; pub fn redo(&mut self) -> bool; pub fn dirty(&self) -> bool;
  pub enum EditError { NoObject, NoField, NotNumber(String), IdTaken(String), IdInvalid(String), ReadOnly(String) }
  ```
  A value is parsed by the field's `store`: `int` as i32 (yes/no fields send `"1"`/`"0"`), `real`/`unreal` as f32,
  `string` as bytes. A record goes to the skin file when the field's `skin` is true, else the main file; a new object
  gets a record (possibly empty) in both files, as World Editor writes. Setting a value equal to the base (numbers
  compared as numbers) removes the record unless the field has `modes` whose values differ. Editing a string whose
  record holds `TRIGSTR_n` changes the `.wts` entry and keeps the reference; any other string is written inline.
  New ids: World Editor's letters (unit `h`, hero `H`, item `I`, destructable `B`, doodad `D`, ability `A`, buff `B`,
  upgrade `R`) followed by the lowest free three characters counting `0-9` then `A-Z` from `000`, free among this
  kind's standard and custom ids. Warnings: the field's rules give `FieldView.warning` text ("Above World Editor's
  limit of 99999.", "Below World Editor's limit of 0.", "Longer than World Editor's limit of 32 characters.", "More
  than World Editor's limit of 4 entries.", "May not be empty."), never an error.
- [ ] **Step 1:** Tests: `set_and_read_back`; `set_goes_to_skin_file` (`unam` → skin, `uhpm` → main);
  `float_equal_to_base_removes_record` (`ua1c` of `hfoo` set to `"1.35"` after `"2"` leaves no record);
  `mode_values_keep_record`; `trigstr_edit_updates_wts`; `new_ids_follow_world_editor` (`create(Unit, "hfoo", None)`
  on the we-3.0 map gives `h001`; a hero base gives `H000`; `create(Ability, "AHhb", Some("A000"))` is `IdTaken`);
  `undo_redo_every_edit` (each operation, undone, leaves `object()` as before; redo restores); `past_limit_warns_not_fails`
  (`uhpm` `600000` sets, with the warning); `text_in_number_field_refused` (`NotNumber`); `read_only_kind_refuses`.
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement (each operation stores what it needs to invert itself). **Step 4:**
  PASS.
- [ ] **Step 5:** Commit `model: edits, new objects, undo and redo`.

### Task 9: Save (`model`)

**Files:** Create `crates/model/src/save.rs`, `crates/model/tests/save.rs`.

**Interfaces:**
- Produces: `pub fn save(&mut self) -> Result<SaveReport, SaveError>` with `SaveReport { written: Vec<String>,
  backup: PathBuf }`, `SaveError::ChangedOnDisk`, `SaveError::Io(io::Error)`; `pub fn reload(&mut self)`.
  Writes only files whose bytes changed; copies their previous bytes first to `<map folder>.backup/<YYYY-MM-DD
  HH-MM-SS>/` beside the map, keeping the newest 10 backup folders.
- [ ] **Step 1:** Tests: `save_writes_only_changed_files` (edit one unit field: only that kind's file changes, all
  other files' bytes and mtimes untouched); `saved_edit_reads_back_in_war3net` (after saving, the oracle's dump of the
  edited file shows the new value and every other record as before; skips like Task 4); `save_creates_missing_skin_file`;
  `backup_keeps_ten`; `save_refuses_when_changed_on_disk` (touch a file after open: `ChangedOnDisk`, nothing written);
  `dirty_clears_after_save`.
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `model: save with backups`.

### Task 10: Search (`model`)

**Files:** Create `crates/model/src/search.rs`, `crates/model/tests/search.rs`.

**Interfaces:**
- Produces: `pub fn search(&self, query: &str, limit: usize) -> Vec<Hit>`, `Hit { kind: Kind, id: String, name:
  String, detail: String, field: Option<String> }`. Every word of the query must match (case-insensitive) one of: the
  object's name, suffix, id; a field's label or id (then `field` is set and `detail` names the field); a field value
  of a custom or changed object. Ranking: exact id, then name prefix, then name contains, then field matches; custom
  objects before standard ones at equal rank.
- [ ] **Step 1:** Tests: `id_exact_first` (`hfoo` first for `hfoo`); `words_all_match` (`foot hum` finds the Footman);
  `finds_field_labels` (`hit points` gives the unit field); `finds_custom_values` (a custom unit with `unam` "Champion"
  found by `champ`); `fast_enough` (any 3-letter query under 30 ms in release on the full bundle).
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement (prebuilt lowercase index per kind, rebuilt on edit for the touched
  object). **Step 4:** PASS.
- [ ] **Step 5:** Commit `model: search`.

### Task 11: Commands and the front end's API

**Files:** Create `src-tauri/src/commands.rs`, modify `src-tauri/src/main.rs`; create `src/lib/api.ts`,
`src/lib/api.test.ts`.

**Interfaces:**
- Consumes: `Session` (Tasks 7-10), `BaseData::dev_bundle`.
- Produces commands (state `Mutex<Option<Session>>`, base data loaded once at start): `open_map(path) -> MapSummary
  { name, path, problems, counts }`, `tree(kind)`, `object(kind, id)`, `set_field(kind, id, field, level, value) ->
  ObjectView`, `reset_field(...) -> ObjectView`, `create_object(kind, base, id?) -> String`, `copy_object`,
  `delete_object`, `undo() -> bool`, `redo() -> bool`, `save() -> SaveReport`, `reload()`, `search(query, limit)`,
  `choices() -> map`, `game_version() -> String`. Errors return `{ code, message }`. Event `session-changed`
  `{ dirty: bool, changed_objects: u32 }` after every edit, undo, redo, save. `api.ts` exports one typed function per
  command and `onSessionChanged(cb)`.
- [ ] **Step 1:** `api.test.ts` with `@tauri-apps/api/mocks` `mockIPC`: each wrapper sends the command name and
  argument names the Rust side expects (camelCase as Tauri converts them), and `onSessionChanged` receives the event.
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement both sides; a missing dev bundle shows a clear error in the window
  ("No standard data: run tools/BaseData", with the command). **Step 4:** PASS; `npm run check` clean.
- [ ] **Step 5:** Commit `Commands and the typed front-end API`.

### Task 12: Browse UI (milestone 1)

**Files:** Create `src/lib/{Tree,Details,LevelGrid,Palette,StatusBar}.svelte`, `src/lib/fields.ts`,
`src/lib/fields.test.ts`; modify `src/App.svelte`.

**Interfaces:**
- Consumes: `api.ts`.
- Produces: `fields.ts` `groupFields(fields: FieldView[], filter: string, changedOnly: boolean): Category[]`
  (categories in World Editor's order, fields by `sort`; per-level fields collected into one grid row per field).

- [ ] **Step 1:** `fields.test.ts`: filter matches label, id and value; `changedOnly`; per-level fields become one
  grid row with levels in order; category order `stats, combat, abil, art, move, path, sound, tech, text, editor,
  data` *(agent's call; unknown categories last, by name)*.
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement `groupFields`, then the components as the mockup: kind buttons,
  virtualised tree with filter; details header with chain and "N fields changed"; field filter (Ctrl F), "Changed
  only", "Field ids" (Ctrl D); categories collapsible; per-level grid; side panel with the abilities of a unit
  (click opens them) and placeholders for "Used by" and the tooltip (milestone 6); Ctrl K palette over `search`;
  status bar with game version and dirty state; Alt ←/→ history. "Open map…" uses the dialog plugin to pick a folder.
- [ ] **Step 4:** PASS. Run `npm run dev`, open `crates/objdata/tests/fixtures/we-3.0` (copied to a temp folder) and
  Moonwell's MyMap fixture; screenshot the tree, a custom unit, an ability's level grid and the palette; check against
  the mockup. Opening a map and jumping to an object both feel instant (note the times).
- [ ] **Step 5:** Commit `Browse: tree, details, levels, search`. **Milestone 1 done; show Sven.**

### Task 13: Edit UI (milestone 2)

**Files:** Create `src/lib/FieldEditor.svelte`, `src/lib/values.ts`, `src/lib/values.test.ts`; modify
`Details.svelte`, `LevelGrid.svelte`, `Tree.svelte`, `App.svelte`.

**Interfaces:**
- Produces: `values.ts` `editorFor(field: FieldView, choices): Editor` (`number` for int/float, `toggle` for bool,
  `select` for one choice, `flags` for a flags choice, `ids` for object lists and refs, `text` otherwise) and
  `formatChoice` / `parseFlags` (comma lists as the data spells them).
- [ ] **Step 1:** `values.test.ts`: the editor per type; flags `air,enemies` ↔ two ticked entries; an int field
  rejects `1.5` before sending.
- [ ] **Step 2:** Run; FAIL. **Step 3:** Implement: inline editors in the details panel and the level grid (Enter
  commits, Esc cancels, Tab to next field); the yellow mark and ↺ reset; red warning text; object-list picker with
  search; new object (Ctrl Shift N, asks for an id with the next free one filled in), copy, delete (with a
  confirmation), F2 to change a name; Ctrl Z / Ctrl Y; Ctrl S save with the report in the status bar and the
  "saving is tested only on small maps" warning in the save button's tooltip until Task 14 passes; on
  `ChangedOnDisk`, a dialog offering Reload.
- [ ] **Step 4:** PASS. In `npm run dev`: edit a copy of the MyMap fixture (change a unit's life, add a unit from
  `hfoo`, change an ability's level 2 value, rename via a `TRIGSTR` name in the we-3.0 copy), save, reopen; the
  values are there. Screenshots.
- [ ] **Step 5:** Commit `Edit: field editors, new objects, undo, save`.

### Task 14: World Editor and game check (closes milestone 2)

Needs Sven's World Editor test map (spec: every tab, abilities with levels, doodad variations, edited standard units).

- [ ] **Step 1:** Copy his map's object files and `.wts` into `crates/objdata/tests/fixtures/we-heavy/` (with
  `SOURCE.md`); the round-trip and oracle tests now cover it; also check every record against the main/skin rule
  (a field with `skin` true only in the skin file) and record what does not fit in the spec.
- [ ] **Step 2:** Run `cargo test --workspace`; fix until PASS; commit `Fixtures: Sven's World Editor test map`.
- [ ] **Step 3:** With the app, on a copy of the map: change a standard unit, add a custom unit, an ability with
  three levels changed, a doodad variation, an upgrade level, and a `TRIGSTR` name; save.
- [ ] **Step 4:** Hand over with the wc3-cs `test-map` skill: Sven opens the saved map in World Editor (each change
  shows; nothing else changed in the Object Editor) and plays it (the changes act in the game). Record the result in
  `docs/checks.md` with the game version.
- [ ] **Step 5:** Remove the "small maps" warning; commit `Saving checked in World Editor and the game`.
  **Milestone 2 done.**
