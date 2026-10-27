<script lang="ts">
/**
 * The right panel (Sven, 2026-10-27): Uses, every object this object's fields name, and Used by, every object whose
 * fields name this one. A click opens the object; the fields that name it show as its tooltip. Each section opens and
 * closes, and stays so from object to object; the tooltip preview comes in a later version.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import { type ObjectView, type Reference, usedBy } from "./api.ts";
import { type Lookup, uses } from "./fields.ts";
import { kindNames, kindText } from "./tree.ts";

type Props = {
  view: ObjectView;
  lookup: Lookup;
  onopen: (kind: Reference["kind"], id: string) => void;
};

let { view, lookup, onopen }: Props = $props();

const used = $derived(uses(view, lookup));

// The sections the user closed.
let closed = $state<Record<string, boolean>>({});

// Used by is asked for again with each new view of the object (another object, or the same one after an edit).
let users = $state.raw<Reference[] | null>(null);
let problem = $state("");
$effect(() => {
  const asked = view;
  users = null;
  problem = "";
  usedBy(asked.kind, asked.id).then(
    (found) => {
      if (asked === view) users = found;
    },
    (error) => {
      if (asked === view) problem = error instanceof Error ? error.message : String(error);
    },
  );
});
</script>

{#snippet list(items: { kind: Reference["kind"]; id: string; name: string; fields: string[] }[])}
  {#each items as item (`${item.kind}:${item.id}`)}
    <button
      type="button"
      class="mb-1 flex w-full items-center gap-2 rounded-md border border-line bg-raised px-2 py-1 text-left hover:border-accent"
      title="{kindNames[item.kind]} · {item.fields.join(', ')}"
      onclick={() => onopen(item.kind, item.id)}
    >
      <span class="truncate {item.name ? kindText[item.kind] : 'text-muted'}">{item.name || "Unknown"}</span>
      <span class="ml-auto shrink-0 font-mono text-[12.5px] text-faint">{item.id}</span>
    </button>
  {/each}
{/snippet}

{#snippet heading(key: string, title: string, count: number | null, top: string)}
  <h3 class="{top} mb-2">
    <button
      type="button"
      class="flex w-full items-center gap-1.5 text-left text-[13px] tracking-[.06em] text-muted uppercase hover:text-fg"
      aria-expanded={!closed[key]}
      onclick={() => (closed[key] = !closed[key])}
    >
      <span class="flex w-3 shrink-0 items-center text-faint">
        {#if closed[key]}<ChevronRight size={12} />{:else}<ChevronDown size={12} />{/if}
      </span>
      {title}
      {#if count !== null}
        <span class="font-mono text-[11px] tracking-normal text-faint">{count}</span>
      {/if}
    </button>
  </h3>
{/snippet}

{@render heading("uses", "Uses", used.length, "mt-1")}
{#if closed.uses}
  <!-- Closed. -->
{:else if used.length === 0}
  <p class="text-[12.5px] text-faint">Its fields name no other object.</p>
{:else}
  {@render list(used)}
{/if}

{@render heading("usedBy", "Used by", users?.length ?? null, "mt-5")}
{#if closed.usedBy}
  <!-- Closed. -->
{:else if problem}
  <p class="text-[12.5px] text-warn">{problem}</p>
{:else if users === null}
  <p class="text-[12.5px] text-faint">Looking…</p>
{:else if users.length === 0}
  <p class="text-[12.5px] text-faint">No object's fields name it.</p>
{:else}
  {@render list(users)}
{/if}

{@render heading("tooltip", "Tooltip preview", null, "mt-5")}
{#if !closed.tooltip}
  <p class="rounded-md border border-dashed border-line px-2 py-1.5 text-[12.5px] text-faint">
    Coming in a later version.
  </p>
{/if}
