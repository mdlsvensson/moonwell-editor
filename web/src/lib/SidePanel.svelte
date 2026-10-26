<script lang="ts">
/**
 * The right panel: the object's components (the abilities its ability lists hold, which open on a click), and the
 * places for "Used by" and the tooltip preview, which come in a later version.
 */
import type { Kind, ObjectView } from "./api.ts";
import { abilityIds, type Lookup } from "./fields.ts";

type Props = {
  view: ObjectView;
  lookup: Lookup;
  onopen: (kind: Kind, id: string) => void;
};

let { view, lookup, onopen }: Props = $props();

const hasAbilityLists = $derived(view.fields.some((field) => field.listOf === "Ability"));
const abilities = $derived(
  abilityIds(view).map((id) => ({ id, name: lookup("Ability", id)?.name ?? "" })),
);
</script>

<h3 class="mt-1 mb-2 text-[13px] tracking-[.06em] text-muted uppercase">Components</h3>
{#if !hasAbilityLists}
  <p class="text-[12.5px] text-faint">This kind of object has no abilities.</p>
{:else if abilities.length === 0}
  <p class="text-[12.5px] text-faint">No abilities.</p>
{:else}
  {#each abilities as ability (ability.id)}
    <button
      type="button"
      class="mb-1.5 flex w-full items-center gap-2 rounded-md border border-line bg-raised px-2 py-1.5 text-left hover:border-accent"
      onclick={() => onopen("ability", ability.id)}
    >
      <span class="truncate {ability.name ? '' : 'text-muted'}">{ability.name || "Unknown ability"}</span>
      <span class="text-[11px] text-cyan">ability</span>
      <span class="ml-auto font-mono text-[12.5px] text-faint">{ability.id}</span>
    </button>
  {/each}
{/if}

<h3 class="mt-4 mb-2 text-[13px] tracking-[.06em] text-muted uppercase">Used by</h3>
<p class="rounded-md border border-dashed border-line px-2 py-1.5 text-[12.5px] text-faint">
  Coming in a later version.
</p>

<h3 class="mt-4 mb-2 text-[13px] tracking-[.06em] text-muted uppercase">Tooltip preview</h3>
<p class="rounded-md border border-dashed border-line px-2 py-1.5 text-[12.5px] text-faint">
  Coming in a later version.
</p>
