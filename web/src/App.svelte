<script lang="ts">
import FolderOpen from "@lucide/svelte/icons/folder-open";
import { onMount } from "svelte";
import { call, on } from "./lib/bridge.ts";
import { chooseGameData, type GameDataState, gameData, viewOf } from "./lib/gameData.ts";

let status = $state("Connecting…");
let data = $state<GameDataState | null>(null);
let problem = $state("");

const view = $derived(data ? viewOf(data) : null);

async function choose(): Promise<void> {
  problem = "";
  try {
    data = await chooseGameData();
  } catch (error) {
    problem = error instanceof Error ? error.message : String(error);
  }
}

onMount(() => {
  // Listen first, then ask: a load that ends in between still reaches the page.
  const stop = on("gameData", (payload) => {
    data = payload as GameDataState;
  });
  (async () => {
    try {
      status = await call<string>("ping");
      data = await gameData();
    } catch (error) {
      status = error instanceof Error ? error.message : String(error);
    }
  })();
  return stop;
});
</script>

<div class="grid h-full grid-rows-[44px_1fr_26px]">
  <header class="flex items-center gap-2.5 border-b border-line bg-surface px-3">
    <button
      type="button"
      class="flex items-center gap-2 rounded-md border border-line bg-raised px-3 py-1 text-fg hover:border-accent"
    >
      <FolderOpen size={16} />
      Open map…
    </button>
  </header>

  {#if view === "missing" || view === "failed"}
    <main class="grid min-h-0 place-items-center" data-testid="first-start">
      <div class="flex max-w-lg flex-col gap-3 px-6">
        <h1 class="font-heading text-2xl font-bold text-fg">Welcome to the Object Editor</h1>
        <p>
          The editor needs the game's own data to show the standard units, items and abilities. Point
          it at a game data folder (a <span class="font-mono text-fg">war3.w3mod</span> export).
        </p>
        {#if view === "failed" && data}
          <p class="text-warn" data-testid="game-data-error">
            <span class="font-mono">{data.source}</span> did not load: {data.error}
          </p>
        {/if}
        {#if problem}
          <p class="text-warn">{problem}</p>
        {/if}
        <div>
          <button
            type="button"
            class="inline-flex items-center gap-2 rounded-md border border-accent bg-raised px-3 py-1.5 text-fg hover:bg-hover"
            onclick={choose}
          >
            <FolderOpen size={16} />
            Choose the game data folder…
          </button>
        </div>
      </div>
    </main>
  {:else}
    <main class="grid min-h-0 grid-cols-[290px_1fr_300px] max-[1100px]:grid-cols-[230px_1fr_240px]">
      <aside class="min-h-0 overflow-auto border-r border-line bg-surface" aria-label="Objects"></aside>
      <section class="min-h-0 overflow-auto" aria-label="Details"></section>
      <aside class="min-h-0 overflow-auto border-l border-line bg-surface p-3" aria-label="Components"></aside>
    </main>
  {/if}

  <footer class="flex items-center gap-4 border-t border-line bg-surface px-3 text-xs text-muted">
    <span data-testid="status">{status}</span>
    <span data-testid="game-data-status">
      {#if view === "loading"}
        Loading the game data…
      {:else if view === "ready"}
        Game data loaded
      {:else if view === "failed"}
        Game data failed to load
      {/if}
    </span>
  </footer>
</div>
