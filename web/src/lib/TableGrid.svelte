<script lang="ts">
/**
 * The table's grid: a header that stays in view (Name, Id, then each column's label with `· n` for a level and the
 * field id under it), and one row per object. A table can hold every object of a kind, so only the rows in view are
 * drawn: every row is the same height, so the row at any scroll position is a division away (as the tree does). The
 * name column stays in view when the grid scrolls sideways.
 *
 * Cells are selected as in a spreadsheet: a click, Shift+click or a drag, the arrows (Shift for a block, Ctrl to the
 * edge), Page Up/Down, Home/End and Ctrl A. Enter, F2, a double click or typing edits the focused cell with the details
 * view's editor (`FieldEditor`) over it: Enter sends the value and moves down, Tab right, Esc drops it. The keys go to
 * the grid's scroller, never to a cell (a row scrolled out of view is taken out of the page, as in the tree); copy,
 * paste, fill down and reset are passed on (`onbulk`) and never reach the window, where they mean something else.
 */
import ArrowDown from "@lucide/svelte/icons/arrow-down";
import ArrowUp from "@lucide/svelte/icons/arrow-up";
import Plus from "@lucide/svelte/icons/plus";
import X from "@lucide/svelte/icons/x";
import { tick } from "svelte";
import type { ChoiceList, Column, FieldInfo, Kind, TableCell, TableRow } from "./api.ts";
import FieldEditor from "./FieldEditor.svelte";
import { type Lookup, shownValue } from "./fields.ts";
import {
  type Cell,
  cellField,
  editRefusal,
  moveSelection,
  type Selection,
  selectAll,
} from "./grid.ts";
import { editorAction, type GridAction, gridAction } from "./gridKeys.ts";
import { columnKey, type Sort } from "./table.ts";
import { type Editor, editorFor } from "./values.ts";

/** The changes of the selection the grid passes on. */
export type BulkAction = "copy" | "paste" | "fill" | "reset";

type Props = {
  rows: TableRow[];
  columns: Column[];
  fields: FieldInfo[];
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  sort: Sort | null;
  /** World Editor's suffix of an object, such as `(Evil)`, by id. */
  suffixes: Map<string, string>;
  onsort: (key: string) => void;
  onremove: (key: string) => void;
  /** Opens the column picker under the element given. */
  onadd: (anchor: HTMLElement) => void;
  /** Opens an object in the details view. */
  onopen: (id: string) => void;
  /** The selected block; null when no cell is selected. */
  selection: Selection | null;
  /** Copies, pastes, fills down or resets the selection. */
  onbulk: (action: BulkAction) => void;
  /** Sets one cell from its editor; answers with why it was refused, or null when it was taken. */
  onset: (row: TableRow, column: Column, value: string) => Promise<string | null>;
  /** Says something in the status bar (`warn` for a refusal). */
  onnotice: (text: string, warn?: boolean) => void;
  /** Opens an object an object list cell holds, in the details view. */
  onfollow: (kind: Kind, id: string) => void;
};

let {
  rows,
  columns,
  fields,
  choices,
  lookup,
  sort,
  suffixes,
  onsort,
  onremove,
  onadd,
  onopen,
  selection = $bindable(),
  onbulk,
  onset,
  onnotice,
  onfollow,
}: Props = $props();

const headerHeight = 42;
const rowHeight = 28;
const overscan = 10;
const nameWidth = 220;
const idWidth = 64;
const columnWidth = 132;
const addWidth = 132;

let scroller: HTMLDivElement | undefined = $state();
let scrollTop = $state(0);
let height = $state(0);

const byId = $derived(new Map(fields.map((field) => [field.id, field])));
const width = $derived(nameWidth + idWidth + columns.length * columnWidth + addWidth);
const first = $derived(Math.max(0, Math.floor(scrollTop / rowHeight) - overscan));
const last = $derived(
  Math.min(rows.length, Math.ceil((scrollTop + height) / rowHeight) + overscan),
);
const visible = $derived(rows.slice(first, last).map((row, at) => ({ row, index: first + at })));

const nameOf = (listOf: string, id: string) => lookup(listOf, id)?.name;

// The selected block's edges, for drawing.
const block = $derived(
  selection
    ? {
        top: Math.min(selection.anchor.row, selection.focus.row),
        bottom: Math.max(selection.anchor.row, selection.focus.row),
        left: Math.min(selection.anchor.col, selection.focus.col),
        right: Math.max(selection.anchor.col, selection.focus.col),
      }
    : null,
);

function inBlock(row: number, col: number): boolean {
  return (
    block !== null &&
    row >= block.top &&
    row <= block.bottom &&
    col >= block.left &&
    col <= block.right
  );
}

function isFocus(row: number, col: number): boolean {
  return selection?.focus.row === row && selection.focus.col === col;
}

/**
 * The cell being edited, by its object and column (rows can move when they are fetched again), and the text typed
 * to start it (null starts from the value).
 */
type Editing = { id: string; key: string; text: string | null };

let editing = $state.raw<Editing | null>(null);
let editError = $state("");
let editor: { commit(): Promise<boolean>; cancel(): void } | undefined = $state();
let editBox: HTMLDivElement | undefined = $state();
// True while the mouse button is down after a click on a cell: moving over cells makes the block.
let dragging = false;

const edited = $derived.by(() => {
  if (!editing) return null;
  const { id, key } = editing;
  const index = rows.findIndex((row) => row.id === id);
  const col = columns.findIndex((column) => columnKey(column) === key);
  const row = rows[index];
  const column = columns[col];
  const cell = row?.cells[col];
  const info = column ? byId.get(column.field) : undefined;
  if (!row || !column || !cell || !info) return null;
  const field = cellField(info, column, cell);
  return { index, col, row, column, field, editor: editorFor(field, choices) };
});

// An edited row that is no longer shown (another kind, a filter) ends the edit.
$effect(() => {
  if (editing && !edited) editing = null;
});

const editorWidths: Record<Editor, number> = {
  number: columnWidth,
  toggle: columnWidth,
  select: 220,
  text: 260,
  flags: 260,
  ids: 280,
};

function labelOf(column: Column): string {
  const label = byId.get(column.field)?.label ?? column.field;
  return column.level > 0 ? `${label} · ${column.level}` : label;
}

function isNumber(column: Column): boolean {
  const type = byId.get(column.field)?.type;
  return type === "int" || type === "float";
}

function textOf(column: Column, cell: TableCell): string {
  const field = byId.get(column.field);
  if (!field || !cell.applies) return cell.value;
  return shownValue({ ...field, value: cell.value }, choices, nameOf);
}

/**
 * A cell's colours and tooltip: grey when it does not apply or the object is read only, red with the warning past
 * World Editor's limit, yellow when the map changes it (with the standard value as its tooltip).
 */
function lookOf(
  row: TableRow,
  column: Column,
  cell: TableCell | undefined,
): { bg: string; fg: string; title?: string } {
  if (!cell?.applies) return { bg: "bg-surface", fg: "", title: "Does not apply to this object" };
  if (row.readOnly) {
    return { bg: "bg-surface", fg: "text-muted", title: `Read only: ${row.readOnly}` };
  }
  if (cell.warning) return { bg: "bg-warn-lo", fg: "text-warn", title: cell.warning };
  if (cell.changed) {
    const base = textOf(column, { ...cell, value: cell.base });
    return { bg: "", fg: "text-changed", title: `Standard: ${base === "" ? "(empty)" : base}` };
  }
  return { bg: "", fg: "text-fg" };
}

function ariaSort(key: string): "ascending" | "descending" | "none" {
  if (sort?.key !== key) return "none";
  return sort.descending ? "descending" : "ascending";
}

/** How many rows a Page Up or Page Down moves: the rows in view, less one. */
function pageRows(): number {
  return Math.max(1, Math.floor((height - headerHeight) / rowHeight) - 1);
}

/** Scrolls a cell into view, below the header and right of the name column. */
function reveal(at: Cell): void {
  if (!scroller) return;
  const top = headerHeight + at.row * rowHeight;
  if (top < scroller.scrollTop + headerHeight) scroller.scrollTop = top - headerHeight;
  else if (top + rowHeight > scroller.scrollTop + scroller.clientHeight) {
    scroller.scrollTop = top + rowHeight - scroller.clientHeight;
  }
  const left = nameWidth + idWidth + at.col * columnWidth;
  if (left < scroller.scrollLeft + nameWidth) scroller.scrollLeft = left - nameWidth;
  else if (left + columnWidth > scroller.scrollLeft + scroller.clientWidth) {
    scroller.scrollLeft = left + columnWidth - scroller.clientWidth;
  }
}

function focusGrid(): void {
  scroller?.focus({ preventScroll: true });
}

function move(rowsBy: number, colsBy: number, extend: boolean): void {
  selection = moveSelection(selection, rowsBy, colsBy, extend, {
    rows: rows.length,
    cols: columns.length,
  });
  if (selection) reveal(selection.focus);
}

/** Edits the focused cell, starting with `text` as typed (null starts from the value); says why when it cannot. */
async function startEdit(text: string | null): Promise<void> {
  const at = selection?.focus;
  const row = at ? rows[at.row] : undefined;
  const column = at ? columns[at.col] : undefined;
  const cell = at ? row?.cells[at.col] : undefined;
  const info = column ? byId.get(column.field) : undefined;
  if (!at || !row || !column || !cell || !info) return;
  const refusal = editRefusal(row, cell, column, fields);
  if (refusal) {
    onnotice(refusal, row.readOnly !== null);
    return;
  }
  // Only a box takes the typed text; a toggle, a list or an object list opens as it is.
  const kind = editorFor(cellField(info, column, cell), choices);
  editError = "";
  editing = {
    id: row.id,
    key: columnKey(column),
    text: kind === "number" || kind === "text" ? text : null,
  };
  reveal(at);
  await tick();
  const target = editBox?.querySelector<HTMLElement>("input, textarea, select, button");
  target?.focus({ preventScroll: true });
  const box =
    target instanceof HTMLTextAreaElement ||
    (target instanceof HTMLInputElement && target.type === "text");
  if (box) {
    const end = target.value.length;
    target.setSelectionRange(end, end);
  }
}

function endEdit(): void {
  editing = null;
  editError = "";
  focusGrid();
}

/** Sends what the editor holds and ends the edit; false (the edit stays, with the reason) when it was refused. */
async function finishEdit(): Promise<boolean> {
  if (!editing) return true;
  const taken = (await editor?.commit()) ?? true;
  if (taken) endEdit();
  return taken;
}

function act(action: NonNullable<GridAction>): void {
  switch (action.type) {
    case "move":
      move(action.rows, action.cols, action.extend);
      break;
    case "selectAll":
      selection = selectAll({ rows: rows.length, cols: columns.length });
      break;
    case "edit":
      startEdit(action.text);
      break;
    default:
      if (selection) onbulk(action.type);
  }
}

// Keys pressed on the grid itself; the editor's and the header buttons' keys are their own.
function onkeydown(event: KeyboardEvent): void {
  if (event.target !== scroller) return;
  const action = gridAction(event, pageRows());
  if (!action) return;
  event.preventDefault();
  event.stopPropagation();
  if (!editing) {
    act(action);
    return;
  }
  // The grid got the keys back while a cell was still open (a click beside the rows): it is sent first.
  finishEdit().then((taken) => {
    if (taken) act(action);
  });
}

// The editor's Enter, Tab and Esc, before the box sees them; a list of choices or an object picker open in it keeps
// its own keys.
function editorKeys(event: KeyboardEvent): void {
  if (event.target instanceof Element && event.target.closest('[role="dialog"]')) return;
  const action = editorAction(event);
  if (!action) return;
  event.preventDefault();
  event.stopPropagation();
  if (action.type === "cancel") {
    editor?.cancel();
    endEdit();
    return;
  }
  finishEdit().then((taken) => {
    if (taken) move(action.rows, action.cols, false);
  });
}

function cellDown(event: MouseEvent, at: Cell): void {
  if (event.button !== 0) return;
  event.preventDefault(); // no text selection; the grid takes the focus
  const extend = event.shiftKey && selection !== null;
  finishEdit().then((taken) => {
    if (!taken) return;
    focusGrid();
    selection =
      extend && selection ? { anchor: selection.anchor, focus: at } : { anchor: at, focus: at };
    dragging = true;
  });
}

function cellOver(at: Cell): void {
  if (dragging && selection) selection = { anchor: selection.anchor, focus: at };
}

function setEdited(value: string): Promise<string | null> {
  const shown = edited;
  if (!shown) return Promise.resolve("The cell is no longer shown.");
  return onset(shown.row, shown.column, value);
}
</script>

<svelte:window onmouseup={() => (dragging = false)} />

{#snippet arrow(key: string)}
  {#if sort?.key === key}
    <span class="shrink-0 text-accent">
      {#if sort.descending}<ArrowDown size={12} />{:else}<ArrowUp size={12} />{/if}
    </span>
  {/if}
{/snippet}

<div
  class="min-h-0 flex-1 overflow-auto outline-none"
  role="grid"
  aria-rowcount={rows.length + 1}
  aria-colcount={columns.length + 2}
  aria-multiselectable="true"
  tabindex="0"
  bind:this={scroller}
  bind:clientHeight={height}
  onscroll={() => (scrollTop = scroller?.scrollTop ?? 0)}
  {onkeydown}
>
  <div class="min-w-full" style:width="{width}px">
    <div
      class="sticky top-0 z-[2] flex h-[42px] border-b border-line bg-surface text-muted"
      role="row"
      aria-rowindex={1}
    >
      {#each [{ key: "name", label: "Name", width: nameWidth }, { key: "id", label: "Id", width: idWidth }] as fixed (fixed.key)}
        <div
          class="flex shrink-0 border-r border-line bg-surface {fixed.key === 'name'
            ? 'sticky left-0 z-[3]'
            : ''}"
          style:width="{fixed.width}px"
          role="columnheader"
          aria-sort={ariaSort(fixed.key)}
        >
          <button
            type="button"
            class="flex min-w-0 flex-1 items-center gap-1 px-2.5 text-left font-semibold hover:text-fg"
            title="Sort by {fixed.label.toLowerCase()}"
            onclick={() => onsort(fixed.key)}
          >
            <span class="truncate">{fixed.label}</span>
            {@render arrow(fixed.key)}
          </button>
        </div>
      {/each}
      {#each columns as column (columnKey(column))}
        {@const key = columnKey(column)}
        <div
          class="group flex shrink-0 border-r border-line"
          style:width="{columnWidth}px"
          role="columnheader"
          aria-sort={ariaSort(key)}
        >
          <button
            type="button"
            class="flex min-w-0 flex-1 flex-col justify-center pl-2.5 text-left hover:text-fg"
            title="{labelOf(column)} ({column.field}) · sort by it"
            onclick={() => onsort(key)}
          >
            <span class="flex w-full items-center gap-1 leading-tight font-semibold">
              <span class="truncate">{labelOf(column)}</span>
              {@render arrow(key)}
            </span>
            <span class="font-mono text-[11px] leading-tight text-faint">{column.field}</span>
          </button>
          <button
            type="button"
            class="shrink-0 self-start p-1 text-faint opacity-0 group-hover:opacity-100 hover:text-warn focus:opacity-100"
            title="Remove the column"
            aria-label="Remove the column {labelOf(column)}"
            onclick={() => onremove(key)}
          >
            <X size={12} />
          </button>
        </div>
      {/each}
      <div class="flex shrink-0" style:width="{addWidth}px" role="columnheader">
        <button
          type="button"
          class="flex flex-1 items-center gap-1 px-2.5 text-left text-faint hover:text-fg"
          onclick={(event) => onadd(event.currentTarget)}
        >
          <Plus size={13} />
          Add a column
        </button>
      </div>
    </div>

    {#if rows.length === 0}
      <p class="sticky left-0 m-0 px-2.5 py-2 text-muted">No objects match.</p>
    {/if}

    <div class="relative" style:height="{rows.length * rowHeight}px">
      {#each visible as { row, index } (row.id)}
        <div
          class="group absolute inset-x-0 flex border-b border-line hover:bg-hover"
          style:top="{index * rowHeight}px"
          style:height="{rowHeight}px"
          role="row"
          aria-rowindex={index + 2}
        >
          <div
            class="sticky left-0 z-[1] flex shrink-0 items-center border-r border-line bg-bg group-hover:bg-hover"
            style:width="{nameWidth}px"
            role="rowheader"
          >
            <button
              type="button"
              class="min-w-0 flex-1 truncate px-2.5 text-left hover:underline {row.baseId
                ? 'text-accent-hi'
                : 'text-fg'}"
              title="Open {row.name} in Details"
              onclick={() => onopen(row.id)}
            >
              {row.name}
              {#if suffixes.get(row.id)}<span class="text-muted">{suffixes.get(row.id)}</span>{/if}
            </button>
          </div>
          <div
            class="flex shrink-0 items-center border-r border-line px-2.5 font-mono text-[12.5px] text-faint"
            style:width="{idWidth}px"
            role="gridcell"
          >
            {row.id}
          </div>
          {#each columns as column, col (columnKey(column))}
            {@const cell = row.cells[col]}
            {@const look = lookOf(row, column, cell)}
            {@const picked = inBlock(index, col)}
            <!-- The grid's scroller takes the focus and the keys, never a cell (see the top of this file). -->
            <!-- svelte-ignore a11y_interactive_supports_focus -->
            <div
              class="flex shrink-0 cursor-cell items-center border-r border-line px-2.5 select-none {picked
                ? 'bg-accent-lo'
                : look.bg} {look.fg} {isFocus(index, col)
                ? 'outline-2 -outline-offset-2 outline-accent'
                : ''} {isNumber(column) ? 'justify-end font-mono text-[12.5px]' : ''}"
              style:width="{columnWidth}px"
              role="gridcell"
              aria-selected={picked}
              title={look.title}
              onmousedown={(event) => cellDown(event, { row: index, col })}
              onmouseenter={() => cellOver({ row: index, col })}
              ondblclick={() => startEdit(null)}
            >
              <span class="truncate">{cell ? textOf(column, cell) : ""}</span>
            </div>
          {/each}
        </div>
      {/each}

      {#if edited}
        <!-- Over the cell, not in its row: the edit stays while its row scrolls out of view. -->
        <div
          class="absolute"
          style:top="{edited.index * rowHeight}px"
          style:left="{nameWidth + idWidth + edited.col * columnWidth}px"
          style:width="{editorWidths[edited.editor]}px"
          role="presentation"
          bind:this={editBox}
          onkeydowncapture={editorKeys}
        >
          {#key editing}
            <div
              class="flex min-h-[28px] items-center rounded border border-accent bg-surface p-0.5 shadow-[0_6px_20px_rgba(0,0,0,.45)]"
            >
              <div class="min-w-0 flex-1">
                <FieldEditor
                  bind:this={editor}
                  bind:error={editError}
                  field={edited.field}
                  {choices}
                  {lookup}
                  initial={editing?.text ?? null}
                  onset={(_, value) => setEdited(value)}
                  onopen={onfollow}
                />
              </div>
            </div>
          {/key}
          {#if editError}
            <p
              class="m-0 mt-0.5 rounded border border-warn bg-surface px-1.5 py-0.5 text-[12px] text-warn"
              role="alert"
            >
              {editError}
            </p>
          {/if}
        </div>
      {/if}
    </div>
  </div>
</div>
