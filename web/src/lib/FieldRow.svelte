<script lang="ts">
/**
 * One row of the details panel for a field with no levels: the changed bar, the label, the value (its editor, when
 * the object can be edited) and ↺ to put a changed field back to its standard value.
 */
import type { ChoiceList, FieldView, Kind } from "./api.ts";
import FieldEditor from "./FieldEditor.svelte";
import { type Lookup, listIds, shownValue } from "./fields.ts";

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
};

let { field, showIds, choices, lookup, editable, onset, onreset, onopen }: Props = $props();

let error = $state("");

const nameOf = (listOf: string, id: string) => lookup(listOf, id)?.name;
const shown = $derived(shownValue(field, choices, nameOf));
const isList = $derived(field.type === "objectList" || field.type === "objectRef");
const objects = $derived(
  isList ? listIds(field.value).map((id) => ({ id, found: lookup(field.listOf ?? "", id) })) : [],
);
const standard = $derived(shownValue({ ...field, value: field.base }, choices, nameOf));
const title = $derived(titleOf(field));

function titleOf(view: FieldView): string {
  const lines = [shown];
  if (view.changed) lines.push(`Standard: ${standard}`);
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
</script>

<div
  class="grid min-h-[30px] grid-cols-[22px_minmax(170px,1fr)_minmax(120px,260px)_24px] items-center border-t border-line hover:bg-surface"
>
  <span class="h-[18px] w-1 justify-self-center rounded-sm {field.changed ? 'bg-changed' : ''}"></span>
  <span class="py-1 pr-2" title={editable && field.modes ? title : undefined}>
    {field.label}
    {#if showIds}<span class="ml-1.5 font-mono text-[12.5px] text-faint">{field.id}</span>{/if}
  </span>
  <span class="min-w-0 px-2 py-[3px]">
    {#if editable}
      <FieldEditor {field} {choices} {lookup} {onset} {onopen} bind:error />
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
        class="block truncate rounded border px-1.5 py-[3px] font-mono text-[12.5px] {field.warning
          ? 'border-warn bg-warn-lo'
          : 'border-transparent'} {field.changed ? 'text-changed' : 'text-fg'}"
        {title}
      >
        {shown === "" ? " " : shown}
      </span>
    {/if}
  </span>
  {#if editable && field.changed}
    <button
      type="button"
      class="justify-self-center text-changed hover:text-fg"
      title="Back to the standard value: {standard === '' ? '(empty)' : standard}"
      aria-label="Reset {field.label}"
      onclick={reset}
    >
      ↺
    </button>
  {:else}
    <span></span>
  {/if}
  {#if error}
    <span class="col-start-2 col-end-5 pr-2 pb-1.5 text-xs text-warn" role="alert">{error}</span>
  {:else if field.warning}
    <span class="col-start-2 col-end-5 pr-2 pb-1.5 text-xs text-warn">{field.warning}</span>
  {/if}
</div>
