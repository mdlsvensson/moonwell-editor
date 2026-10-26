<script lang="ts">
/**
 * The left pane: the kind buttons, the tree filter and a kind's tree. The tree can hold thousands of objects, so only
 * the rows in view are drawn (every row is the same height, so the row at any scroll position is a division away).
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import { tick, untrack } from "svelte";
import { type Kind, kinds, type TreeEntry } from "./api.ts";

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

// The data names races in lower case (`nightelf`); the tree shows them as World Editor does.
const groupNames: Record<string, string> = { nightelf: "Night Elf" };

function groupName(name: string): string {
  return groupNames[name] ?? name.charAt(0).toUpperCase() + name.slice(1);
}

/** One drawn row: a heading, a group that opens and closes, or an object. */
type Line =
  | { type: "heading"; key: string; text: string }
  | { type: "group"; key: string; name: string; depth: number; open: boolean; count: number }
  | { type: "entry"; key: string; entry: TreeEntry; depth: number };

type Group = {
  name: string;
  key: string;
  groups: Map<string, Group>;
  entries: TreeEntry[];
  count: number;
};

const rowHeight = 26;
const overscan = 12;

let filter = $state("");
// Groups the user opened or closed, by `kind:path`; any other is closed, except Custom, which starts open.
let toggled = $state<Record<string, boolean>>({});
let scroller: HTMLDivElement | undefined = $state();
let scrollTop = $state(0);
let height = $state(0);

const customKey = $derived(`${kind}:Custom`);

function isOpen(key: string): boolean {
  return toggled[key] ?? key === customKey;
}

function groupKey(path: string[], depth: number): string {
  return `${kind}:${path.slice(0, depth + 1).join("/")}`;
}

function isCustom(entry: TreeEntry): boolean {
  return entry.path.length === 1 && entry.path[0] === "Custom";
}

function matches(entry: TreeEntry, needle: string): boolean {
  return (
    entry.id.toLowerCase().includes(needle) ||
    entry.name.toLowerCase().includes(needle) ||
    entry.suffix.toLowerCase().includes(needle)
  );
}

const lines = $derived.by((): Line[] => {
  if (!entries) return [];
  const needle = filter.trim().toLowerCase();
  const shown = needle ? entries.filter((entry) => matches(entry, needle)) : entries;
  // While filtering every group is open, so each match can be seen.
  const open = (key: string) => needle !== "" || isOpen(key);
  const result: Line[] = [];

  const custom = shown.filter(isCustom);
  if (custom.length > 0 || !needle) {
    const key = customKey;
    result.push({
      type: "group",
      key,
      name: "Custom · this map",
      depth: -1,
      open: open(key),
      count: custom.length,
    });
    if (open(key)) {
      for (const entry of custom)
        result.push({ type: "entry", key: `e:${entry.id}`, entry, depth: 0 });
    }
  }

  const root: Group = { name: "", key: "", groups: new Map(), entries: [], count: 0 };
  for (const entry of shown) {
    if (isCustom(entry)) continue;
    let group = root;
    group.count++;
    entry.path.forEach((name, depth) => {
      let child = group.groups.get(name);
      if (!child) {
        child = {
          name,
          key: groupKey(entry.path, depth),
          groups: new Map(),
          entries: [],
          count: 0,
        };
        group.groups.set(name, child);
      }
      child.count++;
      group = child;
    });
    group.entries.push(entry);
  }
  if (root.count > 0) result.push({ type: "heading", key: "h:standard", text: "Standard" });

  const walk = (group: Group, depth: number) => {
    for (const child of group.groups.values()) {
      const childOpen = open(child.key);
      result.push({
        type: "group",
        key: child.key,
        name: child.name,
        depth,
        open: childOpen,
        count: child.count,
      });
      if (childOpen) walk(child, depth + 1);
    }
    for (const entry of group.entries) {
      result.push({ type: "entry", key: `e:${entry.id}`, entry, depth });
    }
  };
  walk(root, 0);
  return result;
});

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
  if (isCustom(entry)) {
    if (!isOpen(customKey)) toggled[customKey] = true;
  } else {
    entry.path.forEach((_, depth) => {
      const key = groupKey(entry.path, depth);
      if (!isOpen(key)) toggled[key] = true;
    });
  }
  await tick();
  const index = lines.findIndex((line) => line.type === "entry" && line.entry.id === entry.id);
  if (index < 0 || !scroller) return;
  const top = index * rowHeight;
  if (top < scroller.scrollTop || top + rowHeight > scroller.scrollTop + scroller.clientHeight) {
    scroller.scrollTop = Math.max(0, top - scroller.clientHeight / 2);
  }
}

// ↑ and ↓ move the selection to the object above or below.
function onkeydown(event: KeyboardEvent): void {
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
  const objects = lines.filter((line) => line.type === "entry");
  const at = objects.findIndex((line) => line.entry.id === selected);
  const next = objects[event.key === "ArrowDown" ? at + 1 : Math.max(0, at - 1)];
  if (next) {
    event.preventDefault();
    onselect(next.entry.id);
  }
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
              onclick={() => toggle(line.key)}
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
              onclick={() => toggle(line.key)}
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
              aria-selected={entry.id === selected}
              class="flex w-full items-center gap-1.5 pr-2.5 text-left whitespace-nowrap {entry.id ===
              selected
                ? 'bg-accent/16 text-fg'
                : 'hover:bg-hover'}"
              style:height="{rowHeight}px"
              style:padding-left="{10 + line.depth * 16}px"
              onclick={() => onselect(entry.id)}
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
