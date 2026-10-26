<script lang="ts">
/**
 * The Table view: one kind's objects as rows and chosen fields as columns. The bar picks the kind (as the tree's kind
 * buttons do), filters the rows by the tree's filter text and the row chips (Custom, the tree's top groups, Changed),
 * and the grid shows the cells. The rows are fetched again (`table`) after every change of the map (an edit, undo,
 * redo or reload sends `sessionChanged`) and whenever the kind, the filter, the chips or the columns change; the grid
 * keeps its scroll position.
 */
import { untrack } from "svelte";
import {
  type ChoiceList,
  type Column,
  type FieldInfo,
  fields,
  type Kind,
  kinds,
  type SessionState,
  type TableRow,
  type TreeEntry,
  table,
} from "./api.ts";
import ColumnPicker from "./ColumnPicker.svelte";
import { type Lookup, listKinds } from "./fields.ts";
import TableGrid from "./TableGrid.svelte";
import {
  addColumns,
  columnKey,
  defaultChips,
  groupChips,
  levelFields,
  mostLevels,
  nextSort,
  readColumns,
  removeColumn,
  rowIds,
  type Store,
  sortTable,
  type TableState,
  toggleGroup,
  writeColumns,
} from "./table.ts";
import { kindNames } from "./tree.ts";

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
  onkind: (kind: Kind) => void;
  /** Asks for a kind's tree, for the names of the objects a list column holds. */
  onneed: (kind: Kind) => void;
  /** Opens an object of the kind in the details view. */
  onopen: (kind: Kind, id: string) => void;
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
  onkind,
  onneed,
  onopen,
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
let filterInput: HTMLInputElement | undefined = $state();

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
  loading = true;
  try {
    const found = await table(each, rowIdList, cols);
    if (mine !== asked) return;
    shown = { kind: each, columns: cols, rows: found };
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
  let levels = 1;
  if (field.perLevel && all) {
    try {
      levels = await levelsShown(each);
    } catch (error) {
      pickerError = message(error);
      return;
    }
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

const chip = "rounded-full border bg-raised px-2.5 py-0.5 text-[12.5px] whitespace-nowrap";
</script>

<div class="flex h-full min-h-0 flex-col">
  <div class="flex flex-col gap-2 border-b border-line px-[18px] pt-3 pb-2.5">
    <div class="flex flex-wrap items-center gap-2">
      <div class="flex flex-wrap gap-1" role="group" aria-label="Kinds">
        {#each kinds as each (each)}
          <button
            type="button"
            class="rounded-[5px] border bg-raised px-2 py-0.5 text-[12.5px] {each === kind
              ? 'border-accent text-fg'
              : 'border-line text-muted hover:text-body'}"
            aria-pressed={each === kind}
            onclick={() => onkind(each)}
          >
            {kindNames[each]}
          </button>
        {/each}
      </div>
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
        picker = anchor;
      }}
      onopen={(id) => shown && onopen(shown.kind, id)}
    />
  {/if}
</div>

{#if picker && kindFields && columns}
  <ColumnPicker
    anchor={picker}
    fields={kindFields}
    {columns}
    onadd={add}
    onclose={() => (picker = null)}
  />
{/if}
