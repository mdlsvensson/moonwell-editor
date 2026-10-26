# Object Editor, milestone 3 (the table: bulk edit) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Table view where the user picks a kind's objects (rows) and fields (columns), edits cells, and changes many
cells at once (a value or an expression, paste from a spreadsheet, fill down, reset), each bulk change one step of undo.

**Architecture:** Two new model calls do the work on the C# side: `Session.Table` builds the rows' cells in one call,
and `Session.SetCells` applies a list of cell changes as one all-or-nothing edit. Three new bridge commands
(`fields`, `table`, `setCells`) expose them. The page keeps the pure logic (expressions, tab-separated text,
selection, paste and fill mapping) in tested `.ts` modules and the view in two components.

**Tech Stack:** as milestones 1-2: .NET 10, War3Net `6.1.2-moonwell.4`, xUnit 2.9.3; Svelte 5, TypeScript, Tailwind 4,
Vitest, Biome.

**Spec:** `docs/superpowers/specs/2026-10-26-object-editor-design.md`, section "The table (milestone 3)" (all of it
the agent's calls). Mockup: `docs/mockups/editor.html` (Table view).

## Global Constraints

- No agent attribution anywhere; never create or edit a `README.md`; nothing pushed; `C:\Users\mdlsvensson\Repo\wc3-cs`
  read only.
- C#: .NET 10, nullable, warnings as errors, implicit usings, file-scoped namespaces, `///` summaries on public
  members, `.editorconfig` (4 spaces, LF). Front end: exact versions, Biome (2 spaces, 100 columns), no pixel fonts,
  Vitesse Dark tokens from `web/src/app.css`.
- A field the user did not touch is never changed; a bulk change is all or nothing and one step of undo.
- Tests needing game data read `OBJECT_EDITOR_GAME_DATA` (here `C:\Users\mdlsvensson\Downloads\Work\war3.w3mod`) and
  print why and return when it is unset; fixtures are copied to temp folders, never edited in place.
- Work on a branch `table-bulk-edit` from `main`.

## Review Focus

1. A bulk change that touches a read-only object, a field that does not apply, or a value the field cannot hold
   changes nothing at all, and the message names the first bad cell. Test: Task 1 `Set_cells_is_all_or_nothing`.
2. An expression on an empty or non-numeric cell (a text field, a choice) is refused before anything is sent. Test:
   Task 4 `expressions refuse text and choice fields`.
3. Pasting more rows or columns than are shown stops at the table's edge rather than wrapping or failing. Test:
   Task 4 `paste stops at the table's edge`.
4. Undo after a bulk change restores every cell in one step, and a TRIGSTR name in the block keeps the shared-string
   rule. Test: Task 1 `Set_cells_undoes_in_one_step`, `Set_cells_keeps_the_shared_string_rule`.
5. The table stays responsive with every standard unit shown (about 1000 rows) and 10 columns. Test: Task 2
   `Table_of_every_unit_is_fast`.

---

## File Structure

```
src/ObjectEditor.Model/Table.cs          Session.Table, Session.SetCells, Column, CellEdit, TableRow, TableCell
src/ObjectEditor.Model/Edits.cs          (modify) Set/Reset split into checks and steps that SetCells reuses
src/ObjectEditor.App/Commands.cs         (modify) fields, table, setCells
web/src/lib/api.ts                       (modify) the three commands and their types
web/src/lib/expression.ts                bulk-value expressions
web/src/lib/tsv.ts                       tab-separated text for copy and paste
web/src/lib/grid.ts                      selection, sorting, paste and fill mapping, default columns
web/src/lib/TableView.svelte             toolbar (rows, columns, bulk bar) and the view's keys
web/src/lib/TableGrid.svelte             the virtualised grid: header, rows, cells, selection
web/src/lib/ColumnPicker.svelte          the field picker
web/src/App.svelte                       (modify) the Table button and view switch
tests/ObjectEditor.Model.Tests/TableTests.cs
tests/ObjectEditor.App.Tests/CommandTests.cs (modify)
web/src/lib/{expression,tsv,grid}.test.ts
```

---

### Task 1: One edit for many cells (`Session.SetCells`)

**Files:** Create `src/ObjectEditor.Model/Table.cs` (the `SetCells` part), `tests/ObjectEditor.Model.Tests/TableTests.cs`;
modify `src/ObjectEditor.Model/Edits.cs`.

**Interfaces:**
- Produces:
  ```csharp
  /// <param name="Value">The new value as Set takes it; null resets the cell to its standard value.</param>
  public sealed record CellEdit(string Id, string Field, int Level, string? Value);
  public int SetCells(ObjectKind kind, IReadOnlyList<CellEdit> edits);   // on Session; returns the number of cells changed
  ```
  Every cell is checked first (object, field applies, level in range, not read only, value parses and fits the file:
  the same checks and `EditException` codes as `Set`/`Reset`); the first failure throws with the cell named
  (`"h001 Hit Points (uhpm): ..."`) and nothing changes. Then all cells are applied into **one** `Edit` (so one undo
  step, one `Commit`, the search index updated for every touched object). Each cell follows `Set`'s rules exactly
  (equal to base removes the mod unless modes differ; TRIGSTR entries edited in `war3map.wts` unless shared, then
  inline). Refactor `Set` and `Reset` into a check part and an apply-into-`Edit` part that `SetCells` calls, so the
  rules live in one place; `Set`/`Reset` keep their behaviour (their tests must pass unchanged).
- [ ] **Step 1:** Tests (temp copy of `moonwell-mymap`; game data skip): `Set_cells_changes_every_cell` (three units'
  `uhpm` and one ability's `acdn` levels 1-3 in one call; values read back); `Set_cells_undoes_in_one_step` (one
  `Undo()` restores all, files byte-equal to before; `Redo()` again); `Set_cells_is_all_or_nothing` (a list whose
  third cell is `notNumber`, one whose second is a read-only object (use `Lossy.Open`), one whose cell's field does not
  apply: each throws with the cell named, and the files and `Dirty` are unchanged); `Set_cells_resets_with_null`;
  `Set_cells_keeps_the_shared_string_rule` (two cells whose names share a `TRIGSTR` entry are written inline);
  `Set_cells_with_no_edits_changes_nothing` (empty list: 0, no undo step).
- [ ] **Step 2:** Run `dotnet test tests/ObjectEditor.Model.Tests`; expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** All Model tests PASS (the existing `EditTests` unchanged).
- [ ] **Step 5:** Commit `Model: many cells in one edit`.

### Task 2: The table's cells (`Session.Table`)

**Files:** Modify `src/ObjectEditor.Model/Table.cs`; add tests to `TableTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public sealed record Column(string Field, int Level);              // Level 0 for a field without levels
  public sealed record TableCell(string Value, string Base, bool Changed, bool Applies, string? Warning);
  public sealed record TableRow(string Id, string Name, string? BaseId, string? ReadOnly, IReadOnlyList<TableCell> Cells);
  public IReadOnlyList<TableRow> Table(ObjectKind kind, IReadOnlyList<string> ids, IReadOnlyList<Column> columns);
  ```
  Rows in the order of `ids`; an unknown id is left out. A cell whose field does not apply to the object, or whose
  level is above the object's level count, has `Applies` false and empty `Value`/`Base`. Values, `Changed` and
  `Warning` are exactly what `Object()` shows for that field and level (same `Resolver`). Unknown field ids throw
  `EditException("noField", ...)`.
- [ ] **Step 1:** Tests: `Table_matches_object_view` (for 5 units and 2 abilities and their columns, every cell equals
  the `FieldView` of `Object()`); `Table_marks_cells_that_do_not_apply` (`ustr` on `hfoo`; `acdn` level 4 on a
  3-level ability); `Table_keeps_the_order_of_ids_and_skips_unknown`; `Table_of_every_unit_is_fast` (every unit id
  of the game data, 10 columns: under 300 ms in Release, under 1500 ms in Debug, after one warm-up call).
- [ ] **Step 2:** FAIL. **Step 3:** Implement (one `Resolver` per call; resolve only the asked fields). **Step 4:** PASS.
- [ ] **Step 5:** Commit `Model: the table's cells`.

### Task 3: Commands and the web API

**Files:** Modify `src/ObjectEditor.App/Commands.cs`, `tests/ObjectEditor.App.Tests/CommandTests.cs`,
`web/src/lib/api.ts`, `web/src/lib/api.test.ts`.

**Interfaces:**
- Produces commands (camelCase JSON, `kind` as `ObjectKinds.Key`):
  `fields({ kind })` → `FieldInfo[]` as `{ id, label, category, sort, type, perLevel, choice, listOf }` (the kind's
  fields from the game data, in category then sort order); `table({ kind, ids, columns: [{ field, level }] })` →
  `TableRow[]`; `setCells({ kind, edits: [{ id, field, level, value }] })` → `{ changed, view? }` where `value` may be
  null, and a `sessionChanged` event follows as for every edit. Errors use the existing codes. `api.ts`: `fields`,
  `table`, `setCells` wrappers and the `FieldInfo`, `Column`, `TableCell`, `TableRow`, `CellEdit` types.
- [ ] **Step 1:** Tests: `CommandTests.Table_flow` (open a fixture copy; `fields` for units contains `uhpm` with its
  label; `table` of two units × two columns; `setCells` of both cells, one undo restores both, events sent);
  `setCells` with a bad cell gives its code and no event; `api.test.ts` checks the three wrappers' names and argument
  names.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS (`dotnet test`, `npm test`, `npm run check`).
- [ ] **Step 5:** Commit `App: table commands`.

### Task 4: The table's logic (pure TypeScript)

**Files:** Create `web/src/lib/expression.ts`, `tsv.ts`, `grid.ts` and their `.test.ts`.

**Interfaces:**
- Produces:
  ```ts
  // expression.ts
  export type Expression = { op: "set" | "add" | "sub" | "mul" | "div" | "percent"; operand: string };
  export function parseExpression(text: string): Expression | { error: string };
  export function applyExpression(e: Expression, current: string, type: FieldInfo["type"]): string | { error: string };
  // tsv.ts
  export function parseTsv(text: string): string[][];          // rows of cells; "\r\n" or "\n"; quoted cells as spreadsheets write them
  export function formatTsv(rows: string[][]): string;
  // grid.ts
  export type Cell = { row: number; col: number };
  export type Selection = { anchor: Cell; focus: Cell };
  export function cellsOf(s: Selection): Cell[];               // the block, row by row
  export function sortRows(rows: TableRow[], col: number, descending: boolean): TableRow[];   // numbers as numbers, empty last
  export function pasteEdits(start: Cell, block: string[][], rows: TableRow[], columns: Column[]): CellEdit[];
  export function fillDownEdits(s: Selection, rows: TableRow[], columns: Column[]): CellEdit[];
  export function resetEdits(s: Selection, rows: TableRow[], columns: Column[]): CellEdit[];
  export function bulkEdits(s: Selection, rows: TableRow[], columns: Column[], fields: FieldInfo[], text: string): CellEdit[] | { error: string };
  export const defaultColumns: Record<Kind, Column[]>;
  ```
  Expressions (spec): `50`, `=50`, `+5`, `-5`, `+10%`, `-10%`, `*1.1`, `x1.1`, `×1.1`, `/2`; numbers in invariant form
  (`.` decimal). `int` fields round to the nearest whole number, halves away from zero; `float` results are rounded
  to 4 decimal places with trailing zeros dropped (`1.35 * 1.1` is `1.4850000000000001` in floating point → `1.485`). `bool`, `string`, `choice`, `objectList`, `objectRef` take only `set`.
  A cell with `Applies` false is skipped by paste, fill and reset, and makes a bulk expression refuse. Default columns:
  `unit` `ugol`, `ulum`, `uhpm`, `ua1b`, `udef`, `ua1c`; `item` `igol`, `ilum`, `ilev`; `ability` `amcs`, `acdn`,
  `aran` at level 1; `destructable` `bhps`; others none (a default whose field the game data lacks is dropped when
  the columns load).
- [ ] **Step 1:** Tests: `expression.test.ts` — every form above parses; `+10%` of `135` is `149` for an int (148.5
  rounds away from zero), of `1.35` is `1.485` for a float; `/0` is an error; `expressions refuse text and choice
  fields`; `tsv.test.ts` — round trip of a block with an empty cell, a cell with a tab or newline in quotes, a
  trailing newline from a spreadsheet; `grid.test.ts` — block selection both directions; `sortRows` numbers, text,
  empty last; `paste stops at the table's edge`; paste skips cells that do not apply; fill down copies the top cell
  of each column; reset gives `value: null`; `bulkEdits` with `+5` over a block.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `Table: expressions, tab-separated text, selection`.

### Task 5: The Table view

**Files:** Create `web/src/lib/TableView.svelte`, `TableGrid.svelte`, `ColumnPicker.svelte`; modify `App.svelte`.

**Interfaces:**
- Consumes: Tasks 3 and 4; the existing `FieldEditor` (for a cell being edited), `shownValue` from `fields.ts`, the
  kind buttons and tree entries `App.svelte` already holds.
- [ ] **Step 1:** Implement as the spec's section and the mockup: the Table button enabled (Details, Table; Compare
  stays disabled); row chips (Custom, Changed, the tree's top groups) and the tree's filter text; default rows (custom
  objects, else the selected object's group); the virtualised grid with a sticky header (the label, the field id in
  muted mono, `· n` for a level), sorting by a header click; cell colours (changed yellow, warning red with the text
  as a tooltip, grey when not applying or read only); the column picker (search, per-level: level 1 or all levels);
  remove a column from its header; columns per kind kept in `localStorage` (`objectEditor.columns.<kind>`; a read or
  write that throws is ignored); a row's name opens the object in Details. After any edit the shown rows are fetched
  again (`table`), keeping scroll and selection.
- [ ] **Step 2:** Run `npm test`, `npm run check`, `npm run build`; PASS.
- [ ] **Step 3:** Commit `Table: the view, columns and rows`.

### Task 6: Editing in the table

**Files:** Modify `TableView.svelte`, `TableGrid.svelte`, `App.svelte`.

- [ ] **Step 1:** Implement: click selects a cell, Shift+click / Shift+arrows a block, arrows move, Ctrl A all; Enter,
  F2 or typing edits the focused cell with `FieldEditor` (Enter commits and moves down, Esc cancels, Tab moves right);
  the bulk bar ("Set the selected cells to …" with the expression box and Apply; its error shown beside it);
  Ctrl C copies the selection (`formatTsv` of the data values) through `navigator.clipboard.writeText`; Ctrl V reads
  `navigator.clipboard.readText` and pastes from the focused cell; Ctrl D fills down; Delete resets — each a
  `setCells` call, its error shown in the status bar; in the table these keys act on cells and never reach the
  details view's Ctrl D / Delete; Ctrl Z / Ctrl Y as everywhere. Typed text goes through the existing `typing`
  registry so Open map…, reload and close still ask.
- [ ] **Step 2:** `npm test`, `npm run check`, `npm run build`, `dotnet test` (with game data); PASS.
- [ ] **Step 3:** Commit `Table: editing, bulk changes, paste, fill and reset`.
- [ ] **Step 4 (controller):** in the real window on a copy of `tests/fixtures/we-heavy`: show the Human units,
  add a column, set gold of five units with `+10%`, paste a 3×2 block from a spreadsheet, fill down, reset, undo each
  in one step, save; check with the dump that only the changed fields moved. Screenshots to Sven.
