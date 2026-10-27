<script lang="ts">
/**
 * The middle panel: the object's name, id and chain, Copy and Delete, the field filter, then its fields by category
 * (which open and close), per-level fields as grids. The fields are editors unless the object is read only. A bar
 * floats at the bottom with New, "Changed only", "Field ids", and Collapse all and Expand all.
 */

import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import ChevronsDownUp from "@lucide/svelte/icons/chevrons-down-up";
import ChevronsUpDown from "@lucide/svelte/icons/chevrons-up-down";
import Copy from "@lucide/svelte/icons/copy";
import Hash from "@lucide/svelte/icons/hash";
import Lock from "@lucide/svelte/icons/lock";
import PencilLine from "@lucide/svelte/icons/pencil-line";
import Plus from "@lucide/svelte/icons/plus";
import Trash from "@lucide/svelte/icons/trash-2";
import { type Component, tick } from "svelte";
import type { ChoiceList, FieldView, Kind, ObjectView } from "./api.ts";
import type { MenuItem } from "./ContextMenu.svelte";
import FieldRow from "./FieldRow.svelte";
import { copiedField, copyField, fits } from "./fieldClip.ts";
import {
  type ButtonPair,
  buttonPair,
  groupFields,
  isChanged,
  type Lookup,
  namesOf,
  type Row,
  shownValue,
} from "./fields.ts";
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
  /** Changes several fields of the object as one edit; answers with why it was refused, or null. */
  onfields: (edits: { field: FieldView; value: string }[]) => Promise<string | null>;
  /** The icons the kind's objects use, in the tree's order, for the icon picker. */
  kindIcons?: string[];
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
  onfields,
  kindIcons = [],
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
 * Ctrl C, Ctrl X or Ctrl V while a field is selected and the panel is where the user is (the page asks only then):
 * copies the field's value, or pastes the copied one into it (a field is never cut). False when no field is selected,
 * so the key goes to the object.
 */
export function clipKey(key: "c" | "x" | "v"): boolean {
  const field = selectedField();
  if (!field) return false;
  if (key === "c") copy(field);
  else if (key === "v") paste(field);
  else onnotice("A field cannot be cut: Ctrl C copies its value.", false);
  return true;
}

/** Puts a button at a place on the command card: its X and Y, as one edit. */
function setPair(pair: ButtonPair, x: number, y: number): Promise<string | null> {
  return onfields([
    { field: pair.x, value: String(x) },
    { field: pair.y, value: String(y) },
  ]);
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
let scroller: HTMLDivElement | undefined = $state();

/** A changed field's mark beside the scroll bar: how far down the fields it is, in percent. */
type Mark = { top: number; label: string; element: HTMLElement };
let marks = $state.raw<Mark[]>([]);

// The marks follow what is drawn: measured again whenever the fields' height or the object's fields change.
function measureMarks(): void {
  const box = scroller;
  if (!box || box.scrollHeight === 0) {
    marks = [];
    return;
  }
  const top = box.getBoundingClientRect().top;
  marks = [...box.querySelectorAll<HTMLElement>("[data-changed]")].map((element) => ({
    top: ((element.getBoundingClientRect().top - top + box.scrollTop) / box.scrollHeight) * 100,
    label: element.getAttribute("aria-label") ?? element.textContent?.trim().split("\n")[0] ?? "",
    element,
  }));
}

$effect(() => {
  void categories;
  void closed;
  const box = scroller;
  if (!box) return;
  const resized = new ResizeObserver(() => measureMarks());
  for (const child of box.children) resized.observe(child);
  resized.observe(box);
  tick().then(measureMarks);
  return () => resized.disconnect();
});
// Categories the user closed, by key; they stay closed from object to object.
let closed = $state<Record<string, boolean>>({});

const nameOf = namesOf((listOf, id) => lookup(listOf, id));
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

/** How far the fields are scrolled, for the history to come back to. */
export function scrollPosition(): number {
  return scroller?.scrollTop ?? 0;
}

/** Scrolls the fields to a position the history kept. */
export function scrollTo(top: number): void {
  if (scroller) scroller.scrollTop = top;
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

<div class="relative flex min-h-0 flex-1 flex-col" style:--label-width="{labelWidth}px" bind:this={root}>
  <div class="shrink-0 border-b border-line bg-bg px-[18px] pt-3.5 pb-2.5">
    <div class="flex items-start gap-2">
      <h1 class="m-0 flex min-w-0 flex-1 items-baseline gap-2.5 font-heading text-xl font-bold text-fg">
        <span class="truncate">{view.name}</span>
        <span class="font-mono text-[12.5px] font-normal text-muted">{view.id}</span>
      </h1>
      {#if editable && custom}
        <div class="flex shrink-0 gap-1" role="group" aria-label="Object">
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
    <input
      class="mt-2.5 w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
      placeholder="Filter fields: name, id or value (Ctrl F)…"
      aria-label="Filter fields"
      bind:this={filterInput}
      bind:value={filter}
      onkeydown={clearOnEscape}
    />
  </div>

  <!-- Only the fields scroll: the header stays above them and the bar floats over them, where it is. -->
  <div class="relative flex min-h-0 flex-1">
  <div class="min-h-0 flex-1 overflow-auto pb-16" bind:this={scroller}>
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
            data-changed={(!open && category.rows.some(isChanged)) || undefined}
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
                    pair={buttonPair(row.field, view.fields)}
                    objectKind={view.kind}
                    {kindIcons}
                    onpair={setPair}
                  />
                {/each}
              {/if}
            {/each}
          {/if}
        </div>
      {/each}
    {/key}
  </div>

  <!-- The changed fields' places beside the scroll bar, as a code editor marks changed lines; a click goes there. -->
  <div class="pointer-events-none absolute inset-y-0 right-0 w-2" aria-hidden="true">
    {#each marks as mark, index (index)}
      <button
        type="button"
        tabindex="-1"
        class="pointer-events-auto absolute right-0 h-[3px] w-2 rounded-[1px] bg-changed/80 hover:bg-changed"
        style:top="{mark.top}%"
        title="Changed: {mark.label}"
        onclick={() => mark.element.scrollIntoView({ block: "center" })}
      ></button>
    {/each}
  </div>
  </div>

  <!-- Floats at the bottom of the panel, over the fields, and never moves (Sven, 2026-10-27): icons, named by their
       tooltips. -->
  <div
    class="absolute bottom-3 left-1/2 flex -translate-x-1/2 items-center gap-0.5 rounded-full border border-line bg-raised p-0.5 shadow-lg shadow-black/40"
    role="toolbar"
    aria-label="Fields"
  >
    {#if editable}
      {@render barButton(
        `New object based on ${custom ? "its base" : "this object"} (Ctrl Shift N)`,
        Plus,
        onnew,
      )}
      <span class="mx-0.5 h-4 w-px bg-line" aria-hidden="true"></span>
    {/if}
    {@render barButton("Changed only", PencilLine, () => (changedOnly = !changedOnly), changedOnly)}
    {@render barButton("Field ids (Ctrl D)", Hash, () => (showIds = !showIds), showIds)}
    <span class="mx-0.5 h-4 w-px bg-line" aria-hidden="true"></span>
    {@render barButton("Collapse all", ChevronsDownUp, () => collapseAll(true))}
    {@render barButton("Expand all", ChevronsUpDown, () => collapseAll(false))}
  </div>
</div>

{#snippet barButton(
  label: string,
  Icon: Component<{ size?: number }>,
  onclick: () => void,
  pressed?: boolean,
)}
  <button
    type="button"
    class="rounded-full p-1.5 {pressed
      ? 'bg-accent-lo text-accent-hi'
      : 'text-muted hover:bg-hover hover:text-fg'}"
    title={label}
    aria-label={label}
    aria-pressed={pressed}
    {onclick}
  >
    <Icon size={15} />
  </button>
{/snippet}
