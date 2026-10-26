<script lang="ts">
/**
 * The left pane: the kind buttons, the tree filter and a kind's tree. The tree can hold thousands of objects, so only
 * the rows in view are drawn (every row is the same height, so the row at any scroll position is a division away).
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import { tick, untrack } from "svelte";
import { type Kind, kinds, type TreeEntry } from "./api.ts";
import { customKey, groupKeys, groupName, nextObject, treeLines } from "./tree.ts";

type Props = {
  kind: Kind;
  /** The kind's tree; null while it loads. */
  entries: TreeEntry[] | null;
  /** Why the tree could not be loaded; "" when it could. */
  error: string;
  selected: string | null;
  onkind: (kind: Kind) => void;
  onselect: (id: string) => void;
};

let { kind, entries, error, selected, onkind, onselect }: Props = $props();

const kindNames: Record<Kind, string> = {
  unit: "Units",
  item: "Items",
  destructable: "Destructables",
  doodad: "Doodads",
  ability: "Abilities",
  buff: "Buffs",
  upgrade: "Upgrades",
};

const rowHeight = 26;
const overscan = 12;

let filter = $state("");
// Groups the user opened or closed, by `kind:path`; any other is closed, except Custom, which starts open.
let toggled = $state<Record<string, boolean>>({});
let scroller: HTMLDivElement | undefined = $state();
let scrollTop = $state(0);
let height = $state(0);

function isOpen(key: string): boolean {
  return toggled[key] ?? key === customKey(kind);
}

const lines = $derived(entries ? treeLines(entries, kind, filter, isOpen) : []);

const first = $derived(Math.max(0, Math.floor(scrollTop / rowHeight) - overscan));
const last = $derived(
  Math.min(lines.length, Math.ceil((scrollTop + height) / rowHeight) + overscan),
);
const visible = $derived(lines.slice(first, last));

function toggle(key: string): void {
  toggled[key] = !isOpen(key);
}

// Opens the groups an object is in and scrolls it into view, when it was picked elsewhere (the palette, history, a
// component). Only a change of the selection or of the tree runs this, so closing a group does not open it again.
$effect(() => {
  const id = selected;
  const list = entries;
  untrack(() => reveal(id, list));
});

async function reveal(id: string | null, list: TreeEntry[] | null): Promise<void> {
  const entry = id && list ? list.find((candidate) => candidate.id === id) : undefined;
  if (!entry) return;
  for (const key of groupKeys(kind, entry)) {
    if (!isOpen(key)) toggled[key] = true;
  }
  await tick();
  const index = lines.findIndex((line) => line.type === "entry" && line.entry.id === entry.id);
  if (index < 0 || !scroller) return;
  const top = index * rowHeight;
  if (top < scroller.scrollTop || top + rowHeight > scroller.scrollTop + scroller.clientHeight) {
    scroller.scrollTop = Math.max(0, top - scroller.clientHeight / 2);
  }
}

// The keys go to the scroller, never to a row: a row scrolled out of view is taken out of the page, and focus on it
// would fall to the page's body, where ↑ and ↓ no longer reach the tree.
function focusTree(): void {
  scroller?.focus({ preventScroll: true });
}

// ↑ and ↓ move the selection to the object above or below.
function onkeydown(event: KeyboardEvent): void {
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
  event.preventDefault();
  focusTree();
  const next = nextObject(lines, selected, event.key === "ArrowDown" ? 1 : -1);
  if (next) onselect(next);
}

function clearOnEscape(event: KeyboardEvent): void {
  if (event.key === "Escape" && filter) {
    event.stopPropagation();
    filter = "";
  }
}
</script>

<div class="flex h-full min-h-0 flex-col">
  <div class="flex flex-wrap gap-1 border-b border-line p-2.5" role="group" aria-label="Kinds">
    {#each kinds as each (each)}
      <button
        type="button"
        class="rounded-[5px] border bg-raised px-2 py-0.5 text-[12.5px] {each === kind
          ? 'border-accent text-fg'
          : 'border-line text-muted hover:text-body'}"
        aria-pressed={each === kind}
        onclick={() => onkind(each)}
      >
        {kindNames[each]}
      </button>
    {/each}
  </div>
  <div class="px-2.5 py-2">
    <input
      class="w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
      placeholder="Filter the tree (name or id)…"
      aria-label="Filter the tree"
      bind:value={filter}
      onkeydown={clearOnEscape}
    />
  </div>

  {#if error}
    <p class="px-2.5 text-warn">{error}</p>
  {:else if !entries}
    <p class="px-2.5 text-muted">Loading the tree…</p>
  {:else if lines.length === 0}
    <p class="px-2.5 text-muted">Nothing matches “{filter}”.</p>
  {/if}

  <div
    class="min-h-0 flex-1 overflow-auto outline-none"
    role="tree"
    aria-label="{kindNames[kind]} tree"
    tabindex="0"
    bind:this={scroller}
    bind:clientHeight={height}
    onscroll={() => (scrollTop = scroller?.scrollTop ?? 0)}
    {onkeydown}
  >
    <div class="relative" style:height="{lines.length * rowHeight}px">
      <div class="absolute inset-x-0 top-0" style:transform="translateY({first * rowHeight}px)">
        {#each visible as line (line.key)}
          {#if line.type === "heading"}
            <div
              class="flex items-end px-2.5 pb-1 text-xs tracking-[.06em] text-muted uppercase"
              style:height="{rowHeight}px"
            >
              {line.text}
            </div>
          {:else if line.type === "group" && line.depth < 0}
            <button
              type="button"
              class="flex w-full items-end gap-1 px-2.5 pb-1 text-left text-xs tracking-[.06em] text-muted uppercase hover:text-body"
              style:height="{rowHeight}px"
              aria-expanded={line.open}
              onclick={() => {
                toggle(line.key);
                focusTree();
              }}
            >
              {#if line.open}<ChevronDown size={12} />{:else}<ChevronRight size={12} />{/if}
              {line.name}
              <span class="ml-auto font-mono text-[11px] text-faint normal-case">{line.count}</span>
            </button>
          {:else if line.type === "group"}
            <button
              type="button"
              class="flex w-full items-center gap-1.5 pr-2.5 text-left whitespace-nowrap hover:bg-hover"
              style:height="{rowHeight}px"
              style:padding-left="{10 + line.depth * 16}px"
              aria-expanded={line.open}
              onclick={() => {
                toggle(line.key);
                focusTree();
              }}
            >
              <span class="w-3 text-faint">
                {#if line.open}<ChevronDown size={12} />{:else}<ChevronRight size={12} />{/if}
              </span>
              {groupName(line.name)}
              <span class="ml-auto font-mono text-[11px] text-faint">{line.count}</span>
            </button>
          {:else if line.type === "entry"}
            {@const entry = line.entry}
            <button
              type="button"
              role="treeitem"
              tabindex="-1"
              aria-selected={entry.id === selected}
              class="flex w-full items-center gap-1.5 pr-2.5 text-left whitespace-nowrap {entry.id ===
              selected
                ? 'bg-accent/16 text-fg'
                : 'hover:bg-hover'}"
              style:height="{rowHeight}px"
              style:padding-left="{10 + line.depth * 16}px"
              onclick={() => {
                focusTree();
                onselect(entry.id);
              }}
            >
              <span class="w-3 shrink-0"></span>
              <span class="truncate {entry.baseId ? 'text-accent-hi' : ''}">
                {entry.name}
                {#if entry.suffix}<span class="text-muted">{entry.suffix}</span>{/if}
              </span>
              {#if entry.changed && !entry.baseId}
                <span
                  class="size-1.5 shrink-0 rounded-full bg-changed"
                  title="The map changes this standard object"
                ></span>
              {/if}
              <span class="ml-auto pl-2 font-mono text-[12.5px] text-faint">{entry.id}</span>
            </button>
          {/if}
        {/each}
      </div>
    </div>
  </div>
</div>
