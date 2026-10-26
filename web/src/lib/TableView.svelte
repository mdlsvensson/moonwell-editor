<script lang="ts">
/**
 * The Table view: one kind's objects as rows and chosen fields as columns. The bar picks the kind (as the tree's kind
 * buttons do), filters the rows by the tree's filter text and the row chips (Custom, the tree's top groups, Changed),
 * and the grid shows the cells. The rows are fetched again (`table`) after every change of the map (an edit, undo,
 * redo or reload sends `sessionChanged`) and whenever the kind, the filter, the chips or the columns change; the grid
 * keeps its scroll position and the selection.
 *
 * Every change of cells is one `setCells` (`onedit`), one step of undo: a cell's editor, the bar that sets the
 * selected cells to a value or an expression, and the grid's Ctrl V (paste from the clipboard at the block's upper
 * left cell), Ctrl D (fill down) and Delete (reset). A refusal changes nothing and is said in the status bar (the bar's
 * also beside it). Ctrl C copies the selected values as tab-separated text. The bulk changes (and copies) run one at a
 * time, in the order asked, and each first waits for the edits on their way and the rows fetched after them, so it
 * works from the values as they are (Enter twice on `+10%` adds 10% twice; a paste then Ctrl D fills down the pasted
 * value); the selection is kept by objects and columns, so it stays on them when the rows are sorted or fetched again.
 */
import { untrack } from "svelte";
import {
  type CellEdit,
  type ChoiceList,
  type Column,
  type FieldInfo,
  fields,
  type Kind,
  type SessionState,
  type TableRow,
  type TreeEntry,
  table,
} from "./api.ts";
import ColumnPicker from "./ColumnPicker.svelte";
import { type Lookup, listKinds } from "./fields.ts";
import {
  blockSize,
  bulkEdits,
  copyBlock,
  fillDownEdits,
  pasteEdits,
  relocateSelection,
  resetEdits,
  type Selection,
  type SelectionRef,
  sameSelection,
  selectionRef,
  topLeft,
} from "./grid.ts";
import TableGrid, { type BulkAction } from "./TableGrid.svelte";
import {
  addColumns,
  columnKey,
  defaultChips,
  groupChips,
  levelColumnLimit,
  levelFields,
  mostLevels,
  nextSort,
  oneAtATime,
  readColumns,
  removeColumn,
  rowIds,
  rowsReadiness,
  type Store,
  sortTable,
  type TableState,
  toggleGroup,
  writeColumns,
} from "./table.ts";
import { formatTsv, parseTsv } from "./tsv.ts";

/** What a change of cells answers: how many changed, or why it was refused (nothing changed). */
export type EditResult = { changed: number } | { error: string };

type Props = {
  kind: Kind;
  /** The kind's tree; null while it loads. */
  entries: TreeEntry[] | null;
  /** Why the tree could not be loaded; "" when it could. */
  treeError: string;
  /** The object last picked in the kind: the rows start on its group when the map has no custom objects. */
  selected: string | null;
  /** The tree's filter text, shared with the tree. */
  filter: string;
  /** The chips, sort and columns per kind, kept while the map is open. */
  tableState: TableState;
  /** Where the map stands; every new state fetches the rows again. */
  session: SessionState | null;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  /** Asks for a kind's tree, for the names of the objects a list column holds. */
  onneed: (kind: Kind) => void;
  /** Opens an object of the kind in the details view. */
  onopen: (kind: Kind, id: string) => void;
  /** Changes cells of a kind as one edit (`setCells`), after the edits asked before it. */
  onedit: (kind: Kind, edits: CellEdit[]) => Promise<EditResult>;
  /** Says something in the status bar (`warn` for a refusal). */
  onnotice: (text: string, warn?: boolean) => void;
};

let {
  kind,
  entries,
  treeError,
  selected,
  filter = $bindable(),
  tableState = $bindable(),
  session,
  choices,
  lookup,
  onneed,
  onopen,
  onedit,
  onnotice,
}: Props = $props();

/** The rows shown, with the kind and columns they were fetched for. */
type Shown = { kind: Kind; columns: Column[]; rows: TableRow[] };

let fieldLists = $state.raw<Partial<Record<Kind, FieldInfo[]>>>({});
let fieldsError = $state("");
const fieldsAsked = new Set<Kind>();
let shown = $state.raw<Shown | null>(null);
let rowsError = $state("");
let loading = $state(false);
let asked = 0;
let picker = $state<HTMLElement | null>(null);
let pickerError = $state("");
// What the picker says after "All levels" stopped at the most it adds.
let pickerNote = $state("");
let filterInput: HTMLInputElement | undefined = $state();
let selection = $state.raw<Selection | null>(null);
let bulkText = $state("");
let bulkError = $state("");
// The selection by objects and columns, to find again when the rows change.
let kept: SelectionRef | null = null;
// The edits from this view on their way; the map's state an edit changed, until the next state comes; and the state
// the rows shown were fetched for.
let editsOut = $state(0);
let waitingFor = $state.raw<{ session: SessionState | null } | null>(null);
let rowsFor = $state.raw<{ session: SessionState | null } | null>(null);
// How long a bulk change waits for the rows before it gives up.
const waitLimit = 10000;

const store = (): Store | null => globalThis.localStorage ?? null;

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

const kindFields = $derived(fieldLists[kind] ?? null);
const chips = $derived(tableState.chips[kind] ?? null);
const columns = $derived(tableState.columns[kind] ?? null);
const groups = $derived(entries ? groupChips(entries) : []);
const ids = $derived(entries && chips ? rowIds(entries, chips, filter) : null);
// The fetch below runs again only when these texts change, not each time the tree is fetched again unchanged.
const idsKey = $derived(ids ? ids.join("\n") : null);
const columnsKey = $derived(columns ? columns.map(columnKey).join(",") : null);
const sort = $derived(tableState.sort[kind] ?? null);
const rows = $derived(shown ? sortTable(shown.rows, sort, shown.columns) : []);
const suffixes = $derived(
  new Map((entries ?? []).filter((entry) => entry.suffix).map((entry) => [entry.id, entry.suffix])),
);

// Another kind starts with nothing selected, and without the messages of the last one.
$effect(() => {
  kind;
  untrack(() => {
    selection = null;
    fieldsError = "";
    pickerError = "";
    bulkError = "";
  });
});

// A selection made (by the grid or here) is kept by its objects and columns.
$effect(() => {
  const s = selection;
  untrack(() => {
    kept = s && shown ? selectionRef(s, rows, shown.columns) : null;
  });
});

// When the rows or columns change (a fetch after an edit, undo or redo, a sort, a column added or removed), the
// selection moves with its objects and columns; one that is gone keeps its place, pulled inside the table.
$effect(() => {
  const list = rows;
  const cols = shown?.columns ?? [];
  untrack(() => {
    const next = relocateSelection(kept, list, cols);
    if (!sameSelection(next, selection)) selection = next;
  });
});

// The bar's error was about the cells selected then.
$effect(() => {
  selection;
  untrack(() => {
    bulkError = "";
  });
});

// The kind's fields, once; then its columns (also for a map opened since), from this computer's storage or the
// defaults.
$effect(() => {
  const each = kind;
  const list = fieldLists[each];
  const missing = !tableState.columns[each];
  untrack(() => {
    if (!list) loadFields(each);
    else if (missing) tableState.columns[each] = readColumns(each, list, store);
  });
});

async function loadFields(each: Kind): Promise<void> {
  if (fieldsAsked.has(each)) return;
  fieldsAsked.add(each);
  try {
    const list = await fields(each);
    fieldLists = { ...fieldLists, [each]: list };
    fieldsError = "";
  } catch (error) {
    fieldsAsked.delete(each);
    if (each === kind) fieldsError = message(error);
  }
}

// The chips a kind starts with, once its tree is there.
$effect(() => {
  const each = kind;
  const list = entries;
  if (!list) return;
  untrack(() => {
    if (!tableState.chips[each]) tableState.chips[each] = defaultChips(list, selected);
  });
});

// The rows, fetched again on any change of the map (each sends a new session state) and when the kind, the rows or
// the columns change.
$effect(() => {
  const each = kind;
  const rowsKey = idsKey;
  const colsKey = columnsKey;
  session;
  if (rowsKey === null || colsKey === null) return;
  untrack(() => fetchRows(each, ids ?? [], $state.snapshot(columns ?? [])));
});

async function fetchRows(each: Kind, rowIdList: string[], cols: Column[]): Promise<void> {
  const mine = ++asked;
  const forSession = session;
  loading = true;
  try {
    const found = await table(each, rowIdList, cols);
    if (mine !== asked) return;
    shown = { kind: each, columns: cols, rows: found };
    rowsFor = { session: forSession };
    rowsError = "";
  } catch (error) {
    if (mine === asked) rowsError = message(error);
  } finally {
    if (mine === asked) loading = false;
  }
}

// The trees of the kinds the list columns hold, so their ids show with names.
$effect(() => {
  const list = kindFields;
  const cols = columns;
  if (!list || !cols) return;
  const needed = new Set<Kind>();
  for (const column of cols) {
    const field = list.find((each) => each.id === column.field);
    for (const each of listKinds(field?.listOf ?? null)) needed.add(each);
  }
  untrack(() => {
    for (const each of needed) onneed(each);
  });
});

function setColumns(each: Kind, next: Column[]): void {
  tableState.columns[each] = next;
  writeColumns(each, next, store);
}

/** The most levels any shown row has, from the kind's level count; 1 for a kind without levels or no rows. */
async function levelsShown(each: Kind): Promise<number> {
  const field = levelFields[each];
  const rowIdList = ids ?? [];
  if (!field || rowIdList.length === 0 || !fieldLists[each]?.some((info) => info.id === field)) {
    return 1;
  }
  return mostLevels(await table(each, rowIdList, [{ field, level: 0 }]));
}

async function add(field: FieldInfo, all: boolean): Promise<void> {
  const each = kind;
  pickerError = "";
  pickerNote = "";
  let levels = 1;
  if (field.perLevel && all) {
    try {
      levels = await levelsShown(each);
    } catch (error) {
      pickerError = message(error);
      return;
    }
  }
  if (levels > levelColumnLimit) {
    pickerNote =
      `"All levels" adds at most ${levelColumnLimit} columns: ${field.label} has levels 1 to ` +
      `${levelColumnLimit} of the ${levels} the rows have.`;
  }
  setColumns(each, addColumns(tableState.columns[each] ?? [], field, levels));
}

function remove(key: string): void {
  setColumns(kind, removeColumn(tableState.columns[kind] ?? [], key));
  if (tableState.sort[kind]?.key === key) tableState.sort[kind] = null;
}

function sortBy(key: string): void {
  tableState.sort[kind] = nextSort(tableState.sort[kind] ?? null, key);
}

function toggle(group: string): void {
  if (chips) tableState.chips[kind] = toggleGroup(chips, group);
}

function toggleChanged(): void {
  if (chips) tableState.chips[kind] = { ...chips, changed: !chips.changed };
}

/** Puts the cursor in the filter (Ctrl F). */
export function focusFilter(): void {
  filterInput?.focus();
  filterInput?.select();
}

function clearOnEscape(event: KeyboardEvent): void {
  if (event.key === "Escape" && filter) {
    event.stopPropagation();
    filter = "";
  }
}

function cellCount(count: number): string {
  return `${count} ${count === 1 ? "cell" : "cells"}`;
}

const selectedText = $derived.by(() => {
  if (!selection) return "No cells selected";
  const size = blockSize(selection);
  return `${cellCount(size.rows * size.cols)} selected`;
});

// Bulk changes waiting for the rows to be as the map is.
const waiters = new Set<(problem: string | null) => void>();

const readiness = $derived(rowsReadiness({ editsOut, session, waitingFor, rowsFor, rowsError }));

$effect(() => {
  const state = readiness;
  if (state === "wait") return;
  untrack(() => {
    for (const done of [...waiters]) done(state === "ready" ? null : state.error);
  });
});

/**
 * Waits until no edit from this view is on its way and the rows were fetched for the map as it is, so a bulk change
 * works from the values as they are. Answers null then, or why it cannot: the fetch failed, or it took too long.
 */
function settled(): Promise<string | null> {
  const now = readiness;
  if (now === "ready") return Promise.resolve(null);
  if (now !== "wait") return Promise.resolve(now.error);
  return new Promise((resolve) => {
    const done = (problem: string | null) => {
      clearTimeout(timer);
      waiters.delete(done);
      resolve(problem);
    };
    const timer = setTimeout(() => done("Wait for the last change to finish."), waitLimit);
    waiters.add(done);
  });
}

/**
 * Waits as `settled` does, and again while an edit started in the meantime (a cell's editor sent one), so a bulk
 * change works from the values that edit left. Answers null when ready, or why not. The bulk changes themselves run
 * one at a time (`bulkQueue`), so two of them never wait here together.
 */
async function ready(): Promise<string | null> {
  for (;;) {
    const problem = await settled();
    if (problem !== null || readiness === "ready") return problem;
  }
}

/**
 * Changes cells as one edit (`onedit`), counted while on its way. An edit that changed cells leaves the rows stale
 * until the map's next state comes (if it has not come yet) and the rows are fetched for it.
 */
async function edit(each: Kind, edits: CellEdit[]): Promise<EditResult> {
  const before = session;
  editsOut++;
  try {
    const result = await onedit(each, edits);
    if (!("error" in result) && result.changed > 0 && session === before) {
      waitingFor = { session: before };
    }
    return result;
  } finally {
    editsOut--;
  }
}

/** The fields of the kind the rows were fetched for. */
function shownFields(): FieldInfo[] {
  return shown ? (fieldLists[shown.kind] ?? []) : [];
}

/**
 * Changes cells as one edit and says what it did in the status bar (`done` names it: "Pasted"); a refusal changed
 * nothing and says why. Answers with the refusal, or null.
 */
async function change(each: Kind, edits: CellEdit[], done: string): Promise<string | null> {
  const result = await edit(each, edits);
  if ("error" in result) {
    onnotice(result.error, true);
    return result.error;
  }
  onnotice(
    result.changed === 0
      ? "Nothing changed: the cells already hold those values"
      : `${done} ${cellCount(result.changed)} · Ctrl Z undoes it`,
  );
  return null;
}

/** Sets one cell from its editor; the editor shows a refusal itself. */
async function setCell(row: TableRow, column: Column, value: string): Promise<string | null> {
  const each = shown?.kind;
  if (!each) return "The table is not shown.";
  const result = await edit(each, [
    { id: row.id, field: column.field, level: column.level, value },
  ]);
  return "error" in result ? result.error : null;
}

async function copy(s: Selection): Promise<void> {
  const block = copyBlock(s, rows);
  try {
    await navigator.clipboard.writeText(formatTsv(block));
  } catch {
    onnotice("Copy needs clipboard access", true);
    return;
  }
  onnotice(`Copied ${cellCount(block.length * (block[0]?.length ?? 0))}`);
}

/** Pastes the clipboard's text (read before the wait for the rows) from the block's upper left cell. */
async function paste(s: Selection, text: string): Promise<void> {
  const at = shown;
  if (!at) return;
  const block = parseTsv(text);
  if (block.length === 0) {
    onnotice("The clipboard holds no text to paste.");
    return;
  }
  const edits = pasteEdits(topLeft(s), block, rows, at.columns, shownFields(), choices);
  if ("error" in edits) {
    onnotice(edits.error, true);
    return;
  }
  if (edits.length === 0) {
    onnotice("None of the cells the paste covers can be changed.");
    return;
  }
  await change(at.kind, edits, "Pasted");
}

// The bulk changes (the grid's and the bar's), one at a time in the order asked: each works from the rows as the one
// before left them.
const bulkQueue = oneAtATime();

function bulk(action: BulkAction): Promise<void> {
  return bulkQueue(() => bulkNow(action));
}

async function bulkNow(action: BulkAction): Promise<void> {
  // The clipboard is read first: the edit must follow the wait for the rows at once, with nothing to wait for between.
  let text = "";
  if (action === "paste") {
    try {
      text = await navigator.clipboard.readText();
    } catch {
      onnotice("Paste needs clipboard access", true);
      return;
    }
  }
  const problem = await ready();
  if (problem) {
    onnotice(problem, true);
    return;
  }
  const s = selection;
  const at = shown;
  if (!s || !at) return;
  if (action === "copy") {
    await copy(s);
    return;
  }
  if (action === "paste") {
    await paste(s, text);
    return;
  }
  if (action === "fill" && blockSize(s).rows < 2) {
    onnotice("Ctrl D fills the top cell of each selected column down: select more than one row.");
    return;
  }
  const edits =
    action === "fill"
      ? fillDownEdits(s, rows, at.columns, shownFields())
      : resetEdits(s, rows, at.columns, shownFields());
  if ("error" in edits) {
    onnotice(edits.error, true);
    return;
  }
  if (edits.length === 0) {
    onnotice(`None of the selected cells can be ${action === "fill" ? "filled" : "reset"}.`);
    return;
  }
  await change(at.kind, edits, action === "fill" ? "Filled" : "Reset");
}

/** The bar's Apply: the selected cells set to the value or expression typed, after the bulk changes asked before. */
function apply(): Promise<void> {
  const text = bulkText;
  return bulkQueue(() => applyNow(text));
}

async function applyNow(text: string): Promise<void> {
  const problem = await ready();
  if (problem) {
    bulkError = problem;
    return;
  }
  const s = selection;
  const at = shown;
  if (!s || !at) {
    bulkError = "Select the cells to set first.";
    return;
  }
  const edits = bulkEdits(s, rows, at.columns, shownFields(), text, choices);
  if ("error" in edits) {
    bulkError = edits.error;
    return;
  }
  bulkError = (await change(at.kind, edits, "Set")) ?? "";
}

function bulkKeys(event: KeyboardEvent): void {
  if (event.key === "Enter") {
    event.preventDefault();
    apply();
  } else if (event.key === "Escape" && (bulkText || bulkError)) {
    event.stopPropagation();
    bulkText = "";
    bulkError = "";
  }
}

const chip = "rounded-full border bg-raised px-2.5 py-0.5 text-[12.5px] whitespace-nowrap";
</script>

<div class="flex h-full min-h-0 flex-col">
  <div class="flex flex-col gap-2 border-b border-line px-[18px] pt-3 pb-2.5">
    <div class="flex flex-wrap items-center gap-2">
      <input
        class="w-60 rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
        placeholder="Filter the rows (name or id; Ctrl F)…"
        aria-label="Filter the rows"
        bind:this={filterInput}
        bind:value={filter}
        onkeydown={clearOnEscape}
      />
      <span class="text-[12.5px] text-muted" data-testid="table-count">
        {#if shown}
          {rows.length}
          {rows.length === 1 ? "object" : "objects"}
        {/if}
        {#if loading}· loading…{/if}
      </span>
    </div>
    <div class="flex flex-wrap items-center gap-1.5" role="group" aria-label="Rows">
      {#each groups as group (group.key)}
        {@const on = chips?.groups.includes(group.key) ?? false}
        <button
          type="button"
          class="{chip} {on ? 'border-accent text-fg' : 'border-line text-muted hover:text-body'}"
          aria-pressed={on}
          onclick={() => toggle(group.key)}
        >
          {group.name}
          <span class="ml-0.5 font-mono text-[11px] text-faint">{group.count}</span>
        </button>
      {/each}
      <span class="mx-1 h-4 w-px bg-line"></span>
      <button
        type="button"
        class="{chip} {chips?.changed
          ? 'border-changed text-changed'
          : 'border-line text-muted hover:text-body'}"
        aria-pressed={chips?.changed ?? false}
        title="Only the objects the map changes"
        onclick={toggleChanged}
      >
        Changed
      </button>
      {#if chips && chips.groups.length === 0}
        <span class="text-[12.5px] text-faint">every group</span>
      {/if}
    </div>
    <div class="flex flex-wrap items-center gap-2 text-[12.5px]" role="group" aria-label="Bulk change">
      <span class="text-muted" data-testid="table-selection">{selectedText} ·</span>
      <label class="flex items-center gap-2 text-muted">
        Set the selected cells to
        <input
          class="w-36 rounded-[5px] border bg-bg px-2 py-0.5 font-mono text-fg outline-none focus:border-accent {bulkError
            ? 'border-warn'
            : 'border-line'}"
          placeholder="50, +5, -10%, *1.1"
          spellcheck="false"
          aria-invalid={!!bulkError}
          bind:value={bulkText}
          onkeydown={bulkKeys}
          oninput={() => (bulkError = "")}
        />
      </label>
      <button
        type="button"
        class="rounded-[5px] border border-line bg-raised px-2.5 py-0.5 text-fg hover:border-accent disabled:opacity-40 disabled:hover:border-line"
        disabled={!selection || bulkText.trim() === ""}
        onclick={apply}
      >
        Apply
      </button>
      {#if bulkError}
        <span class="text-warn" role="alert">{bulkError}</span>
      {:else}
        <span class="text-faint">
          (a value, +5, -10%, *1.1 or /2; Ctrl V pastes from a spreadsheet, Ctrl D fills down, Delete
          resets)
        </span>
      {/if}
    </div>
    {#if treeError || fieldsError || rowsError || pickerError}
      <p class="m-0 text-[12.5px] text-warn" role="alert">
        {treeError || fieldsError || rowsError || pickerError}
      </p>
    {/if}
  </div>

  {#if !entries || !kindFields || !shown}
    {#if !treeError && !fieldsError && !rowsError}
      <p class="mx-[18px] mt-3.5 text-muted">Loading the table…</p>
    {/if}
  {:else}
    <TableGrid
      bind:selection
      {rows}
      columns={shown.columns}
      fields={fieldLists[shown.kind] ?? []}
      {choices}
      {lookup}
      {sort}
      {suffixes}
      onsort={sortBy}
      onremove={remove}
      onadd={(anchor) => {
        pickerError = "";
        pickerNote = "";
        picker = anchor;
      }}
      onopen={(id) => shown && onopen(shown.kind, id)}
      onbulk={bulk}
      onset={setCell}
      {onnotice}
      onfollow={onopen}
    />
  {/if}
</div>

{#if picker && kindFields && columns}
  <ColumnPicker
    anchor={picker}
    fields={kindFields}
    {columns}
    note={pickerNote}
    onadd={add}
    onclose={() => (picker = null)}
  />
{/if}
