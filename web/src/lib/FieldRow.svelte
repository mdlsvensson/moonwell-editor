<script lang="ts">
/**
 * One row of the details panel for a field with no levels: the changed bar, the label, the value (its editor, when
 * the object can be edited) and ↺ to put a changed field back to its standard value.
 */

import AssetPicker from "./AssetPicker.svelte";
import { type ChoiceList, type FieldView, iconUrl, type Kind } from "./api.ts";
import CommandCard from "./CommandCard.svelte";
import FieldEditor from "./FieldEditor.svelte";
import { type ButtonPair, type Lookup, listIds, namesOf, shownValue } from "./fields.ts";
import { graphics } from "./graphics.svelte.ts";
import { t } from "./text.ts";
import { isExtendedTooltip } from "./values.ts";

type Props = {
  field: FieldView;
  showIds: boolean;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  /** False for an object that cannot be edited: the value is only shown. */
  editable: boolean;
  onset: (field: FieldView, value: string) => Promise<string | null>;
  onreset: (field: FieldView) => Promise<string | null>;
  onopen: (kind: Kind, id: string) => void;
  /** True when the field is the one selected (for Ctrl C and Ctrl V). */
  selected: boolean;
  onselect: (field: FieldView) => void;
  /** A right click on the field, for its context menu. */
  onmenu: (field: FieldView, x: number, y: number) => void;
  /** For a button position field, it and its other half, which the command card sets together; null otherwise. */
  pair?: ButtonPair | null;
  /** The kind of the object the field is on (`unit`), for the pickers' order. */
  objectKind?: string;
  /** The icons the kind's objects use, for the icon picker's order. */
  kindIcons?: string[];
  /** Sets the pair's X and Y as one edit; answers with why it was refused, or null. */
  onpair?: (pair: ButtonPair, x: number, y: number) => Promise<string | null>;
  /** Indents the row under a group's row (a damage's base, dice and sides). */
  nested?: boolean;
};

let {
  field,
  showIds,
  choices,
  lookup,
  editable,
  onset,
  onreset,
  onopen,
  selected,
  onselect,
  onmenu,
  pair = null,
  onpair,
  objectKind = "",
  kindIcons = [],
  nested = false,
}: Props = $props();

let error = $state("");

const nameOf = namesOf((listOf, id) => lookup(listOf, id));
const shown = $derived(shownValue(field, choices, nameOf));
const isList = $derived(field.type === "objectList" || field.type === "objectRef");
const objects = $derived(
  isList ? listIds(field.value).map((id) => ({ id, found: lookup(field.listOf ?? "", id) })) : [],
);
const standard = $derived(shownValue({ ...field, value: field.base }, choices, nameOf));
const title = $derived(titleOf(field));
// A text of several lines, or an extended tooltip, gets a box as wide as the row; any other value a box as wide as
// itself.
const multiline = $derived(isExtendedTooltip(field) || field.value.includes("\n"));

function titleOf(view: FieldView): string {
  const lines = [shown];
  if (view.changed) lines.push(t("field.standard", { value: standard }));
  if (view.modes) {
    lines.push(
      Object.entries(view.modes)
        .map(([mode, value]) => `${mode.toUpperCase()}: ${value}`)
        .join(" · "),
    );
  }
  return lines.join("\n");
}

async function reset(): Promise<void> {
  error = (await onreset(field)) ?? "";
}

// A double click anywhere on a True or False field turns it (Sven, 2026-10-29); a click on the word itself already
// does, so a double click there is left to its two clicks.
async function flip(event: MouseEvent): Promise<void> {
  if (!editable || field.type !== "bool" || (event.target as Element).closest("[aria-pressed]"))
    return;
  window.getSelection()?.removeAllRanges();
  error = (await onset(field, field.value === "1" ? "0" : "1")) ?? "";
}
</script>

<div
  data-changed={field.changed || undefined}
  class="grid min-h-[30px] grid-cols-[22px_var(--label-width,220px)_minmax(120px,1fr)] items-center border-t border-line {selected
    ? 'bg-accent/10 outline outline-1 -outline-offset-1 outline-accent/60'
    : 'hover:bg-surface'}"
  role="group"
  aria-label={field.label}
  onfocusin={() => onselect(field)}
  onpointerdown={() => onselect(field)}
  ondblclick={flip}
  oncontextmenu={(event) => {
    event.preventDefault();
    onselect(field);
    onmenu(field, event.clientX, event.clientY);
  }}
>
  <span class="h-[18px] w-1 justify-self-center rounded-sm {field.changed ? 'bg-changed' : ''}"></span>
  <button
    type="button"
    class="cursor-default py-1 pr-2 text-left focus:outline-none {nested ? 'pl-5 text-muted' : ''}"
    title={editable && field.modes ? title : undefined}
    onclick={() => onselect(field)}
  >
    {field.label}
    {#if showIds}<span class="ml-1.5 font-mono text-[12.5px] text-faint">{field.id}</span>{/if}
  </button>
  <!-- The value starts right after the names, as World Editor lays them out (Sven, 2026-10-27). -->
  <span class="flex min-w-0 items-center gap-1 px-2 py-[3px]">
    {#if field.asset === "icon" && field.value.trim()}
      <!-- The icon the field names (Sven, 2026-10-27). -->
      <img
        src={iconUrl(field.value, graphics.mode)}
        alt=""
        class="size-6 shrink-0 rounded-[2px]"
        draggable="false"
        onerror={(event) => ((event.currentTarget as HTMLImageElement).style.display = "none")}
      />
    {/if}
    {#if editable}
      <span class="min-w-0 {multiline ? 'flex-1' : ''}"><FieldEditor {field} {choices} {lookup} {onset} {onopen} {selected} {showIds} bind:error /></span>
      {#if pair && onpair}
        {@const both = pair}
        <CommandCard pair={both} onpick={(x, y) => onpair(both, x, y)} />
      {/if}
      {#if field.asset === "icon" || field.asset === "model"}
        <AssetPicker
          kind={field.asset}
          value={field.value}
          label={field.label}
          onpick={(path) => onset(field, path)}
          {objectKind}
          fieldId={field.id}
          suggested={kindIcons}
        />
      {/if}
      {#if field.changed}
        <!-- At the row's right end (Sven, 2026-10-29). -->
        <button
          type="button"
          class="ml-auto shrink-0 px-1 text-changed hover:text-fg"
          title={t("field.resetTitle", { value: standard === "" ? t("field.empty") : standard })}
          aria-label={t("field.resetLabel", { field: field.label })}
          onclick={reset}
        >
          ↺
        </button>
      {/if}
    {:else if isList && objects.length > 0}
      <span class="flex flex-wrap gap-1" {title}>
        {#each objects as object, index (index)}
          {#if object.found}
            {@const found = object.found}
            <button
              type="button"
              class="rounded border border-line bg-raised px-1.5 text-[12.5px] hover:border-accent {field.changed
                ? 'text-changed'
                : 'text-fg'}"
              onclick={() => onopen(found.kind, object.id)}
            >
              {found.name}
              <span class="font-mono text-faint">{object.id}</span>
            </button>
          {:else}
            <span class="rounded border border-line px-1.5 font-mono text-[12.5px] text-muted">{object.id}</span>
          {/if}
        {/each}
      </span>
    {:else}
      <span
        class="block max-w-full truncate rounded border px-1.5 py-[3px] font-mono text-[12.5px] {field.warning
          ? 'border-warn bg-warn-lo'
          : 'border-transparent'} {field.changed ? 'text-changed' : 'text-fg'}"
        {title}
      >
        {shown === "" ? " " : shown}
      </span>
    {/if}
  </span>
  {#if error}
    <span class="col-start-2 col-end-4 pr-2 pb-1.5 text-xs text-warn" role="alert">{error}</span>
  {:else if field.warning}
    <span class="col-start-2 col-end-4 pr-2 pb-1.5 text-xs text-warn">{field.warning}</span>
  {/if}
</div>
