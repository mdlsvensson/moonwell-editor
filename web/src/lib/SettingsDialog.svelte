<script lang="ts">
/**
 * The editor's settings (Sven, 2026-10-27), in tabs: General (the graphics mode and the theme, which show at once and
 * are remembered), Hotkeys (each action's keys, which a click and a key press change), Map (what the editor knows
 * about the open map: its name, its game data option and the tables the game reads under it) and Game data.
 */
import Check from "@lucide/svelte/icons/check";
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import RotateCcw from "@lucide/svelte/icons/rotate-ccw";
import { type Kind, kinds, type MapInfo } from "./api.ts";
import Dialog from "./Dialog.svelte";
import type { GameDataState } from "./gameData.ts";
import { graphics, graphicsModes, setGraphicsMode } from "./graphics.svelte.ts";
import {
  type Action,
  actionList,
  clashes,
  defaultKeymap,
  type Keymap,
  keyOf,
  saveKeymap,
  shownKey,
} from "./keymap.ts";
import Popover from "./Popover.svelte";
import { type Theme, themes } from "./themes.ts";
import { kindNames } from "./tree.ts";

type Props = {
  theme: Theme;
  ontheme: (theme: Theme) => void;
  keymap: Keymap;
  onkeymap: (keymap: Keymap) => void;
  map: MapInfo | null;
  data: GameDataState | null;
  onclose: () => void;
};

let { theme, ontheme, keymap, onkeymap, map, data, onclose }: Props = $props();

type Tab = "general" | "hotkeys" | "map" | "data";
const tabs: { tab: Tab; name: string }[] = [
  { tab: "general", name: "General" },
  { tab: "hotkeys", name: "Hotkeys" },
  { tab: "map", name: "Map" },
  { tab: "data", name: "Game data" },
];
let tab = $state<Tab>("general");

const tableNames: Record<string, string> = {
  base: "the base tables",
  custom_v0: "custom_v0",
  custom_v1: "custom_v1",
  melee_v0: "melee_v0",
};

const swatch = [
  "bg",
  "surface",
  "accent",
  "changed",
  "kind-unit",
  "kind-item",
  "kind-ability",
] as const;
const customKinds = $derived(map ? kinds.filter((kind: Kind) => (map.counts[kind] ?? 0) > 0) : []);

// The theme list, open under its button.
let themesOpen = $state(false);
let themeButton: HTMLButtonElement | undefined = $state();

// The action whose new key is being pressed.
let listening = $state<Action | null>(null);

function setKeys(action: Action, keys: string[]): void {
  const next = { ...keymap, [action]: keys };
  saveKeymap(next);
  onkeymap(next);
}

// The next key pressed (not a modifier alone) replaces the action's keys; Esc stops listening.
function listen(event: KeyboardEvent): void {
  if (!listening) return;
  event.preventDefault();
  event.stopPropagation();
  if (event.key === "Escape") {
    listening = null;
    return;
  }
  const key = keyOf(event);
  if (!key) return;
  setKeys(listening, [key]);
  listening = null;
}

function resetAll(): void {
  saveKeymap(defaultKeymap());
  onkeymap(defaultKeymap());
}
</script>

<svelte:window onkeydowncapture={listen} />

{#snippet row(label: string, value: string, mono = false)}
  <div class="flex gap-3 py-0.5">
    <span class="w-32 shrink-0 text-muted">{label}</span>
    <span class="min-w-0 break-words {mono ? 'font-mono text-[12.5px]' : ''} text-fg">{value}</span>
  </div>
{/snippet}

{#snippet palette(each: Theme)}
  <span class="flex shrink-0 overflow-hidden rounded-sm border border-black/30">
    {#each swatch as token (token)}
      <span class="h-4 w-2.5" style:background={each.colors[token]}></span>
    {/each}
  </span>
{/snippet}

{#snippet heading(text: string)}
  <h3 class="m-0 mt-4 mb-2 text-[12px] tracking-[.06em] text-muted uppercase first:mt-1">{text}</h3>
{/snippet}

<Dialog title="Settings" oncancel={() => (listening ? (listening = null) : onclose())} wide>
  <div class="mb-3 flex gap-1 border-b border-line" role="tablist">
    {#each tabs as each (each.tab)}
      <button
        type="button"
        role="tab"
        aria-selected={tab === each.tab}
        class="-mb-px border-b-2 px-3 py-1.5 {tab === each.tab
          ? 'border-accent text-fg'
          : 'border-transparent text-muted hover:text-fg'}"
        onclick={() => (tab = each.tab)}
      >
        {each.name}
      </button>
    {/each}
  </div>

  <div class="min-h-[300px]">
    {#if tab === "general"}
      {@render heading("Graphics mode")}
      <p class="m-0 mb-2 text-[12.5px] text-faint">
        As World Editor's: the icons, the models and the values the game keeps for each mode.
      </p>
      <div class="flex gap-1" role="radiogroup" aria-label="Graphics mode">
        {#each graphicsModes as each (each.mode)}
          <button
            type="button"
            role="radio"
            aria-checked={graphics.mode === each.mode}
            class="rounded-md border px-3 py-1 {graphics.mode === each.mode
              ? 'border-accent bg-accent-lo/40 text-fg'
              : 'border-line text-body hover:border-muted'}"
            onclick={() => setGraphicsMode(each.mode)}
          >
            {each.name}
          </button>
        {/each}
      </div>

      {@render heading("Theme")}
      <button
        type="button"
        class="flex w-72 items-center gap-2 rounded-md border border-line bg-bg px-2 py-1.5 text-left hover:border-muted"
        aria-haspopup="listbox"
        aria-expanded={themesOpen}
        bind:this={themeButton}
        onclick={() => (themesOpen = !themesOpen)}
      >
        {@render palette(theme)}
        <span class="min-w-0 flex-1 truncate text-fg">{theme.name}</span>
        <ChevronDown size={14} class="text-faint" />
      </button>
      {#if themesOpen && themeButton}
        <Popover anchor={themeButton} onclose={() => (themesOpen = false)} large>
          <div class="flex w-[272px] flex-col" role="listbox" aria-label="Theme">
            {#each themes as each (each.id)}
              <button
                type="button"
                role="option"
                aria-selected={each.id === theme.id}
                class="flex items-center gap-2 rounded px-2 py-1 text-left hover:bg-hover"
                onclick={() => {
                  ontheme(each);
                  themesOpen = false;
                }}
              >
                {@render palette(each)}
                <span class="min-w-0 flex-1 truncate {each.id === theme.id ? 'text-fg' : ''}">{each.name}</span>
                {#if each.id === theme.id}<Check size={14} class="shrink-0 text-accent-hi" />{/if}
              </button>
            {/each}
          </div>
        </Popover>
      {/if}
    {:else if tab === "hotkeys"}
      <p class="m-0 mb-2 text-[12.5px] text-faint">
        Click a key to change it, then press the new one (Esc keeps the old one).
      </p>
      <div class="flex flex-col">
        {#each actionList as each (each.action)}
          {@const keys = keymap[each.action]}
          {@const changed = keys.join("|") !== each.keys.join("|")}
          {@const clash = keys.flatMap((key) => clashes(keymap, each.action, key))}
          <div class="flex items-center gap-2 border-t border-line py-1 first:border-t-0">
            <span class="min-w-0 flex-1 truncate {changed ? 'text-changed' : 'text-body'}">{each.name}</span>
            {#if clash.length > 0}
              <span class="text-[11.5px] text-warn" title="The same key is set for another action">
                also {actionList.find((other) => other.action === clash[0])?.name}
              </span>
            {/if}
            <button
              type="button"
              class="min-w-28 rounded border px-2 py-0.5 text-right font-mono text-[12px] {listening === each.action
                ? 'border-accent text-accent-hi'
                : 'border-line text-fg hover:border-muted'}"
              onclick={() => (listening = listening === each.action ? null : each.action)}
            >
              {listening === each.action ? "Press a key…" : keys.map(shownKey).join(", ") || "None"}
            </button>
            <button
              type="button"
              class="rounded p-1 text-faint hover:text-fg disabled:opacity-30"
              title="Back to {each.keys.map(shownKey).join(', ')}"
              aria-label="Reset {each.name}"
              disabled={!changed}
              onclick={() => setKeys(each.action, [...each.keys])}
            >
              <RotateCcw size={13} />
            </button>
          </div>
        {/each}
      </div>
      <button type="button" class="mt-3 text-[12.5px] text-muted hover:text-fg" onclick={resetAll}>
        Reset every hotkey
      </button>
    {:else if tab === "map"}
      {#if map}
        {@render row("Name", map.title || map.name)}
        {@render row("Path", map.path, true)}
        {@render row(
          "Kind",
          `${map.archive ? "Archive" : "Folder"}${map.expansion === true ? ", Frozen Throne (Exp)" : map.expansion === false ? ", Reign of Chaos (RoC)" : ""}`,
        )}
        {#if map.options}
          {@render row("Data set", map.options.dataSet)}
          {@render row("Data version", map.options.dataVersion)}
          {@render row("Game values", `From ${tableNames[map.options.values] ?? map.options.values}`)}
          {@render row("Game objects", `Those in ${tableNames[map.options.objects] ?? map.options.objects}`)}
        {:else}
          {@render row("Game data", "Unknown: the map has no war3map.w3i the editor can read")}
        {/if}
        {@render row(
          "Custom objects",
          customKinds.length === 0
            ? "None"
            : customKinds.map((kind) => `${map.counts[kind]} ${kindNames[kind].toLowerCase()}`).join(", "),
        )}
        {#if map.problems.length > 0}
          {@render row("Problems", `${map.problems.length} (the status bar lists them)`)}
        {/if}
      {:else}
        <p class="m-0 text-faint">No map is open.</p>
      {/if}
    {:else}
      {@render row("Folder", data?.source ?? "None chosen", true)}
      {@render row(
        "State",
        data?.error ? `Did not load: ${data.error}` : data?.loaded ? "Loaded" : "Loading…",
      )}
    {/if}
  </div>

  {#snippet actions()}
    <button
      type="button"
      class="rounded-[5px] bg-accent px-3 py-1 font-bold text-bg hover:bg-accent-hi"
      data-primary
      onclick={onclose}
    >
      Done
    </button>
  {/snippet}
</Dialog>
