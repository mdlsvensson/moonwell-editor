<script lang="ts">
/**
 * A total of dice in the details panel (Sven, 2026-10-27): a unit's damage per attack or bounty as the range the game
 * rolls (Attack 1 - Damage 12 - 13), which can be typed, with its base, number of dice and sides per die under it as
 * fields of their own. Typing a range sets the three (one edit, keeping the number of dice where it can); editing one
 * of the three changes the range.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import { type ChoiceList, type FieldView, iconUrl, type Kind } from "./api.ts";
import { type DiceFields, parseRange, rangeEdits, rangeOf, rangeText } from "./dice.ts";
import FieldRow from "./FieldRow.svelte";
import type { Lookup } from "./fields.ts";
import { graphics } from "./graphics.svelte.ts";
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
  /** The game's icon for the total, as a game path; undefined for none. */
  icon?: (field: FieldView) => string | undefined;
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
  icon = () => undefined,
  open,
  ontoggle,
}: Props = $props();

const parts = $derived([fields.base, fields.dice, fields.sides]);
const changed = $derived(parts.some((field) => field.changed));
const range = $derived(rangeText(rangeOf(fields)));
const standard = $derived(
  rangeText(
    rangeOf({
      base: { ...fields.base, value: fields.base.base },
      dice: { ...fields.dice, value: fields.dice.base },
      sides: { ...fields.sides, value: fields.sides.base },
    }),
  ),
);
const totalIcon = $derived(icon(fields.base));

// What was typed and not sent yet; null shows the range.
let draft = $state<string | null>(null);
let error = $state("");
// A range changed elsewhere (an edit of one of the three, undo) replaces what was typed.
$effect(() => {
  void range;
  draft = null;
  error = "";
});

async function commit(): Promise<void> {
  if (draft === null) return;
  const typed = parseRange(draft);
  if (!typed) {
    error = t("dice.notRange", { field: label, text: draft });
    return;
  }
  const edits = rangeEdits(fields, typed);
  if (edits.length === 0) {
    draft = null;
    error = "";
    return;
  }
  const problem = await onfields(edits);
  error = problem ?? "";
  if (problem === null) draft = null;
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
  <span class="h-[18px] w-1 justify-self-center rounded-sm {changed ? 'bg-changed' : ''}"></span>
  <button
    type="button"
    class="flex items-center gap-1 py-1 pr-2 text-left focus:outline-none"
    aria-expanded={open}
    title={open ? t("dice.hideParts") : t("dice.showParts")}
    onclick={ontoggle}
  >
    <span class="-ml-1 flex w-3.5 shrink-0 items-center text-faint">
      {#if open}<ChevronDown size={13} />{:else}<ChevronRight size={13} />{/if}
    </span>
    {label}
  </button>
  <span class="flex min-w-0 items-center gap-1 px-2 py-[3px]">
    {#if totalIcon}
      <img src={iconUrl(totalIcon, graphics.mode)} alt="" class="size-5 shrink-0" draggable="false" />
    {/if}
    {#if editable}
      <input
        class="field-sizing-content min-w-[8ch] max-w-full rounded border px-1.5 py-[3px] font-mono text-[12.5px] outline-none focus:border-accent {error
          ? 'border-warn bg-warn-lo'
          : 'border-line bg-raised hover:border-muted'} {changed ? 'text-changed' : 'text-fg'}"
        value={draft ?? range}
        data-field="{fields.base.id}:total"
        aria-label={label}
        aria-invalid={!!error}
        title={changed ? t("field.standard", { value: standard }) : undefined}
        oninput={(event) => {
          draft = event.currentTarget.value;
          error = "";
        }}
        onkeydown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            commit();
          } else if (event.key === "Escape" && (draft !== null || error)) {
            event.preventDefault();
            event.stopPropagation();
            draft = null;
            error = "";
          }
        }}
        onblur={commit}
      />
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
      <span class="px-1.5 py-[3px] font-mono text-[12.5px] {changed ? 'text-changed' : 'text-fg'}">{range}</span>
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
