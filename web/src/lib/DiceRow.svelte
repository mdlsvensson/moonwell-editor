<script lang="ts">
/**
 * A total of dice in the details panel (Sven, 2026-10-27): a unit's damage per attack or bounty as the range the game
 * rolls (Attack 1 - Damage 12 — 13), its lowest and highest in boxes of their own (Sven, 2026-10-28), with its base,
 * number of dice and sides per die under it as fields of their own. Typing the lowest or highest sets the three (one
 * edit, keeping the number of dice where it can); editing one of the three changes the range. The arrow that shows and
 * hides the three sits in the gutter left of the names, so the total's name lines up with the other fields' (Sven,
 * 2026-10-28); it takes the changed colour when one of the three is changed.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import type { ChoiceList, FieldView, Kind } from "./api.ts";
import { type DiceFields, parseBound, rangeEdits, rangeOf, rangeText } from "./dice.ts";
import FieldRow from "./FieldRow.svelte";
import type { Lookup } from "./fields.ts";
import { t } from "./text.ts";

type Props = {
  label: string;
  fields: DiceFields;
  showIds: boolean;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  editable: boolean;
  onset: (field: FieldView, value: string) => Promise<string | null>;
  onreset: (field: FieldView) => Promise<string | null>;
  onopen: (kind: Kind, id: string) => void;
  /** Changes several fields as one edit; answers with why it was refused, or null. */
  onfields: (edits: { field: FieldView; value: string }[]) => Promise<string | null>;
  /** The selected field and level, as `field:level`. */
  selected: string | null;
  onselect: (field: FieldView) => void;
  onmenu: (field: FieldView, x: number, y: number) => void;
  /** Whether the three fields show under the total. */
  open: boolean;
  ontoggle: () => void;
};

let {
  label,
  fields,
  showIds,
  choices,
  lookup,
  editable,
  onset,
  onreset,
  onopen,
  onfields,
  selected,
  onselect,
  onmenu,
  open,
  ontoggle,
}: Props = $props();

const parts = $derived([fields.base, fields.dice, fields.sides]);
const changed = $derived(parts.some((field) => field.changed));
const range = $derived(rangeOf(fields));
const standard = $derived(
  rangeText(
    rangeOf({
      base: { ...fields.base, value: fields.base.base },
      dice: { ...fields.dice, value: fields.dice.base },
      sides: { ...fields.sides, value: fields.sides.base },
    }),
  ),
);

// What was typed in each box and not sent yet; null shows the range's end.
let lowDraft = $state<string | null>(null);
let highDraft = $state<string | null>(null);
let error = $state("");
let low: HTMLInputElement | undefined = $state();
let high: HTMLInputElement | undefined = $state();
// A range changed elsewhere (an edit of one of the three, undo) replaces what was typed.
$effect(() => {
  void range.min;
  void range.max;
  lowDraft = null;
  highDraft = null;
  error = "";
});

async function commit(): Promise<void> {
  if (lowDraft === null && highDraft === null) return;
  const lowText = lowDraft ?? String(range.min);
  const highText = highDraft ?? String(range.max);
  const min = parseBound(lowText);
  const max = parseBound(highText);
  if (min === null || max === null) {
    error = t("dice.notNumber", { field: label, text: min === null ? lowText : highText });
    return;
  }
  if (min > max) {
    error = t("dice.backwards", { field: label, min, max });
    return;
  }
  const edits = rangeEdits(fields, { min, max });
  if (edits.length === 0) {
    lowDraft = null;
    highDraft = null;
    error = "";
    return;
  }
  const problem = await onfields(edits);
  error = problem ?? "";
  if (problem === null) {
    lowDraft = null;
    highDraft = null;
  }
}

// Moving from one box to the other sends nothing yet, so a lowest above the old highest can be typed before the new
// highest.
function leave(event: FocusEvent): void {
  const to = event.relatedTarget;
  if (to !== low && to !== high) commit();
}

function onkey(event: KeyboardEvent): void {
  if (event.key === "Enter") {
    event.preventDefault();
    commit();
  } else if (event.key === "Escape" && (lowDraft !== null || highDraft !== null || error)) {
    event.preventDefault();
    event.stopPropagation();
    lowDraft = null;
    highDraft = null;
    error = "";
  }
}

async function resetAll(): Promise<void> {
  for (const field of parts) {
    if (field.changed) {
      const problem = await onreset(field);
      if (problem) {
        error = problem;
        return;
      }
    }
  }
}
</script>

{#snippet bound(
  draft: string | null,
  shown: number,
  name: string,
  oninput: (text: string) => void,
  bind: (element: HTMLInputElement) => void,
  field?: string,
)}
  <input
    class="field-sizing-content min-w-[5ch] max-w-full rounded border px-1.5 py-[3px] font-mono text-[12.5px] outline-none focus:border-accent {error
      ? 'border-warn bg-warn-lo'
      : 'border-line bg-raised hover:border-muted'} {changed ? 'text-changed' : 'text-fg'}"
    value={draft ?? String(shown)}
    data-field={field}
    aria-label={name}
    aria-invalid={!!error}
    title={changed ? t("field.standard", { value: standard }) : undefined}
    {@attach bind}
    oninput={(event) => {
      oninput(event.currentTarget.value);
      error = "";
    }}
    onkeydown={onkey}
    onblur={leave}
  />
{/snippet}

<div
  data-changed={changed || undefined}
  class="grid min-h-[30px] grid-cols-[22px_var(--label-width,220px)_minmax(120px,1fr)] items-center border-t border-line {selected ===
  `${fields.base.id}:0`
    ? 'bg-accent/10 outline outline-1 -outline-offset-1 outline-accent/60'
    : 'hover:bg-surface'}"
  role="group"
  aria-label={label}
  onpointerdown={() => onselect(fields.base)}
  oncontextmenu={(event) => {
    event.preventDefault();
    onselect(fields.base);
    onmenu(fields.base, event.clientX, event.clientY);
  }}
>
  <button
    type="button"
    class="flex h-full items-center justify-center focus:outline-none {changed
      ? 'text-changed'
      : 'text-faint hover:text-fg'}"
    aria-expanded={open}
    title={open ? t("dice.hideParts") : t("dice.showParts")}
    aria-label={open ? t("dice.hideParts") : t("dice.showParts")}
    onclick={ontoggle}
  >
    {#if open}<ChevronDown size={14} />{:else}<ChevronRight size={14} />{/if}
  </button>
  <button
    type="button"
    class="cursor-default py-1 pr-2 text-left focus:outline-none"
    aria-expanded={open}
    title={open ? t("dice.hideParts") : t("dice.showParts")}
    onclick={ontoggle}
  >
    {label}
  </button>
  <span class="flex min-w-0 items-center gap-1 px-2 py-[3px]">
    {#if editable}
      <span class="flex min-w-0 items-center gap-1.5">
        {@render bound(
          lowDraft,
          range.min,
          t("dice.lowest", { field: label }),
          (text) => (lowDraft = text),
          (element) => {
            low = element;
          },
          `${fields.base.id}:total`,
        )}
        <span class="text-muted" aria-hidden="true">—</span>
        {@render bound(
          highDraft,
          range.max,
          t("dice.highest", { field: label }),
          (text) => (highDraft = text),
          (element) => {
            high = element;
          },
        )}
      </span>
      {#if changed}
        <button
          type="button"
          class="shrink-0 px-1 text-changed hover:text-fg"
          title={t("field.resetTitle", { value: standard })}
          aria-label={t("field.resetLabel", { field: label })}
          onclick={resetAll}
        >
          ↺
        </button>
      {/if}
    {:else}
      <span class="px-1.5 py-[3px] font-mono text-[12.5px] {changed ? 'text-changed' : 'text-fg'}"
        >{range.min} — {range.max}</span
      >
    {/if}
  </span>
  {#if error}
    <span class="col-start-2 col-end-4 pr-2 pb-1.5 text-xs text-warn" role="alert">{error}</span>
  {/if}
</div>
{#if open}
  {#each parts as part (part.id)}
    <FieldRow
      field={part}
      {showIds}
      {choices}
      {lookup}
      {editable}
      {onset}
      {onreset}
      {onopen}
      selected={selected === `${part.id}:0`}
      {onselect}
      {onmenu}
      nested
    />
  {/each}
{/if}
