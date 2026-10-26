# Object Editor, milestones 1 and 2 (browse, then edit and save a map folder) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows desktop app that opens a Warcraft III map folder, shows every object of the seven kinds against
the game's standard data with fast search, and edits and saves the map's object files exactly.

**Architecture:** C# libraries (`ObjectEditor.Files` on War3Net, `ObjectEditor.GameData` on Moonwell's generator,
`ObjectEditor.Model`) under a WinForms window holding one WebView2 (`ObjectEditor.App`), whose `Bridge` turns JSON
requests from the Svelte front end (`web/`) into model calls. Everything heavy happens in C#; the web side shows only
what is on screen.

**Tech Stack:** .NET 10, `Moonwell.War3Net.Build.Core` (Sven's War3Net fork), `Moonwell.Generator` (project
reference into wc3-cs), `Microsoft.Web.WebView2`, xUnit; Svelte 5, TypeScript, Vite, Tailwind CSS 4, Lucide,
fontsource, Biome, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-26-object-editor-design.md`. Mockup: `docs/mockups/editor.html`.

## Global Constraints

- No agent attribution anywhere (commits, files). Commit as the configured git identity. Never create or edit a
  `README.md`. Nothing is pushed and no GitHub repository is created.
- `C:\Users\mdlsvensson\Repo\wc3-cs` and everything under it is read only (fixtures are copied out of it).
- Blizzard's data is never committed or shipped; fixtures hold only map files made by Sven or Moonwell.
- C#: .NET 10, `Nullable` enable, `TreatWarningsAsErrors` true, `ImplicitUsings` enable, file-scoped namespaces,
  `///` summaries on public types and members, `.editorconfig` as Moonwell's (4 spaces, LF; fixtures exempt). Code
  stays plain (Sven knows GDScript best and some C#): no reflection, no source generators, LINQ where it reads easier.
- Front end: exact versions in `package.json`; Biome (2 spaces, 100 columns); no pixel fonts.
- An unchanged object or strings file is written back byte for byte; a field the user did not touch is never changed;
  a kind whose file War3Net cannot write back exactly is read only.
- Theme (Vitesse Dark, from the mockup): background `#121212`, surface `#181818`, text `#bfbaaa`, bright text
  `#dbd7ca`, muted `#959da5`, border `#262626`, accent `#4d9375` / `#80a665`, changed `#e6cc77`, warning `#cb7676`.
  Fonts: Google Sans (UI), Nunito (headings), JetBrains Mono (ids, values).
- Development paths: wc3-cs checkout `C:\Users\mdlsvensson\Repo\wc3-cs` (MSBuild property `Wc3Cs`, default
  `$(MSBuildThisFileDirectory)..\wc3-cs` from the repo root); game data folder from the environment variable
  `OBJECT_EDITOR_GAME_DATA` in tests (Sven's: `C:\Users\mdlsvensson\Downloads\Work\war3.w3mod`); tests that need it
  print why and return when it is unset or missing.

## Review Focus

1. A map folder with only some object files (no `.wts`, no skin files, one kind missing) opens, and saving creates
   only the files that now have content. Tests: Task 6 `Opens_partial_folder`, Task 8
   `Save_creates_missing_skin_file`.
2. A truncated, corrupt or lossy object file makes only its kind read only, with the file named, and never crashes.
   Tests: Task 2 `Truncated_file_is_a_problem`, `Trailing_table_is_a_problem`, `Non_utf8_name_is_a_problem`; Task 6
   `Problem_kind_is_read_only`.
3. The map changed on disk since it was opened: save refuses and offers reload. Test: Task 8
   `Save_refuses_when_changed_on_disk`.
4. Floats: `0.1` typed and a standard `0.100` compare equal; an unedited float keeps its exact bits (the guard).
   Test: Task 7 `Float_equal_to_base_removes_record`.
5. A command that throws in C# reaches the web side as an error message, and the window stays usable. Test: Task 10
   `Command_exception_becomes_error_reply`.

---

## File Structure

```
ObjectEditor.slnx  Directory.Build.props  .editorconfig  global.json
src/ObjectEditor.Files/      ObjectKind.cs  ObjectFile.cs  ObjectEntry.cs  FieldMod.cs  WtsFile.cs  MapFolder.cs
src/ObjectEditor.GameData/   GameData.cs  KindData.cs  FieldInfo.cs  StandardObject.cs  ChoiceList.cs  GameDataCache.cs
src/ObjectEditor.Model/      Session.cs  Views.cs  Resolver.cs  Edits.cs  History.cs  NewIds.cs  Saver.cs  Search.cs
src/ObjectEditor.App/        Program.cs  MainForm.cs  Bridge.cs  Commands.cs  Settings.cs
web/                         package.json  vite.config.ts  index.html  src/{main.ts,App.svelte,app.css}  src/lib/*
tests/ObjectEditor.*.Tests/  one test project per library and the App
tests/fixtures/we-3.0/          World Editor 3.0 output (copied from wc3-cs WorldEditorObjects, with SOURCE.md)
tests/fixtures/moonwell-mymap/  Moonwell's output (MyMap/build/MyMap object files and .wts, with SOURCE.md)
```

---

### Task 1: Solution, window and web skeleton

**Files:**
- Create: `ObjectEditor.slnx`, `Directory.Build.props`, `.editorconfig` (Moonwell's, with `tests/fixtures/**`
  exempt), `global.json` (SDK 10), `src/ObjectEditor.App/{ObjectEditor.App.csproj,Program.cs,MainForm.cs,Bridge.cs}`,
  `tests/ObjectEditor.App.Tests/{ObjectEditor.App.Tests.csproj,BridgeTests.cs}`, `web/` (Vite + Svelte 5 + TS +
  Tailwind 4 + Biome + Vitest + fontsource + Lucide), `web/src/lib/theme.test.ts`, `web/src/lib/bridge.ts`,
  `web/src/lib/bridge.test.ts`.
- Modify: `.gitignore` (add `bin/`, `obj/`, `web/node_modules/`, `web/dist/`; remove the Rust lines).

**Interfaces:**
- Produces:
  ```csharp
  // ObjectEditor.App
  public sealed class Bridge
  {
      public Bridge(Func<string, JsonElement, Task<object?>> run);   // run(command, args) -> result (serialised camelCase)
      public Task<string> Handle(string requestJson);                 // {"id","command","args"} -> {"id","ok":true,"result"} | {"id","ok":false,"error":{"code","message"}}
      public static string Event(string name, object payload);       // {"event","payload"}
  }
  ```
  ```ts
  // web/src/lib/bridge.ts
  export function call<T>(command: string, args?: object): Promise<T>;   // rejects with Error(message) when ok is false
  export function on(event: string, handler: (payload: unknown) => void): () => void;
  ```
  `MainForm` hosts one `WebView2`; in Debug with `OBJECT_EDITOR_DEV_URL` set it navigates there (Vite,
  `http://localhost:5173`), otherwise it maps the published `wwwroot` folder (the `web/dist` output copied on build)
  to `https://app.objecteditor/` with `SetVirtualHostNameToFolderMapping`. Messages: `WebMessageReceived` →
  `Bridge.Handle` on a background task (one at a time, `SemaphoreSlim`) → `PostWebMessageAsString` on the UI thread.
  For now `run` knows one command, `ping`, returning `"pong"`.
  Scripts: `npm run dev` (Vite), `npm run build`, `npm test` (Vitest), `npm run check` (Biome + svelte-check); from
  the root, `dotnet build` and `dotnet test`.

- [ ] **Step 1:** Tests: `BridgeTests.Ping_round_trips`, `BridgeTests.Unknown_command_is_an_error` (`ok` false,
  `code` `"unknownCommand"`), `BridgeTests.Command_exception_becomes_error_reply` (a `run` that throws gives `ok`
  false with the exception's message, and the next request still works); `theme.test.ts` (`app.css` defines `--bg:
  #121212`, `--accent: #4d9375`, `--changed: #e6cc77`, `--warn: #cb7676`; no font matches
  `/pixel|press start|silkscreen/i`); `bridge.test.ts` (with a fake `window.chrome.webview`: `call` resolves with
  `result`, rejects on `ok: false`, matches replies by `id` out of order; `on` receives events and unsubscribes).
- [ ] **Step 2:** Run `dotnet test` and `npm test` in `web/`; expect FAIL.
- [ ] **Step 3:** Implement. `App.svelte` shows the mockup's empty layout (top bar with "Open map…", left, middle and
  right panes, status bar) and the reply of `call("ping")` in the status bar.
- [ ] **Step 4:** Tests PASS; `npm run check` clean; `dotnet build` with no warnings. Start the app with the Vite dev
  server and confirm the window opens and shows "pong" (the controller takes the screenshot).
- [ ] **Step 5:** Commit `Skeleton: WinForms + WebView2 window, bridge, Svelte front end`.

### Task 2: Object files through War3Net, with the round-trip guard

**Files:**
- Create: `src/ObjectEditor.Files/{ObjectEditor.Files.csproj,ObjectKind.cs,ObjectFile.cs,ObjectEntry.cs,FieldMod.cs,MapFolder.cs}`,
  `tests/ObjectEditor.Files.Tests/{ObjectEditor.Files.Tests.csproj,ObjectFileTests.cs,Fixtures.cs}`, the fixtures
  (copy unchanged: every file of `C:\Users\mdlsvensson\Repo\wc3-cs\moonwell\tests\Moonwell.Build.Tests\Fixtures\WorldEditorObjects\`
  except `README.md` into `tests/fixtures/we-3.0/`; the `war3map*.w3?` files and `war3map.wts` of
  `C:\Users\mdlsvensson\Repo\MyMap\build\MyMap\` into `tests/fixtures/moonwell-mymap/`; a `SOURCE.md` in each).

**Interfaces:**
- Produces:
  ```csharp
  public enum ObjectKind { Unit, Item, Destructable, Doodad, Ability, Buff, Upgrade }
  public static class ObjectKinds
  {
      public static string Extension(ObjectKind kind);   // "w3u" "w3t" "w3b" "w3d" "w3a" "w3h" "w3q"
      public static bool HasLevels(ObjectKind kind);      // Doodad, Ability, Upgrade
      public static string Key(ObjectKind kind);          // "unit" "item" "destructable" "doodad" "ability" "buff" "upgrade"
      public static ObjectKind? FromKey(string key);
  }
  public sealed class ObjectFile        // one main or skin file of one kind, wrapping War3Net's object
  {
      public static ObjectFile Read(ObjectKind kind, byte[] bytes);   // throws ObjectFileException (message names the problem)
      public static ObjectFile Empty(ObjectKind kind);                 // version 3, no records
      public ObjectKind Kind { get; }
      public IReadOnlyList<ObjectEntry> Original { get; }              // changes to standard objects
      public IReadOnlyList<ObjectEntry> Custom { get; }
      public ObjectEntry AddCustom(string baseId, string id);          // set flags [0] in version 3
      public ObjectEntry AddOriginal(string id);
      public void Remove(ObjectEntry entry);
      public byte[] ToBytes();
  }
  public sealed class ObjectEntry { public string BaseId; public string Id; public int SetCount;   // Id "" for original-table entries
      public IReadOnlyList<FieldMod> Mods { get; }
      public FieldMod? Find(string field, int level);
      public FieldMod Set(string field, int level, int pointer, FieldValue value);   // replaces in place or appends
      public void Remove(FieldMod mod); }
  public sealed class FieldMod { public string Field; public int Level; public int Pointer; public FieldValue Value; }
  public abstract record FieldValue { Int(int), Real(float), Unreal(float), Text(string) }   // nested records
  public sealed class ObjectFileException : Exception { }
  ```
  `ObjectFile.Read` reads with War3Net (`reader.ReadUnitObjectData()` and the other six), then **the guard**:
  `ToBytes()` must equal the input, else `ObjectFileException("War3Net cannot write war3map.w3a back exactly: ...")`
  naming the first differing offset. War3Net's own exceptions (end of stream, bad UTF-8) become
  `ObjectFileException` with the file's name and War3Net's message. The wrappers edit War3Net's objects in place
  (its internal end marker is kept as read; new records get 0). Level and pointer are 0 for kinds without levels.
  `MapFolder`: `Open(string path)`, `byte[]? Read(string name)`, `void Write(IReadOnlyList<(string Name, byte[]? Bytes)>)`
  (null deletes), `DateTime Stamp()` (newest write time of the object files and `.wts`), `string Path`.

- [ ] **Step 1:** Tests: `Every_fixture_file_round_trips` (every `.w3?` of both fixtures reads, `ToBytes()` equals
  the file); `We_fixture_values` (`war3mapSkin.w3a`: one custom entry base `ANab`, id `A000`, set count 1, one mod
  `anam` level 0 pointer 0 `Text("TRIGSTR_016")`; `war3mapSkin.w3q`'s `gnam` at level 1); `Truncated_file_is_a_problem`
  (every fixture cut at every shorter length throws `ObjectFileException`, never another exception);
  `Trailing_table_is_a_problem` (a fixture plus 4 zero bytes throws, message says "exactly");
  `Non_utf8_name_is_a_problem` (a fixture's string bytes patched to `0xFF`); `Edit_then_reread` (set `uhpm` on a
  custom unit, `ToBytes()`, read again: the mod is there, every other mod unchanged); `Skin_split_matches_fixture`
  (record for the controller: list which file each fixture mod is in; assert `unam`, `anam`, `gnam`, `fnam`, `bnam`
  are in skin files and `dnam` in the main file, as the fixture README says).
- [ ] **Step 2:** `dotnet test tests/ObjectEditor.Files.Tests`; expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** Commit `Files: object files through War3Net with a round-trip guard`.

### Task 3: The strings file (`WtsFile`)

**Files:** Create `src/ObjectEditor.Files/WtsFile.cs`, `tests/ObjectEditor.Files.Tests/WtsFileTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public sealed class WtsFile
  {
      public static WtsFile Read(byte[] bytes);      // keeps the file as pieces: untouched bytes are written back as read
      public string? Get(int number);
      public bool Set(int number, string text);      // false when there is no such entry
      public byte[] ToBytes();
      public static int? TriggerString(string value); // "TRIGSTR_016" -> 16; "TRIGSTR_" and "trigstr_016" -> null
  }
  ```
- [ ] **Step 1:** Tests: `Round_trips_both_fixtures`; `Reads_entries` (we-3.0 `Get(16)` is the ability's name; read
  the file to pin the text); `Set_keeps_bom_and_crlf` (after `Set(16, "Line one\nLine two")` the bytes still start
  `EF BB BF`, every line end is CRLF, the text's lines are joined with CRLF, every other byte unchanged);
  `Comments_and_padding_kept`; `Trigger_string_parses`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement (line-based: `STRING n`, optional `//` lines, `{`, text, `}`; the line
  end of the first line is the file's; a file with none uses CRLF). **Step 4:** PASS.
- [ ] **Step 5:** Commit `Files: war3map.wts reader and writer`.

### Task 4: The game's standard data (`ObjectEditor.GameData`)

**Files:** Create `src/ObjectEditor.GameData/*.cs` and `.csproj` (`ProjectReference` to
`$(Wc3Cs)\moonwell\generator\Moonwell.Generator\Moonwell.Generator.csproj`; add moonwell's NuGet source if restore
needs it), `tests/ObjectEditor.GameData.Tests/GameDataTests.cs`.

**Interfaces:**
- Consumes: `ExportTables.Read`, `Choices.All(tables, null)`, `Fields.Of(tables, kind)`, `StandardObjects.Of`,
  `Fields.WithStandardNegatives`, `ExportTables.Meta(kind)` (`category`, `sort`, `notSpecific`, use flags),
  `ExportTables.Objects(kind, null)` (the `:sd`/`:hd`/`:de` keys), `ExportTables.Label`.
- Produces:
  ```csharp
  public sealed class GameData
  {
      public static GameData Load(string folder, string cacheFolder);   // builds with Moonwell's generator, or reads the cache when the folder's files are unchanged
      public string Source { get; }                                     // the folder
      public KindData Kind(ObjectKind kind);
      public IReadOnlyDictionary<string, ChoiceList> Choices { get; }   // by metadata type: attackType, targetList, ...
  }
  public sealed class KindData
  {
      public string? LevelField { get; }                                // "alev" for abilities, "glvl" for upgrades
      public IReadOnlyList<FieldInfo> Fields { get; }
      public IReadOnlyList<StandardObject> Objects { get; }
      public FieldInfo? Field(string id); public StandardObject? Object(string id);
      public bool Applies(FieldInfo field, StandardObject obj);         // Moonwell's FieldPlan.Applies rules
      public int Levels(StandardObject obj);                            // 0 for kinds without levels
  }
  public sealed record FieldInfo(string Id, string Label, string Category, string Sort, string Type, string Store,
      string? ListOf, bool PerLevel, int Pointer, bool Skin, IReadOnlyList<string> Specific,
      IReadOnlyList<string> NotSpecific, bool ForUnit, bool ForHero, bool ForBuilding, double? Min, double? Max,
      int? MaxLength, int? MaxEntries, bool Required, bool NoNegative, string? Choice);
  public sealed record StandardObject(string Id, string Name, string Suffix, string Race, bool Campaign, bool Hero,
      bool Building, bool Special, string? Code, IReadOnlyDictionary<string, string> Values,
      IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Modes);   // Values keyed "fid" or "fid:level"; Modes: key -> {"sd","hd","de"} only where they differ
  public sealed record ChoiceList(bool Flags, string Store, IReadOnlyList<ChoiceEntry> Entries);
  public sealed record ChoiceEntry(string Value, string Label);
  ```
  `Type` is the lower camel case of Moonwell's `FieldType` (`int`, `float`, `bool`, `string`, `objectList`,
  `objectRef`, `choice`). Cache: `cacheFolder\<hash of folder path>.json` with the newest file time of the folder; the
  app passes `%LOCALAPPDATA%\ObjectEditor\gamedata`.
- [ ] **Step 1:** Tests (skip with a message when `OBJECT_EDITOR_GAME_DATA` is unset or missing): `Known_values`
  (`hfoo` `uhpm` `420`; `Amls` `acdn:1` `30`; `attackType` has `normal`); `Hero_fields_only_on_heroes` (`ustr`
  applies to `Hpal`, not `hfoo`); `Ability_levels` (`Levels(AHhb)` 3); `Counts` (every kind has objects and fields;
  units more than 400); `Cache_is_used_second_time` (second `Load` with the same cache folder is at least 5 times
  faster and gives equal data); `Cached_load_is_fast` (under 1.5 s).
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS; note the first and cached load times in the report.
- [ ] **Step 5:** Commit `GameData: the standard data from Moonwell's generator, cached`.

### Task 5: Settings and the first real command

**Files:** Create `src/ObjectEditor.App/{Settings.cs,Commands.cs}`, `tests/ObjectEditor.App.Tests/SettingsTests.cs`;
modify `MainForm.cs`, `Bridge` wiring.

**Interfaces:**
- Produces: `Settings` (`GameDataFolder`, `RecentMaps`), stored as JSON in `%LOCALAPPDATA%\ObjectEditor\settings.json`
  (a constructor taking the folder, for tests). Commands: `gameData()` → `{ source, loaded, error }`;
  `chooseGameData()` opens a folder dialog (UI thread), loads, saves the setting. The game data loads on a background
  task at start; until it is loaded, commands that need it return error code `"gameDataLoading"`; with no setting,
  `"gameDataMissing"`.
- [ ] **Step 1:** Tests: `Settings_round_trip`, `Missing_settings_file_gives_defaults`, `Corrupt_settings_file_gives_defaults`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement; the web side shows a first-start screen ("Choose the game data
  folder…") when `gameDataMissing`. **Step 4:** PASS.
- [ ] **Step 5:** Commit `App: settings and the game data folder`.

### Task 6: Open a map folder and resolve values (`ObjectEditor.Model`)

**Files:** Create `src/ObjectEditor.Model/{Session.cs,Views.cs,Resolver.cs}`, `tests/ObjectEditor.Model.Tests/OpenTests.cs`.

**Interfaces:**
- Consumes: `ObjectFile`, `WtsFile`, `MapFolder`, `GameData`.
- Produces:
  ```csharp
  public sealed class Session
  {
      public static Session Open(MapFolder map, GameData game);
      public IReadOnlyList<string> Problems { get; }                    // per file, and the Moonwell build-output note
      public IReadOnlyList<TreeEntry> Tree(ObjectKind kind);
      public ObjectView? Object(ObjectKind kind, string id);
  }
  public sealed record TreeEntry(string Id, string? BaseId, string Name, string Suffix, IReadOnlyList<string> Path, bool Changed);
  public sealed record ObjectView(string Kind, string Id, string? BaseId, string Name, IReadOnlyList<ChainLink> Chain,
      int Levels, string? ReadOnly, IReadOnlyList<FieldView> Fields);
  public sealed record ChainLink(string Name, string Id);
  public sealed record FieldView(string Id, string Label, string Category, string Sort, string Type, string? Choice,
      int Level, string Value, string Base, bool Changed, bool Skin, string? Warning, IReadOnlyDictionary<string, string>? Modes);
  ```
  Tree paths: custom `["Custom"]`; standard units `[race, "Melee" | "Campaign", "Units" | "Buildings" | "Heroes" |
  "Special"]`; other kinds `[race]` when set, else `["Other"]`. Values: the map's record, else the standard value
  (a custom object's own standard base, never the map's change to the base). A `TRIGSTR_n` value shows the `.wts`
  text. Floats show with `float.ToString("R", CultureInfo.InvariantCulture)`. A kind whose file is a problem, or an
  entry with a set count other than 1, is read only with the reason in `ReadOnly`. A folder whose parent is `build`
  with a `.csproj` beside it gets the problem "This is Moonwell's build output, rewritten by every build: edit the
  project's map/ folder".
- [ ] **Step 1:** Tests on temp copies of the fixtures (game data as in Task 4): `Custom_unit_from_we` (`h000`: base
  `hpea`, name from `.wts`, `uhpm` equals the standard `hpea` value, not changed); `Standard_object_unchanged`;
  `Custom_inherits_standard_base_not_map_change` (a written `war3map.w3u` changing `hfoo` `uhpm` to 500 and adding
  `h001` based on `hfoo`: `h001` shows 420); `Opens_partial_folder` (only `war3map.w3u`); `Problem_kind_is_read_only`
  (a 5-byte `war3map.w3a`: abilities read only, `Problems` names the file, units open); `Moonwell_mymap_opens` (every
  custom object has a name and a chain); `Moonwell_build_output_warns`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `Model: open a map folder and resolve values`.

### Task 7: Edits and undo

**Files:** Create `src/ObjectEditor.Model/{Edits.cs,History.cs,NewIds.cs}`, `tests/ObjectEditor.Model.Tests/EditTests.cs`.

**Interfaces:**
- Produces on `Session`:
  ```csharp
  public void Set(ObjectKind kind, string id, string field, int level, string value);   // throws EditException(code, message)
  public void Reset(ObjectKind kind, string id, string field, int level);
  public string Create(ObjectKind kind, string baseId, string? id);    // the new id
  public string Copy(ObjectKind kind, string fromId, string? id);
  public void Delete(ObjectKind kind, string id);
  public bool Undo(); public bool Redo(); public bool Dirty { get; }
  ```
  `EditException.Code`: `noObject`, `noField`, `notNumber`, `idTaken`, `idInvalid`, `readOnly`. A value is parsed by
  the field's `Store`: `int` (yes/no fields send `"1"`/`"0"`), `real`/`unreal` as float (invariant culture),
  `string` as text. A mod goes to the skin file when the field's `Skin` is true, else the main file; a new object gets
  an entry in both files. Setting a value equal to the base (numbers compared as numbers) removes the mod unless the
  field has `Modes` that differ. Editing a string whose mod holds `TRIGSTR_n` changes the `.wts` entry and keeps the
  reference; other strings are written inline. New ids: `h` (unit), `H` (hero base), `I`, `B` (destructable), `D`,
  `A`, `B` (buff), `R`, then the lowest free three characters counting `0-9` then `A-Z` from `000`, free among the
  kind's standard and custom ids. Warnings in `FieldView.Warning`: "Above World Editor's limit of {max}.", "Below
  World Editor's limit of {min}.", "Longer than World Editor's limit of {n} characters.", "More than World Editor's
  limit of {n} entries.", "May not be empty.".
- [ ] **Step 1:** Tests: `Set_and_read_back`; `Set_goes_to_skin_file` (`unam` skin, `uhpm` main);
  `Float_equal_to_base_removes_record` (`ua1c` of `hfoo` set `"2"` then `"1.35"`: no mod);
  `Mode_values_keep_record`; `Trigstr_edit_updates_wts`; `New_ids_follow_world_editor` (on we-3.0: `Create(Unit,
  "hfoo", null)` is `h001`; a hero base gives `H000`; `Create(Ability, "AHhb", "A000")` throws `idTaken`);
  `Undo_redo_every_edit`; `Past_limit_warns_not_fails` (`uhpm` `600000`); `Text_in_number_field_refused`;
  `Read_only_kind_refuses`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement (each operation records what it needs to undo itself). **Step 4:** PASS.
- [ ] **Step 5:** Commit `Model: edits, new objects, undo and redo`.

### Task 8: Save

**Files:** Create `src/ObjectEditor.Model/Saver.cs`, `tests/ObjectEditor.Model.Tests/SaveTests.cs`.

**Interfaces:**
- Produces on `Session`: `SaveReport Save()` (`SaveReport(IReadOnlyList<string> Written, string Backup)`), throws
  `ChangedOnDiskException`; `void Reload()`. Writes only files whose bytes changed, after copying their previous
  bytes to `<map folder>.backup\<yyyy-MM-dd HH-mm-ss>\`, keeping the newest 10 backup folders.
- [ ] **Step 1:** Tests: `Save_writes_only_changed_files` (other files' bytes and write times untouched);
  `Saved_edit_reads_back` (reopen: new value, every other mod equal); `Save_creates_missing_skin_file`;
  `Backup_keeps_ten`; `Save_refuses_when_changed_on_disk`; `Dirty_clears_after_save`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `Model: save with backups`.

### Task 9: Search

**Files:** Create `src/ObjectEditor.Model/Search.cs`, `tests/ObjectEditor.Model.Tests/SearchTests.cs`.

**Interfaces:**
- Produces on `Session`: `IReadOnlyList<Hit> Search(string query, int limit)`, `Hit(string Kind, string Id, string
  Name, string Detail, string? Field)`. Every word must match (ignoring case) the name, suffix or id, a field's label
  or id (then `Field` is set), or a value of a custom or changed object. Order: exact id, name prefix, name contains,
  field matches; custom before standard at equal rank.
- [ ] **Step 1:** Tests: `Id_exact_first`; `Words_all_match` (`foot hum`); `Finds_field_labels` (`hit points`);
  `Finds_custom_values` (`champ` finds a custom unit named "Champion"); `Fast_enough` (any 3-letter query under 30 ms
  in Release on the full game data).
- [ ] **Step 2:** FAIL. **Step 3:** Implement (a lowercase index per kind, updated for the edited object). **Step 4:**
  PASS.
- [ ] **Step 5:** Commit `Model: search`.

### Task 10: Commands for the model

**Files:** Modify `src/ObjectEditor.App/Commands.cs`; create `tests/ObjectEditor.App.Tests/CommandTests.cs`,
`web/src/lib/api.ts`, `web/src/lib/api.test.ts`.

**Interfaces:**
- Produces commands (camelCase JSON; `kind` is `ObjectKinds.Key`): `openMap({ path? })` (no path: folder dialog) →
  `{ name, path, problems, counts }`; `tree({ kind })`; `object({ kind, id })`; `setField({ kind, id, field, level,
  value })` → `ObjectView`; `resetField(...)` → `ObjectView`; `createObject({ kind, baseId, id? })` → id;
  `copyObject`; `deleteObject`; `undo()`; `redo()`; `save()` → `SaveReport`; `reload()`; `search({ query, limit })`;
  `choices()`. Errors: `{ code, message }` with `EditException` codes, `changedOnDisk`, `noMap`. Event
  `sessionChanged` `{ dirty, changedObjects }` after every edit, undo, redo, save and reload. `api.ts`: one typed
  function per command and `onSessionChanged(handler)`.
- [ ] **Step 1:** Tests: `CommandTests` drive the `Bridge` with JSON over a fixture copy and the game data
  (`Open_tree_object_set_undo_save` in one flow; `Edit_error_has_code`); `api.test.ts` checks each wrapper's command
  name and argument names against a mocked `call`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `App: model commands and the typed web API`.

### Task 11: Browse UI (milestone 1)

**Files:** Create `web/src/lib/{Tree,Details,LevelGrid,Palette,StatusBar}.svelte`, `web/src/lib/fields.ts`,
`web/src/lib/fields.test.ts`; modify `web/src/App.svelte`.

**Interfaces:**
- Produces: `groupFields(fields: FieldView[], filter: string, changedOnly: boolean): Category[]` (categories in the
  order `stats, combat, abil, art, move, path, sound, tech, text, editor, data` *(agent's call)*, unknown ones last by
  name; fields by `sort`; per-level fields collected into one grid row per field).
- [ ] **Step 1:** `fields.test.ts`: filter matches label, id and value; changed only; per-level rows; category order.
- [ ] **Step 2:** FAIL. **Step 3:** Implement as the mockup: kind buttons; virtualised tree with filter; details
  header with chain and "N fields changed"; field filter (Ctrl F), "Changed only", "Field ids" (Ctrl D);
  collapsible categories; per-level grid; side panel with a unit's abilities (click opens them) and placeholders for
  "Used by" and the tooltip; Ctrl K palette over `search`; status bar with the game data source, problems and dirty
  state; Alt ←/→ history; "Open map…".
- [ ] **Step 4:** PASS. Run the app on copies of both fixtures; the controller screenshots the tree, a custom unit,
  an ability's level grid and the palette against the mockup. Note how long opening and jumping take.
- [ ] **Step 5:** Commit `Browse: tree, details, levels, search`. **Milestone 1 done; show Sven.**

### Task 12: Edit UI (milestone 2)

**Files:** Create `web/src/lib/{FieldEditor.svelte,values.ts,values.test.ts}`; modify `Details.svelte`,
`LevelGrid.svelte`, `Tree.svelte`, `App.svelte`.

**Interfaces:**
- Produces: `editorFor(field: FieldView, choices): "number" | "toggle" | "select" | "flags" | "ids" | "text"`;
  `parseFlags` / `formatFlags` (comma lists as the data spells them).
- [ ] **Step 1:** `values.test.ts`: the editor per type; `air,enemies` ↔ two ticked entries; an int field rejects
  `1.5` before sending.
- [ ] **Step 2:** FAIL. **Step 3:** Implement: inline editors in the details panel and the level grid (Enter commits,
  Esc cancels, Tab moves on); the yellow mark and ↺ reset; red warning text; object-list picker with search; new
  object (Ctrl Shift N, the next free id filled in), copy, delete (with a confirmation), F2 for the name; Ctrl Z /
  Ctrl Y; Ctrl S with the report in the status bar and the warning "Saving is tested only on small maps" until Task
  13 passes; on `changedOnDisk`, a dialog offering Reload.
- [ ] **Step 4:** PASS. In the app, on a copy of the MyMap fixture: change a unit's life, add a unit from `hfoo`,
  change an ability's level 2 value; on a copy of we-3.0 change a `TRIGSTR` name; save, reopen, values there. The
  controller screenshots.
- [ ] **Step 5:** Commit `Edit: field editors, new objects, undo, save`.

### Task 13: World Editor and game check (closes milestone 2)

Needs Sven's World Editor test map.

- [ ] **Step 1:** Copy its object files and `.wts` into `tests/fixtures/we-heavy/` (with `SOURCE.md`); the guard and
  `WtsFile` tests now cover it; check every mod against the skin rule and record in the spec what does not fit.
- [ ] **Step 2:** `dotnet test`; fix until PASS; commit `Fixtures: Sven's World Editor test map`.
- [ ] **Step 3:** With the app, on a copy: change a standard unit, add a custom unit, change an ability's three
  levels, a doodad variation, an upgrade level and a `TRIGSTR` name; save.
- [ ] **Step 4:** Hand over with the wc3-cs `test-map` skill: Sven opens the saved map in World Editor (each change
  shows, nothing else changed) and plays it. Record the result in `docs/checks.md` with the game version.
- [ ] **Step 5:** Remove the "small maps" warning; commit `Saving checked in World Editor and the game`.
  **Milestone 2 done.**
