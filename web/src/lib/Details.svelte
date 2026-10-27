<script lang="ts">
/**
 * The middle panel: the object's name, id and chain, New / Copy / Delete, the field filter and chips, then its fields
 * by category (which open and close), per-level fields as grids. The fields are editors unless the object is read
 * only.
 */

import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import ChevronsDownUp from "@lucide/svelte/icons/chevrons-down-up";
import ChevronsUpDown from "@lucide/svelte/icons/chevrons-up-down";
import Copy from "@lucide/svelte/icons/copy";
import Lock from "@lucide/svelte/icons/lock";
import Plus from "@lucide/svelte/icons/plus";
import Trash from "@lucide/svelte/icons/trash-2";
import { tick } from "svelte";
import type { ChoiceList, FieldView, Kind, ObjectView } from "./api.ts";
import type { MenuItem } from "./ContextMenu.svelte";
import FieldRow from "./FieldRow.svelte";
import { copiedField, copyField, fits } from "./fieldClip.ts";
import { groupFields, isChanged, type Lookup, type Row, shownValue } from "./fields.ts";
import LevelGrid from "./LevelGrid.svelte";
import { labelColumnWidth } from "./measure.ts";
import { kindText } from "./tree.ts";

type Props = {
  view: ObjectView;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  filter: string;
  changedOnly: boolean;
  showIds: boolean;
  onopen: (kind: Kind, id: string) => void;
  onset: (field: FieldView, value: string) => Promise<string | null>;
  onreset: (field: FieldView) => Promise<string | null>;
  /** New object based on this one (or on its base); Ctrl Shift N. */
  onnew: () => void;
  /** Copies this custom object. */
  oncopy: () => void;
  /** Deletes this custom object (asks first). */
  ondelete: () => void;
  /** Opens a context menu at the pointer. */
  onmenu: (x: number, y: number, items: MenuItem[]) => void;
  /** Shows a short message in the status bar. */
  onnotice: (text: string, warn: boolean) => void;
};

let {
  view,
  choices,
  lookup,
  filter = $bindable(),
  changedOnly = $bindable(),
  showIds = $bindable(),
  onopen,
  onset,
  onreset,
  onnew,
  oncopy,
  ondelete,
  onmenu,
  onnotice,
}: Props = $props();

let root: HTMLDivElement | undefined = $state();
// The selected field and level, as `field:level` (Sven, 2026-10-27); it goes with the object.
let selected = $state<string | null>(null);
let selectedOf = "";
$effect.pre(() => {
  const of = `${view.kind}:${view.id}`;
  if (of !== selectedOf) {
    selectedOf = of;
    selected = null;
  }
});

function keyOf(field: FieldView): string {
  return `${field.id}:${field.level}`;
}

function select(field: FieldView): void {
  selected = keyOf(field);
}

function selectedField(): FieldView | undefined {
  return view.fields.find((field) => keyOf(field) === selected);
}

function copy(field: FieldView): void {
  copyField(field);
  navigator.clipboard?.writeText(field.value).catch(() => {});
  onnotice(
    `Copied ${field.label}${field.level > 0 ? ` (level ${field.level})` : ""} · Ctrl V pastes it into a field that takes it`,
    false,
  );
}

async function paste(field: FieldView): Promise<void> {
  const clip = copiedField();
  if (!clip) {
    onnotice("No field is copied: select a field and press Ctrl C.", false);
    return;
  }
  if (!editable) {
    onnotice(`Read only: ${view.readOnly}`, true);
    return;
  }
  if (!fits(clip, field)) {
    onnotice(`${clip.label}'s value does not go in ${field.label}.`, true);
    return;
  }
  const error = await onset(field, clip.value);
  onnotice(error ?? `Pasted ${clip.label}'s value into ${field.label}`, error !== null);
}

/**
 * Ctrl C, Ctrl X or Ctrl V while a field is selected and the focus is in the panel: copies the field's value, or
 * pastes the copied one into it (a field is never cut). False when no field is selected there, so the key goes to the
 * object.
 */
export function clipKey(key: "c" | "x" | "v"): boolean {
  const field = selectedField();
  if (!field || !root?.contains(document.activeElement)) return false;
  if (key === "c") copy(field);
  else if (key === "v") paste(field);
  else onnotice("A field cannot be cut: Ctrl C copies its value.", false);
  return true;
}

/** The context menu of a field (one level of a per-level field). */
function fieldMenu(field: FieldView, x: number, y: number): void {
  const clip = copiedField();
  onmenu(x, y, [
    { label: "Copy", shortcut: "Ctrl C", run: () => copy(field) },
    {
      label: clip ? `Paste ${clip.label}` : "Paste",
      shortcut: "Ctrl V",
      disabled: !clip || !editable || !fits(clip, field),
      run: () => paste(field),
    },
    { separator: true },
    {
      label: "Reset to default",
      disabled: !editable || !field.changed,
      run: () => onreset(field),
    },
  ]);
}

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

// The values start just past the widest name of the object's fields shown one per row, filtered or not, so they
// stay put while the filter changes (measured once the fonts are in).
let fontsReady = $state(false);
document.fonts?.ready.then(() => (fontsReady = true));
const labelWidth = $derived.by(() => {
  void fontsReady;
  const labels = groupFields(view.fields, "", false).flatMap((category) =>
    category.rows
      .filter((row) => row.levels === null)
      .map((row) => ({ label: row.label, id: row.id })),
  );
  return labelColumnWidth(labels, showIds);
});

const editable = $derived(view.readOnly === null);
const custom = $derived(view.baseId !== null);

/**
 * Puts the cursor in a field's editor (F2 for the name), showing it first when the filter, "Changed only" or a closed
 * category hides it. False when the field has no editor.
 */
export async function focusField(field: FieldView): Promise<boolean> {
  const selector = `[data-field="${field.id}:${field.level}"]`;
  let element = document.querySelector<HTMLElement>(selector);
  if (!element) {
    filter = "";
    changedOnly = false;
    closed[field.category] = false;
    await tick();
    element = document.querySelector<HTMLElement>(selector);
  }
  if (!element) return false;
  element.focus();
  if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)
    element.select();
  return true;
}

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

/** Closes every category (true) or opens every one (false); they stay so from object to object. */
function collapseAll(collapse: boolean): void {
  closed = collapse ? Object.fromEntries(categories.map((category) => [category.key, true])) : {};
}

function clearOnEscape(event: KeyboardEvent): void {
  if (event.key === "Escape" && filter) {
    event.stopPropagation();
    filter = "";
  }
}
</script>

<div class="pb-4" style:--label-width="{labelWidth}px" bind:this={root}>
  <div class="sticky top-0 z-[2] border-b border-line bg-bg px-[18px] pt-3.5 pb-2.5">
    <div class="flex items-start gap-2">
      <h1 class="m-0 flex min-w-0 flex-1 items-baseline gap-2.5 font-heading text-xl font-bold text-fg">
        <span class="truncate">{view.name}</span>
        <span class="font-mono text-[12.5px] font-normal text-muted">{view.id}</span>
      </h1>
      {#if editable}
        <div class="flex shrink-0 gap-1" role="group" aria-label="Object">
          <button
            type="button"
            class="flex items-center gap-1 rounded-[5px] border border-line bg-raised px-2 py-0.5 text-[12.5px] text-body hover:border-accent hover:text-fg"
            title="A new object based on {custom ? 'its base' : 'this object'} (Ctrl Shift N)"
            onclick={onnew}
          >
            <Plus size={13} />
            New
          </button>
          {#if custom}
            <button
              type="button"
              class="flex items-center gap-1 rounded-[5px] border border-line bg-raised px-2 py-0.5 text-[12.5px] text-body hover:border-accent hover:text-fg"
              title="A copy of this object, with the next free id (Ctrl Shift D)"
              onclick={oncopy}
            >
              <Copy size={13} />
              Copy
            </button>
            <button
              type="button"
              class="flex items-center gap-1 rounded-[5px] border border-line bg-raised px-2 py-0.5 text-[12.5px] text-body hover:border-warn hover:text-warn"
              title="Delete this object (Delete; asks first, and Ctrl Z brings it back)"
              onclick={ondelete}
            >
              <Trash size={13} />
              Delete
            </button>
          {/if}
        </div>
      {/if}
    </div>
    <div class="mt-1 text-[13px] text-muted">
      {#each view.chain as link, index (index)}
        {#if index > 0}<span class="mx-0.5">›</span>{/if}
        {#if index === view.chain.length - 1 && link.id}
          <b class="font-semibold {kindText[view.kind]}">{link.name}</b>
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

  <!-- The editors are made again for another object, so what was typed for one never shows on the next. -->
  {#key `${view.kind}:${view.id}`}
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
              <LevelGrid
                rows={run.rows}
                {showIds}
                {choices}
                {lookup}
                {editable}
                {onset}
                {onreset}
                {onopen}
                {selected}
                onselect={select}
                onmenu={fieldMenu}
              />
            {:else}
              {#each run.rows as row (row.id)}
                <FieldRow
                  field={row.field}
                  {showIds}
                  {choices}
                  {lookup}
                  {editable}
                  {onset}
                  {onreset}
                  {onopen}
                  selected={selected === keyOf(row.field)}
                  onselect={select}
                  onmenu={fieldMenu}
                />
              {/each}
            {/if}
          {/each}
        {/if}
      </div>
    {/each}
  {/key}

  <!-- Floats at the bottom of the panel while the fields scroll under it. -->
  {#if categories.length > 1}
    <div class="pointer-events-none sticky bottom-3 mt-4 flex justify-center">
      <div
        class="pointer-events-auto flex gap-0.5 rounded-full border border-line bg-raised p-0.5 shadow-lg shadow-black/40"
        role="group"
        aria-label="Categories"
      >
        <button
          type="button"
          class="flex items-center gap-1.5 rounded-full px-3 py-1 text-[12.5px] text-muted hover:bg-hover hover:text-fg"
          onclick={() => collapseAll(true)}
        >
          <ChevronsDownUp size={14} />
          Collapse all
        </button>
        <button
          type="button"
          class="flex items-center gap-1.5 rounded-full px-3 py-1 text-[12.5px] text-muted hover:bg-hover hover:text-fg"
          onclick={() => collapseAll(false)}
        >
          <ChevronsUpDown size={14} />
          Expand all
        </button>
      </div>
    </div>
  {/if}
</div>
