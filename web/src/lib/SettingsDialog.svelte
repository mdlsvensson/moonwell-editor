<script lang="ts">
/**
 * The editor's settings (Sven, 2026-10-27): the theme, which shows at once and is remembered, and what the editor
 * knows about the open map and the game data: the map's game data option (World Editor's Data Set and Data Version)
 * and the tables the game reads under it.
 */
import Check from "@lucide/svelte/icons/check";
import { type Kind, kinds, type MapInfo } from "./api.ts";
import Dialog from "./Dialog.svelte";
import type { GameDataState } from "./gameData.ts";
import { type Theme, themes } from "./themes.ts";
import { kindNames } from "./tree.ts";

type Props = {
  theme: Theme;
  ontheme: (theme: Theme) => void;
  map: MapInfo | null;
  data: GameDataState | null;
  onclose: () => void;
};

let { theme, ontheme, map, data, onclose }: Props = $props();

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
</script>

{#snippet row(label: string, value: string, mono = false)}
  <div class="flex gap-3 py-0.5">
    <span class="w-32 shrink-0 text-muted">{label}</span>
    <span class="min-w-0 break-words {mono ? 'font-mono text-[12.5px]' : ''} text-fg">{value}</span>
  </div>
{/snippet}

<Dialog title="Settings" oncancel={onclose} wide>
  <h3 class="m-0 mt-1 mb-2 text-[12px] tracking-[.06em] text-muted uppercase">Theme</h3>
  <div class="grid grid-cols-2 gap-1.5" role="radiogroup" aria-label="Theme">
    {#each themes as each (each.id)}
      <button
        type="button"
        role="radio"
        aria-checked={each.id === theme.id}
        class="flex items-center gap-2 rounded-md border px-2 py-1.5 text-left {each.id === theme.id
          ? 'border-accent bg-accent-lo/40 text-fg'
          : 'border-line hover:border-muted'}"
        onclick={() => ontheme(each)}
      >
        <span class="flex shrink-0 overflow-hidden rounded-sm border border-black/30">
          {#each swatch as token (token)}
            <span class="h-4 w-2.5" style:background={each.colors[token]}></span>
          {/each}
        </span>
        <span class="min-w-0 flex-1 truncate">{each.name}</span>
        {#if each.id === theme.id}<Check size={14} class="shrink-0 text-accent-hi" />{/if}
      </button>
    {/each}
  </div>

  <h3 class="m-0 mt-5 mb-2 text-[12px] tracking-[.06em] text-muted uppercase">Map</h3>
  {#if map}
    {@render row("Name", map.name)}
    {@render row("Path", map.path, true)}
    {@render row("Kind", map.archive ? "Archive (.w3x, .w3m)" : "Folder")}
    {#if map.options}
      {@render row("Data set", map.options.dataSet)}
      {@render row("Data version", map.options.dataVersion)}
      {@render row("Game values", `From ${tableNames[map.options.values] ?? map.options.values}`)}
      {@render row("Game objects", `Those in ${tableNames[map.options.objects] ?? map.options.objects}`)}
      <p class="m-0 mt-1.5 text-[12.5px] text-faint">
        The values the editor shows are the base tables' for now; following the map's data set is a later step.
      </p>
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

  <h3 class="m-0 mt-5 mb-2 text-[12px] tracking-[.06em] text-muted uppercase">Game data</h3>
  {@render row("Folder", data?.source ?? "None chosen", true)}
  {@render row("State", data?.error ? `Did not load: ${data.error}` : data?.loaded ? "Loaded" : "Loading…")}

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
