<script lang="ts">
import FolderOpen from "@lucide/svelte/icons/folder-open";
import Search from "@lucide/svelte/icons/search";
import { onMount } from "svelte";
import {
  type ChoiceList,
  choices,
  type Hit,
  type Kind,
  type MapInfo,
  type ObjectView,
  object,
  onSessionChanged,
  openMap,
  type SessionState,
  startup,
  type TreeEntry,
  tree,
} from "./lib/api.ts";
import { call, on } from "./lib/bridge.ts";
import Details from "./lib/Details.svelte";
import { type Lookup, listKinds } from "./lib/fields.ts";
import { chooseGameData, type GameDataState, gameData, newer, viewOf } from "./lib/gameData.ts";
import Palette from "./lib/Palette.svelte";
import SidePanel from "./lib/SidePanel.svelte";
import StatusBar from "./lib/StatusBar.svelte";
import Tree from "./lib/Tree.svelte";

/** A place in the visited-objects history (Alt ← and Alt →). */
type Place = { kind: Kind; id: string };

let connection = $state("");
let data = $state<GameDataState | null>(null);
let problem = $state("");

let map = $state.raw<MapInfo | null>(null);
let session = $state.raw<SessionState | null>(null);
let mapProblem = $state("");
let showProblems = $state(false);
let opening = $state(false);

let kind = $state<Kind>("unit");
// Each kind's tree, fetched when first needed; also where the names of a list's objects come from.
let trees = $state.raw<Partial<Record<Kind, TreeEntry[]>>>({});
let treeErrors = $state<Partial<Record<Kind, string>>>({});
// The object last picked in each kind.
let picked = $state<Partial<Record<Kind, string>>>({});
let current = $state.raw<ObjectView | null>(null);
let objectError = $state("");
let choiceLists = $state.raw<Record<string, ChoiceList> | null>(null);

let fieldFilter = $state("");
let changedOnly = $state(false);
let showIds = $state(false);
let paletteOpen = $state(false);
let details: { focusFilter(): void } | undefined = $state();

let history: Place[] = [];
let at = -1;
// Answers that come back after a newer request (another map, another object) are dropped.
let mapGeneration = 0;
let objectAsked = 0;
const treesAsked = new Set<Kind>();
let startupAsked = false;

const view = $derived(data ? viewOf(data) : null);

// Names by kind and id, from the trees fetched so far.
const names = $derived.by(() => {
  const result: Partial<Record<Kind, Map<string, string>>> = {};
  for (const [each, entries] of Object.entries(trees) as [Kind, TreeEntry[]][]) {
    result[each] = new Map(
      entries.map((entry) => [
        entry.id,
        entry.suffix ? `${entry.name} ${entry.suffix}` : entry.name,
      ]),
    );
  }
  return result;
});

const lookup: Lookup = (listOf, id) => {
  for (const each of listKinds(listOf)) {
    const name = names[each]?.get(id);
    if (name !== undefined) return { kind: each, name };
  }
  return undefined;
};

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

async function choose(): Promise<void> {
  problem = "";
  try {
    const chosen = await chooseGameData();
    data = newer(data, chosen);
  } catch (error) {
    problem = message(error);
  }
}

/** Opens a map folder: `path`, or the one picked in the app's dialog. */
async function openMapFolder(path?: string): Promise<void> {
  mapProblem = "";
  opening = true;
  try {
    const started = performance.now();
    const info = await openMap(path);
    if (!info) return;
    console.info(`[timing] openMap ${info.name}: ${Math.round(performance.now() - started)} ms`);
    mapGeneration++;
    map = info;
    trees = {};
    treeErrors = {};
    treesAsked.clear();
    picked = {};
    objectAsked++;
    current = null;
    objectError = "";
    history = [];
    at = -1;
    showProblems = info.problems.length > 0;
    await ensureTree(kind);
  } catch (error) {
    mapProblem = message(error);
  } finally {
    opening = false;
  }
}

async function ensureTree(each: Kind): Promise<void> {
  if (treesAsked.has(each)) return;
  treesAsked.add(each);
  const generation = mapGeneration;
  const started = performance.now();
  try {
    const entries = await tree(each);
    if (generation !== mapGeneration) return;
    console.info(
      `[timing] tree ${each} (${entries.length}): ${Math.round(performance.now() - started)} ms`,
    );
    trees = { ...trees, [each]: entries };
  } catch (error) {
    if (generation !== mapGeneration) return;
    treesAsked.delete(each);
    treeErrors[each] = message(error);
  }
}

/** Shows an object, and remembers it in the history unless the history is being walked. */
async function show(each: Kind, id: string, remember = true): Promise<void> {
  if (remember) visit({ kind: each, id });
  kind = each;
  picked[each] = id;
  ensureTree(each);
  const mine = ++objectAsked;
  objectError = "";
  const started = performance.now();
  try {
    const found = await object(each, id);
    if (mine !== objectAsked) return;
    console.info(`[timing] object ${each} ${id}: ${Math.round(performance.now() - started)} ms`);
    current = found;
  } catch (error) {
    if (mine !== objectAsked) return;
    current = null;
    objectError = message(error);
  }
}

function visit(place: Place): void {
  const here = history[at];
  if (here && here.kind === place.kind && here.id === place.id) return;
  history = history.slice(0, at + 1);
  history.push(place);
  if (history.length > 100) history.shift();
  at = history.length - 1;
}

function walk(by: number): void {
  const place = history[at + by];
  if (!place) return;
  at += by;
  show(place.kind, place.id, false);
}

function switchKind(each: Kind): void {
  const id = picked[each];
  if (id) {
    show(each, id);
    return;
  }
  kind = each;
  ensureTree(each);
  objectAsked++;
  current = null;
  objectError = "";
}

async function pick(hit: Hit): Promise<void> {
  paletteOpen = false;
  if (!hit.field) {
    await show(hit.kind, hit.id);
    return;
  }
  // A field hit filters the details panel to the field, on an object of the field's kind.
  fieldFilter = hit.field;
  changedOnly = false;
  if (current?.kind === hit.kind) return;
  const id = picked[hit.kind];
  if (id) {
    await show(hit.kind, id);
    return;
  }
  kind = hit.kind;
  await ensureTree(hit.kind);
  const first = trees[hit.kind]?.[0];
  if (first) await show(hit.kind, first.id);
}

function onkeydown(event: KeyboardEvent): void {
  if (paletteOpen) return; // the palette has its own keys
  const key = event.key.toLowerCase();
  if (event.ctrlKey && !event.altKey && key === "k") {
    event.preventDefault();
    if (map) paletteOpen = true;
  } else if (event.ctrlKey && !event.altKey && key === "f") {
    event.preventDefault();
    details?.focusFilter();
  } else if (event.ctrlKey && !event.altKey && key === "d") {
    event.preventDefault();
    showIds = !showIds;
  } else if (event.altKey && (event.key === "ArrowLeft" || event.key === "ArrowRight")) {
    event.preventDefault();
    walk(event.key === "ArrowLeft" ? -1 : 1);
  } else if (event.key === "Escape") {
    showProblems = false;
  }
}

// Once the game data is ready: the choice lists, and the map the app was started with, if any.
$effect(() => {
  if (view !== "ready" || startupAsked) return;
  startupAsked = true;
  (async () => {
    try {
      choiceLists = await choices();
      const started = await startup();
      if (started.map) await openMapFolder(started.map);
    } catch (error) {
      mapProblem = message(error);
    }
  })();
});

// The trees of the kinds the object's lists hold, so their ids show with names.
$effect(() => {
  const shown = current;
  if (!shown) return;
  for (const field of shown.fields) {
    for (const each of listKinds(field.listOf)) ensureTree(each);
  }
});

onMount(() => {
  // Listen first, then ask: a load that ends in between still reaches the page.
  let stops: (() => void)[] = [];
  try {
    stops = [
      on("gameData", (payload) => {
        data = newer(data, payload as GameDataState);
      }),
      onSessionChanged((state) => {
        session = state;
      }),
    ];
  } catch (error) {
    connection = message(error);
    return;
  }
  (async () => {
    try {
      await call<string>("ping");
      const state = await gameData();
      data = newer(data, state); // the event may have brought a newer state meanwhile
    } catch (error) {
      connection = message(error);
    }
  })();
  return () => {
    for (const stop of stops) stop();
  };
});
</script>

<svelte:window {onkeydown} />

<div class="grid h-full grid-cols-1 grid-rows-[44px_1fr_26px]">
  <header class="flex items-center gap-2.5 border-b border-line bg-surface px-3">
    <button
      type="button"
      class="flex shrink-0 items-center gap-2 rounded-md border border-line bg-raised px-3 py-1 text-fg hover:border-accent disabled:opacity-50"
      disabled={view !== "ready" || opening}
      onclick={() => openMapFolder()}
    >
      <FolderOpen size={16} />
      Open map…
    </button>
    {#if map}
      <span
        class="max-w-[35%] min-w-0 truncate font-bold text-fg"
        data-testid="map-name"
        title={map.path}
      >
        {map.name}
        <small class="ml-1.5 font-mono text-xs font-normal text-muted">{map.path}</small>
      </span>
    {/if}
    <button
      type="button"
      class="mx-auto flex max-w-[520px] flex-1 cursor-text items-center gap-2 rounded-md border border-line bg-bg px-2.5 py-[5px] text-left text-muted disabled:cursor-default disabled:opacity-50"
      disabled={!map}
      onclick={() => (paletteOpen = true)}
    >
      <Search size={14} />
      <span class="truncate">Search objects, ids, fields, values…</span>
      <span
        class="ml-auto shrink-0 rounded border border-line px-1.5 font-mono text-[11px] whitespace-nowrap"
        >Ctrl K</span
      >
    </button>
    <div class="flex shrink-0 gap-0.5" role="group" aria-label="Views">
      <button
        type="button"
        class="rounded-[5px] bg-accent-lo px-2.5 py-1.5 text-accent-hi"
        aria-pressed="true"
      >
        Details
      </button>
      <button
        type="button"
        class="rounded-[5px] px-2.5 py-1.5 text-muted opacity-50"
        disabled
        title="Coming in a later version"
      >
        Table
      </button>
      <button
        type="button"
        class="rounded-[5px] px-2.5 py-1.5 text-muted opacity-50"
        disabled
        title="Coming in a later version"
      >
        Compare
      </button>
    </div>
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
  {:else if !map}
    <main class="grid min-h-0 place-items-center" data-testid="no-map">
      <div class="flex max-w-lg flex-col items-start gap-3 px-6">
        <h1 class="font-heading text-2xl font-bold text-fg">No map is open</h1>
        {#if view === "ready"}
          <p>
            Open a map folder (a World Editor folder map, or a Moonwell project's map folder) to browse
            its objects.
          </p>
        {:else}
          <p>Loading the game data… A map can be opened once it is ready.</p>
        {/if}
        {#if mapProblem}
          <p class="text-warn" data-testid="map-error">{mapProblem}</p>
        {/if}
        <button
          type="button"
          class="inline-flex items-center gap-2 rounded-md border border-accent bg-raised px-3 py-1.5 text-fg hover:bg-hover disabled:opacity-50"
          disabled={view !== "ready" || opening}
          onclick={() => openMapFolder()}
        >
          <FolderOpen size={16} />
          {opening ? "Opening…" : "Open map…"}
        </button>
      </div>
    </main>
  {:else}
    <main
      class="grid min-h-0 grid-cols-[290px_1fr_300px] max-[1100px]:grid-cols-[230px_1fr_240px]"
    >
      <aside class="min-h-0 overflow-hidden border-r border-line bg-surface" aria-label="Objects">
        <Tree
          {kind}
          entries={trees[kind] ?? null}
          error={treeErrors[kind] ?? ""}
          selected={picked[kind] ?? null}
          onkind={switchKind}
          onselect={(id) => show(kind, id)}
        />
      </aside>
      <section class="min-h-0 overflow-auto" aria-label="Details">
        {#if mapProblem}
          <p class="mx-[18px] mt-3.5 text-warn" data-testid="map-error">{mapProblem}</p>
        {/if}
        {#if objectError}
          <p class="mx-[18px] mt-3.5 text-warn">{objectError}</p>
        {:else if current}
          <Details
            bind:this={details}
            view={current}
            choices={choiceLists}
            {lookup}
            bind:filter={fieldFilter}
            bind:changedOnly
            bind:showIds
            onopen={(each, id) => show(each, id)}
          />
        {:else}
          <p class="mx-[18px] mt-3.5 text-muted">
            Pick an object in the tree, or search with Ctrl K.
          </p>
        {/if}
      </section>
      <aside
        class="min-h-0 overflow-auto border-l border-line bg-surface p-3"
        aria-label="Components"
      >
        {#if current}
          <SidePanel view={current} {lookup} onopen={(each, id) => show(each, id)} />
        {/if}
      </aside>
    </main>
  {/if}

  <StatusBar
    {session}
    problems={map?.problems ?? []}
    bind:showProblems
    {data}
    dataView={view}
    {connection}
  />
</div>

{#if paletteOpen}
  <Palette onpick={pick} onclose={() => (paletteOpen = false)} />
{/if}
