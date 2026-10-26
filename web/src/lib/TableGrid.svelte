<script lang="ts">
/**
 * The table's grid: a header that stays in view (Name, Id, then each column's label with `· n` for a level and the
 * field id under it), and one row per object. A table can hold every object of a kind, so only the rows in view are
 * drawn: every row is the same height, so the row at any scroll position is a division away (as the tree does). The
 * name column stays in view when the grid scrolls sideways.
 */
import ArrowDown from "@lucide/svelte/icons/arrow-down";
import ArrowUp from "@lucide/svelte/icons/arrow-up";
import Plus from "@lucide/svelte/icons/plus";
import X from "@lucide/svelte/icons/x";
import type { ChoiceList, Column, FieldInfo, TableCell, TableRow } from "./api.ts";
import { type Lookup, shownValue } from "./fields.ts";
import { columnKey, type Sort } from "./table.ts";

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
}: Props = $props();

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
): { tone: string; title?: string } {
  if (!cell?.applies) return { tone: "bg-surface", title: "Does not apply to this object" };
  if (row.readOnly) return { tone: "bg-surface text-muted", title: `Read only: ${row.readOnly}` };
  if (cell.warning) return { tone: "bg-warn-lo text-warn", title: cell.warning };
  if (cell.changed) {
    const base = textOf(column, { ...cell, value: cell.base });
    return { tone: "text-changed", title: `Standard: ${base === "" ? "(empty)" : base}` };
  }
  return { tone: "text-fg" };
}

function ariaSort(key: string): "ascending" | "descending" | "none" {
  if (sort?.key !== key) return "none";
  return sort.descending ? "descending" : "ascending";
}
</script>

{#snippet arrow(key: string)}
  {#if sort?.key === key}
    <span class="shrink-0 text-accent">
      {#if sort.descending}<ArrowDown size={12} />{:else}<ArrowUp size={12} />{/if}
    </span>
  {/if}
{/snippet}

<div
  class="min-h-0 flex-1 overflow-auto"
  role="grid"
  aria-rowcount={rows.length + 1}
  aria-colcount={columns.length + 2}
  bind:this={scroller}
  bind:clientHeight={height}
  onscroll={() => (scrollTop = scroller?.scrollTop ?? 0)}
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
            <div
              class="flex shrink-0 items-center border-r border-line px-2.5 {look.tone} {isNumber(
                column,
              )
                ? 'justify-end font-mono text-[12.5px]'
                : ''}"
              style:width="{columnWidth}px"
              role="gridcell"
              title={look.title}
            >
              <span class="truncate">{cell ? textOf(column, cell) : ""}</span>
            </div>
          {/each}
        </div>
      {/each}
    </div>
  </div>
</div>
