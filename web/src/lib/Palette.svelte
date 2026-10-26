<script lang="ts">
/**
 * The search palette (Ctrl K): searches as the user types, ↑ and ↓ move, Enter opens the hit, Esc closes it. An
 * object hit opens the object; a field hit filters the details panel to that field.
 */
import { tick } from "svelte";
import { type Hit, search } from "./api.ts";

type Props = {
  onpick: (hit: Hit) => void;
  onclose: () => void;
};

let { onpick, onclose }: Props = $props();

const limit = 30;

let query = $state("");
let hits = $state.raw<Hit[]>([]);
let index = $state(0);
let problem = $state("");
let input: HTMLInputElement | undefined = $state();
let list: HTMLDivElement | undefined = $state();
// Only the newest search's answer is shown: one typed earlier can answer later.
let asked = 0;
let timer: ReturnType<typeof setTimeout> | undefined;

$effect(() => {
  input?.focus();
  return () => clearTimeout(timer);
});

function oninput(): void {
  clearTimeout(timer);
  timer = setTimeout(run, 60);
}

async function run(): Promise<void> {
  const mine = ++asked;
  const text = query.trim();
  if (text === "") {
    hits = [];
    problem = "";
    return;
  }
  try {
    const found = await search(text, limit);
    if (mine !== asked) return;
    hits = found;
    index = 0;
    problem = "";
  } catch (error) {
    if (mine !== asked) return;
    hits = [];
    problem = error instanceof Error ? error.message : String(error);
  }
}

async function move(by: number): Promise<void> {
  if (hits.length === 0) return;
  index = (index + by + hits.length) % hits.length;
  await tick();
  list?.querySelector(`[data-index="${index}"]`)?.scrollIntoView({ block: "nearest" });
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    move(event.key === "ArrowDown" ? 1 : -1);
  } else if (event.key === "Enter") {
    event.preventDefault();
    const hit = hits[index];
    if (hit) onpick(hit);
  } else if (event.key === "Escape") {
    event.preventDefault();
    event.stopPropagation();
    onclose();
  }
}
</script>

<!-- A click on the veil, outside the palette, closes it; the keyboard has Esc. -->
<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div
  class="fixed inset-0 z-10 flex items-start justify-center bg-black/55 pt-[90px]"
  onclick={(event) => {
    if (event.target === event.currentTarget) onclose();
  }}
>
  <div
    class="w-[640px] max-w-[calc(100%-32px)] overflow-hidden rounded-[10px] border border-line bg-surface shadow-[0_20px_60px_rgba(0,0,0,.5)]"
    role="dialog"
    aria-modal="true"
    aria-label="Search"
  >
    <input
      class="w-full border-0 border-b border-line bg-surface px-4 py-3.5 text-base text-fg outline-none"
      placeholder="Search objects, ids, fields, values…"
      aria-label="Search"
      autocomplete="off"
      spellcheck="false"
      bind:this={input}
      bind:value={query}
      {oninput}
      {onkeydown}
    />
    <div class="max-h-[50vh] overflow-auto" bind:this={list} role="listbox" aria-label="Hits">
      {#if problem}
        <p class="px-4 py-2 text-warn">{problem}</p>
      {:else if query.trim() !== "" && hits.length === 0}
        <p class="px-4 py-2 text-muted">Nothing found.</p>
      {/if}
      {#each hits as hit, at (`${hit.kind}:${hit.field ?? ""}:${hit.id}`)}
        <div
          class="flex cursor-pointer items-center gap-2.5 px-4 py-2 {at === index ? 'bg-hover' : 'hover:bg-hover'}"
          role="option"
          tabindex="-1"
          aria-selected={at === index}
          data-index={at}
          onclick={() => onpick(hit)}
          onkeydown={() => {}}
          onmousemove={() => (index = at)}
        >
          <span class="w-16 shrink-0 text-[11px] text-cyan">{hit.field ? "field" : hit.kind}</span>
          <span class="min-w-0 truncate">
            {hit.name}
            <span class="text-xs text-faint">{hit.field ? `${hit.kind} field` : hit.detail}</span>
          </span>
          <span class="ml-auto pl-2 font-mono text-[12.5px] text-faint">{hit.field ?? hit.id}</span>
        </div>
      {/each}
    </div>
    <div class="border-t border-line px-4 py-2 text-xs text-faint">
      ↑↓ to move · Enter to open · Esc to close
    </div>
  </div>
</div>
