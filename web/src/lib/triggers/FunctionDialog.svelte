<script lang="ts">
/** Picks a function of a kind: by category and typed text, with its sentence and World Editor's hint below the list. */
import Dialog from "../Dialog.svelte";
import Select, { type SelectOption } from "../Select.svelte";
import { t } from "../text.ts";
import type { Catalog, CatalogFunction, FunctionKind } from "./api.ts";
import { functionsOf, functionTitle } from "./lists.ts";

type Props = {
  catalog: Catalog;
  kind: FunctionKind;
  title: string;
  /** The function shown first, when one is being changed. */
  current?: string;
  onpick: (name: string) => void;
  oncancel: () => void;
};

let { catalog, kind, title, current, onpick, oncancel }: Props = $props();

let category = $state("");
let text = $state("");
let chosen = $state<CatalogFunction | null>(null);

const options = $derived.by<SelectOption[]>(() => {
  const used = new Set(
    catalog.functions.filter((each) => each.kind === kind).map((each) => each.category),
  );
  return [
    { value: "", label: t("triggers.function.all") },
    ...catalog.categories
      .filter((each) => used.has(each.key))
      .map((each) => ({ value: each.key, label: each.title })),
  ];
});
const functions = $derived(
  functionsOf(catalog, kind, category, text).toSorted((one, other) =>
    functionTitle(catalog, one).localeCompare(functionTitle(catalog, other)),
  ),
);
const sentence = $derived(
  chosen?.sentence.map((part) => (part.argument >= 0 ? `[${part.text}]` : part.text)).join("") ??
    "",
);

$effect(() => {
  if (chosen === null && current) {
    chosen = functions.find((each) => each.name === current) ?? null;
  }
});
</script>

<Dialog {title} {oncancel} large>
  <div class="flex flex-col gap-2">
    <div class="flex items-center gap-2">
      <Select
        {options}
        value={category}
        label={t("triggers.function.category")}
        onpick={(value) => (category = value)}
        snug
      />
      <input
        class="w-0 min-w-0 flex-1 rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
        placeholder={t("triggers.function.filter")}
        aria-label={t("triggers.function.filter")}
        bind:value={text}
      />
    </div>
    <div class="h-[340px] overflow-auto rounded-[5px] border border-line" role="listbox" aria-label={title}>
      {#each functions as each (each.name)}
        <button
          type="button"
          role="option"
          aria-selected={chosen?.name === each.name}
          class="block w-full px-2 py-[3px] text-left {chosen?.name === each.name
            ? 'bg-accent-lo text-fg'
            : 'text-body hover:bg-hover'}"
          onclick={() => (chosen = each)}
          ondblclick={() => onpick(each.name)}
        >
          {functionTitle(catalog, each)}
        </button>
      {:else}
        <p class="p-2 text-muted">{t("triggers.function.none")}</p>
      {/each}
    </div>
    <div class="min-h-[52px]">
      <p class="m-0 text-fg">{sentence}</p>
      {#if chosen?.hint}<p class="m-0 mt-1 text-muted">{chosen.hint}</p>{/if}
    </div>
  </div>
  {#snippet actions()}
    <button
      type="button"
      class="rounded-md border border-line bg-raised px-3 py-1.5 text-fg hover:bg-hover"
      onclick={oncancel}
    >
      {t("triggers.close")}
    </button>
    <button
      type="button"
      data-primary
      class="rounded-md border border-accent bg-raised px-3 py-1.5 text-fg hover:bg-hover disabled:opacity-50"
      disabled={chosen === null}
      onclick={() => chosen && onpick(chosen.name)}
    >
      {t("triggers.add")}
    </button>
  {/snippet}
</Dialog>
