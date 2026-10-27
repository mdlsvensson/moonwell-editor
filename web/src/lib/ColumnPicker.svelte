<script lang="ts">
/**
 * The column picker: searches the kind's fields by label, id and category and adds one as a column. A per-level field
 * is added at level 1, or at every level up to the most any shown row has (at most 20; `note` says so when it stopped
 * there). A column already shown is not added again.
 * Enter adds the first field found (at level 1); Esc closes the picker.
 */
import type { Column, FieldInfo } from "./api.ts";
import { categoryName } from "./fields.ts";
import Popover from "./Popover.svelte";
import { columnKey, searchFields } from "./table.ts";
import { t } from "./text.ts";

type Props = {
  anchor: HTMLElement;
  fields: FieldInfo[];
  columns: Column[];
  /** A line to show under the search, such as where "All levels" stopped; "" for none. */
  note?: string;
  /** Adds a field: `all` for every level of a per-level field, else level 1 (or the field, with no levels). */
  onadd: (field: FieldInfo, all: boolean) => void;
  onclose: () => void;
};

let { anchor, fields, columns, note = "", onadd, onclose }: Props = $props();

let query = $state("");
let input: HTMLInputElement | undefined = $state();

const found = $derived(searchFields(fields, query));
const shown = $derived(new Set(columns.map(columnKey)));

function isShown(field: FieldInfo): boolean {
  return shown.has(columnKey({ field: field.id, level: field.perLevel ? 1 : 0 }));
}

$effect(() => {
  input?.focus();
});

function onkeydown(event: KeyboardEvent): void {
  if (event.key !== "Enter") return;
  event.preventDefault();
  const first = found[0];
  if (first && !isShown(first)) onadd(first, false);
}

const small =
  "shrink-0 rounded border border-line bg-raised px-1.5 text-xs text-body hover:border-accent hover:text-fg disabled:opacity-40 disabled:hover:border-line";
</script>

<Popover {anchor} {onclose}>
  <div class="flex w-[340px] flex-col gap-1">
    <input
      class="w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
      placeholder={t("columnPicker.placeholder")}
      aria-label={t("columnPicker.label")}
      bind:this={input}
      bind:value={query}
      {onkeydown}
    />
    {#if note}
      <p class="m-0 px-1 text-[12px] text-changed" role="status">{note}</p>
    {/if}
    {#if found.length === 0}
      <p class="m-0 px-1 py-1 text-muted">{t("columnPicker.noMatch")}</p>
    {/if}
    <ul class="m-0 list-none p-0" aria-label={t("columnPicker.fields")}>
      {#each found as field (field.id)}
        {@const added = isShown(field)}
        <li class="flex items-center gap-1.5 rounded px-1 py-0.5 hover:bg-hover">
          <span class="min-w-0 flex-1 truncate {added ? 'text-faint' : 'text-fg'}" title={field.label}>
            {field.label}
            <span class="ml-1 font-mono text-[11.5px] text-faint">{field.id}</span>
          </span>
          <span class="shrink-0 text-[11.5px] text-faint">{categoryName(field.category)}</span>
          {#if field.perLevel}
            <button
              type="button"
              class={small}
              disabled={added}
              title={added ? t("columnPicker.levelOneShown") : t("columnPicker.addLevelOne")}
              onclick={() => onadd(field, false)}
            >
              {t("columnPicker.levelOne")}
            </button>
            <button
              type="button"
              class={small}
              title={t("columnPicker.allLevelsTitle")}
              onclick={() => onadd(field, true)}
            >
              {t("columnPicker.allLevels")}
            </button>
          {:else}
            <button
              type="button"
              class={small}
              disabled={added}
              title={added ? t("columnPicker.alreadyShown") : t("columnPicker.addTitle")}
              onclick={() => onadd(field, false)}
            >
              {added ? t("columnPicker.shown") : t("columnPicker.add")}
            </button>
          {/if}
        </li>
      {/each}
    </ul>
  </div>
</Popover>
