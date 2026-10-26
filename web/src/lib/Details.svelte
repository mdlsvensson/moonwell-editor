<script lang="ts">
/**
 * The middle panel: the object's name, id and chain, the field filter and chips, then its fields by category (which
 * open and close), per-level fields as grids.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import Lock from "@lucide/svelte/icons/lock";
import type { ChoiceList, Kind, ObjectView } from "./api.ts";
import FieldRow from "./FieldRow.svelte";
import { groupFields, isChanged, type Lookup, type Row, shownValue } from "./fields.ts";
import LevelGrid from "./LevelGrid.svelte";

type Props = {
  view: ObjectView;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  filter: string;
  changedOnly: boolean;
  showIds: boolean;
  onopen: (kind: Kind, id: string) => void;
};

let {
  view,
  choices,
  lookup,
  filter = $bindable(),
  changedOnly = $bindable(),
  showIds = $bindable(),
  onopen,
}: Props = $props();

let filterInput: HTMLInputElement | undefined = $state();
// Categories the user closed, by key; they stay closed from object to object.
let closed = $state<Record<string, boolean>>({});

const nameOf = (listOf: string, id: string) => lookup(listOf, id)?.name;
const categories = $derived(
  groupFields(view.fields, filter, changedOnly, (field) => shownValue(field, choices, nameOf)),
);
const changedCount = $derived(
  groupFields(view.fields, "", true).reduce((sum, category) => sum + category.rows.length, 0),
);

/** Puts the cursor in the field filter (Ctrl F). */
export function focusFilter(): void {
  filterInput?.focus();
  filterInput?.select();
}

/** A category's rows as runs: fields with no levels one by one, per-level fields together as one grid. */
function runs(rows: Row[]): { grid: boolean; rows: Row[] }[] {
  const result: { grid: boolean; rows: Row[] }[] = [];
  for (const row of rows) {
    const grid = row.levels !== null;
    const lastRun = result[result.length - 1];
    if (lastRun && lastRun.grid === grid) lastRun.rows.push(row);
    else result.push({ grid, rows: [row] });
  }
  return result;
}

function clearOnEscape(event: KeyboardEvent): void {
  if (event.key === "Escape" && filter) {
    event.stopPropagation();
    filter = "";
  }
}
</script>

<div class="pb-10">
  <div class="sticky top-0 z-[2] border-b border-line bg-bg px-[18px] pt-3.5 pb-2.5">
    <h1 class="m-0 flex items-baseline gap-2.5 font-heading text-xl font-bold text-fg">
      {view.name}
      <span class="font-mono text-[12.5px] font-normal text-muted">{view.id}</span>
    </h1>
    <div class="mt-1 text-[13px] text-muted">
      {#each view.chain as link, index (index)}
        {#if index > 0}<span class="mx-0.5">›</span>{/if}
        {#if index === view.chain.length - 1 && link.id}
          <b class="font-semibold text-accent-hi">{link.name}</b>
        {:else if link.id}
          <button
            type="button"
            class="font-semibold text-body hover:text-fg hover:underline"
            onclick={() => onopen(view.kind, link.id)}
          >
            {link.name}
          </button>
          <span class="font-mono text-[12.5px]">{link.id}</span>
        {:else}
          <span>{link.name}</span>
        {/if}
      {/each}
      · {changedCount === 0
        ? "no fields changed"
        : `${changedCount} ${changedCount === 1 ? "field" : "fields"} changed`}
    </div>
    {#if view.readOnly}
      <p class="mt-2 flex items-center gap-1.5 text-[12.5px] text-warn" data-testid="read-only">
        <Lock size={14} />
        Read only: {view.readOnly}
      </p>
    {/if}
    <div class="mt-2.5 flex items-center gap-2">
      <input
        class="w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
        placeholder="Filter fields: name, id or value (Ctrl F)…"
        aria-label="Filter fields"
        bind:this={filterInput}
        bind:value={filter}
        onkeydown={clearOnEscape}
      />
      <button
        type="button"
        class="rounded-full border bg-raised px-2.5 py-0.5 text-[12.5px] whitespace-nowrap {changedOnly
          ? 'border-changed text-changed'
          : 'border-line text-muted'}"
        aria-pressed={changedOnly}
        onclick={() => (changedOnly = !changedOnly)}
      >
        Changed only
      </button>
      <button
        type="button"
        class="rounded-full border bg-raised px-2.5 py-0.5 text-[12.5px] whitespace-nowrap {showIds
          ? 'border-changed text-changed'
          : 'border-line text-muted'}"
        aria-pressed={showIds}
        title="Ctrl D"
        onclick={() => (showIds = !showIds)}
      >
        Field ids
      </button>
    </div>
  </div>

  {#if categories.length === 0}
    <p class="mx-[18px] mt-3.5 text-muted">
      {changedOnly && !filter ? "The map changes no fields of this object." : "No field matches."}
    </p>
  {/if}

  {#each categories as category (category.key)}
    {@const open = !closed[category.key]}
    <div class="mx-[18px] mt-3.5 overflow-hidden rounded-[7px] border border-line">
      <button
        type="button"
        class="flex w-full items-center gap-2 bg-surface px-2.5 py-1.5 text-left font-bold text-fg"
        aria-expanded={open}
        onclick={() => (closed[category.key] = open)}
      >
        {#if open}<ChevronDown size={14} />{:else}<ChevronRight size={14} />{/if}
        {category.name}
        <span class="ml-auto text-xs font-normal text-faint">
          {category.rows.length}
          {category.rows.length === 1 ? "field" : "fields"}
          {#if category.rows.some(isChanged)}· <span class="text-changed">changed</span>{/if}
        </span>
      </button>
      {#if open}
        {#each runs(category.rows) as run, index (index)}
          {#if run.grid}
            <LevelGrid rows={run.rows} {showIds} {choices} {lookup} />
          {:else}
            {#each run.rows as row (row.id)}
              <FieldRow field={row.field} {showIds} {choices} {lookup} {onopen} />
            {/each}
          {/if}
        {/each}
      {/if}
    </div>
  {/each}
</div>
