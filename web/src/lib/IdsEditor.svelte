<script lang="ts">
/**
 * An object list (or a field that holds one object): its objects as chips with names (a click opens the object, ×
 * takes it out), and + to add one, which opens a picker that searches the objects of the list's kinds.
 */
import Plus from "@lucide/svelte/icons/plus";
import X from "@lucide/svelte/icons/x";
import { type FieldView, type Hit, type Kind, search } from "./api.ts";
import { type Lookup, listIds, listKinds } from "./fields.ts";
import Popover from "./Popover.svelte";
import { addId, removeId } from "./values.ts";

type Props = {
  field: FieldView;
  lookup: Lookup;
  compact: boolean;
  /** Sends a value; it ends once the app has answered (well or not). */
  onset: (value: string) => Promise<unknown>;
  onopen: (kind: Kind, id: string) => void;
};

let { field, lookup, compact, onset, onopen }: Props = $props();

// The search spans every kind, so it asks for many hits and keeps the list's kinds.
const limit = 200;
const shownHits = 12;

let open = $state(false);
let addButton: HTMLButtonElement | undefined = $state();
let query = $state("");
let hits = $state.raw<Hit[]>([]);
let index = $state(0);
let problem = $state("");
let asked = 0;
let timer: ReturnType<typeof setTimeout> | undefined;

const single = $derived(field.type === "objectRef");
const kinds = $derived(listKinds(field.listOf));
const objects = $derived(
  listIds(field.value).map((id) => ({ id, found: lookup(field.listOf ?? "", id) })),
);

function oninput(): void {
  clearTimeout(timer);
  timer = setTimeout(find, 60);
}

// Searches every kind, and keeps the objects of the list's kinds (never a field hit).
async function find(): Promise<void> {
  const mine = ++asked;
  const text = query.trim();
  if (text === "") {
    hits = [];
    return;
  }
  try {
    const found = await search(text, limit);
    if (mine !== asked) return;
    hits = found
      .filter((hit) => hit.field === null && kinds.includes(hit.kind))
      .slice(0, shownHits);
    index = 0;
    problem = "";
  } catch (error) {
    if (mine !== asked) return;
    hits = [];
    problem = error instanceof Error ? error.message : String(error);
  }
}

function close(): void {
  open = false;
  query = "";
  hits = [];
  clearTimeout(timer);
}

function add(hit: Hit): void {
  const value = addId(field.value, hit.id, single);
  close();
  addButton?.focus();
  if (value !== field.value) onset(value);
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    if (hits.length > 0) {
      index = (index + (event.key === "ArrowDown" ? 1 : hits.length - 1)) % hits.length;
    }
  } else if (event.key === "Enter") {
    event.preventDefault();
    const hit = hits[index];
    if (hit) add(hit);
  }
}

function focusOnMount(element: HTMLInputElement): void {
  element.focus();
}
</script>

<span class="flex min-w-0 flex-wrap items-center gap-1 {compact ? 'max-w-72' : ''}">
  {#each objects as object, at (at)}
    <span
      class="inline-flex items-center rounded border border-line bg-raised text-[12.5px] {field.changed
        ? 'text-changed'
        : 'text-fg'} {field.warning ? 'border-warn' : ''}"
    >
      {#if object.found}
        {@const found = object.found}
        <button
          type="button"
          class="rounded-l px-1.5 hover:text-accent-hi"
          title="Open {found.name}"
          onclick={() => onopen(found.kind, object.id)}
        >
          {found.name}
          <span class="font-mono text-faint">{object.id}</span>
        </button>
      {:else}
        <span class="px-1.5 font-mono text-muted">{object.id}</span>
      {/if}
      <button
        type="button"
        class="px-0.5 text-faint hover:text-warn"
        aria-label="Take {object.found?.name ?? object.id} out"
        onclick={() => onset(removeId(field.value, at))}
      >
        <X size={12} />
      </button>
    </span>
  {/each}
  <button
    type="button"
    class="inline-flex items-center rounded border border-dashed border-line px-1 py-px text-muted hover:border-accent hover:text-fg focus:border-accent focus:outline-none"
    aria-label={single ? `Choose the ${field.label}` : `Add to ${field.label}`}
    title={single ? "Choose…" : "Add…"}
    data-field="{field.id}:{field.level}"
    bind:this={addButton}
    onclick={() => (open ? close() : (open = true))}
  >
    <Plus size={13} />
  </button>
</span>
{#if open && addButton}
  <Popover anchor={addButton} onclose={close}>
    <input
      class="w-[300px] rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
      placeholder="Search by name or id…"
      aria-label="Search"
      bind:value={query}
      {oninput}
      {onkeydown}
      use:focusOnMount
    />
    {#if problem}
      <p class="m-0 px-2 py-1 text-warn">{problem}</p>
    {:else if query.trim() !== "" && hits.length === 0}
      <p class="m-0 px-2 py-1 text-muted">Nothing found.</p>
    {/if}
    <div class="mt-1 flex flex-col">
      {#each hits as hit, at (`${hit.kind}:${hit.id}`)}
        <button
          type="button"
          class="flex items-center gap-2 rounded px-2 py-1 text-left {at === index
            ? 'bg-hover text-fg'
            : 'hover:bg-hover'}"
          onclick={() => add(hit)}
        >
          <span class="truncate">{hit.name}</span>
          <span class="truncate text-xs text-faint">{hit.detail}</span>
          <span class="ml-auto pl-2 font-mono text-[12px] text-faint">{hit.id}</span>
        </button>
      {/each}
    </div>
  </Popover>
{/if}
