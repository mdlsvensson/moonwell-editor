<script lang="ts">
import FolderOpen from "@lucide/svelte/icons/folder-open";
import RotateCw from "@lucide/svelte/icons/rotate-cw";
import Search from "@lucide/svelte/icons/search";
import { onMount } from "svelte";
import {
  type ChoiceList,
  choices,
  closeAsking,
  closeWindow,
  copyObject,
  createObject,
  currentMap,
  deleteObject,
  type FieldRef,
  type FieldView,
  type Hit,
  type Kind,
  type MapInfo,
  nextId,
  type ObjectView,
  object,
  onCloseRequested,
  onSessionChanged,
  openMap,
  redo,
  reload,
  resetField,
  type SessionState,
  save,
  setField,
  startup,
  type TreeEntry,
  tree,
  undo,
} from "./lib/api.ts";
import { BridgeError, call, on } from "./lib/bridge.ts";
import Details from "./lib/Details.svelte";
import Dialog from "./lib/Dialog.svelte";
import { type Lookup, listKinds } from "./lib/fields.ts";
import { chooseGameData, type GameDataState, gameData, newer, viewOf } from "./lib/gameData.ts";
import Palette from "./lib/Palette.svelte";
import SidePanel from "./lib/SidePanel.svelte";
import StatusBar, { type Notice } from "./lib/StatusBar.svelte";
import {
  type Choice,
  dropUnsaved,
  type Settled,
  type Settling,
  settleUnsaved,
} from "./lib/settle.ts";
import Tree from "./lib/Tree.svelte";
import { hasTyped, sendTyped } from "./lib/typing.ts";
import { nameField, savedText } from "./lib/values.ts";

/** A place in the visited-objects history (Alt ← and Alt →). */
type Place = { kind: Kind; id: string };

/** The unsaved-changes dialog: its question, whether Save is offered, and how it answers. */
type Unsaved = { question: string; canSave: boolean; answer: (choice: Choice) => void };

/** The new-object dialog: the base, the id typed (the next free one to start with), and why it failed. */
type Creating = {
  kind: Kind;
  baseId: string;
  baseName: string;
  id: string;
  error: string;
  busy: boolean;
};

/** The object the delete dialog asks about. */
type Deleting = { kind: Kind; id: string; name: string; baseId: string | null };

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
let details: { focusFilter(): void; focusField(field: FieldView): Promise<boolean> } | undefined =
  $state();

let notice = $state.raw<Notice | null>(null);
let noticeTimer: ReturnType<typeof setTimeout> | undefined;
let saving = $state(false);
let unsaved = $state.raw<Unsaved | null>(null);
// The message of a save refused because the map changed on disk; "" when there is none to show.
let diskChanged = $state("");
let creating = $state<Creating | null>(null);
let deleting = $state.raw<Deleting | null>(null);
let closing = false;
// The app's commands that change the map run one after another, in the order asked (a save after the edit typed).
let queue: Promise<unknown> = Promise.resolve();

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

/** Runs a command that changes the map once the ones asked before it have answered. */
function queued<T>(work: () => Promise<T>): Promise<T> {
  const next = queue.then(work);
  queue = next.catch(() => {});
  return next;
}

/** Shows a short message in the status bar for a few seconds (longer for a warning or a caution). */
function say(
  text: string,
  options: { title?: string; warn?: boolean; caution?: string } = {},
): void {
  clearTimeout(noticeTimer);
  notice = { text, ...options };
  noticeTimer = setTimeout(() => (notice = null), options.warn || options.caution ? 10000 : 6000);
}

/**
 * How to ask what to do with the unsaved edits before an action (`question` names it), once a value still being typed
 * and every edit on its way have been answered (see `settle.ts`). With nothing unsaved it does not ask, unless
 * `always` (the window saw the map dirty, or a box holds a typed value).
 */
function settling(question: string, canSave = true, always = false): Settling {
  return {
    flush: async () => {
      sendTyped(); // puts what was typed on the queue before the queue is waited for
      await queue;
    },
    dirty: () => session?.dirty === true,
    ask: () =>
      unsaved
        ? Promise.resolve<Choice>("cancel") // already asking
        : new Promise<Choice>((answer) => {
            unsaved = {
              question,
              canSave,
              answer: (choice) => {
                unsaved = null;
                answer(choice);
              },
            };
          }),
    save: saveMap,
    always,
  };
}

/** Asks about the unsaved edits before an action that keeps them (closing asks this way). */
function settle(question: string, canSave = true, always = false): Promise<Settled> {
  return settleUnsaved(settling(question, canSave, always));
}

/**
 * Runs an action that drops the unsaved edits (opening another map, reloading) once they are settled; `act` tells the
 * app to discard them only when the user chose to. When the app holds edits the page did not know of (the page was
 * loaded again), it refuses, and the page asks after all. True when it acted.
 */
function dropping(
  question: string,
  canSave: boolean,
  act: (discard: boolean) => Promise<unknown>,
): Promise<boolean> {
  return dropUnsaved({
    ...settling(question, canSave),
    act,
    refresh: async () => {
      const open = await currentMap();
      if (open) session = open.session;
    },
  });
}

/** "Open map…": asks about unsaved edits first. */
async function openAnother(): Promise<void> {
  try {
    await dropping("Save them before opening another map?", true, (discard) =>
      openMapFolder(undefined, discard),
    );
  } catch (error) {
    mapProblem = message(error);
  }
}

/**
 * Opens a map folder: `path`, or the one picked in the app's dialog. `discard` lets the app drop unsaved edits; without
 * it, it refuses with `unsaved`, which is passed on (every other failure is shown).
 */
async function openMapFolder(path?: string, discard = false): Promise<void> {
  mapProblem = "";
  opening = true;
  try {
    const started = performance.now();
    const info = await openMap(path, discard);
    if (!info) return;
    console.info(`[timing] openMap ${info.name}: ${Math.round(performance.now() - started)} ms`);
    await showMap(info);
  } catch (error) {
    if (error instanceof BridgeError && error.code === "unsaved") throw error;
    mapProblem = message(error);
  } finally {
    opening = false;
  }
}

/** Shows a map that was opened (or that the app had open when the page loaded): its tree, no object picked yet. */
async function showMap(info: MapInfo): Promise<void> {
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

/** Fetches the trees already loaded again (or one kind's), for new, deleted and renamed objects. */
async function refreshTrees(only?: Kind): Promise<void> {
  const generation = mapGeneration;
  const loaded = (Object.keys(trees) as Kind[]).filter((each) => !only || each === only);
  await Promise.all(
    loaded.map(async (each) => {
      try {
        const entries = await tree(each);
        if (generation === mapGeneration) trees = { ...trees, [each]: entries };
      } catch {
        // The tree as it was stays.
      }
    }),
  );
}

/** Fetches the object shown again (after an undo, a redo or a reload). */
async function refreshObject(): Promise<void> {
  const shown = current;
  if (!shown) return;
  const mine = ++objectAsked;
  try {
    const found = await object(shown.kind, shown.id);
    if (mine === objectAsked) current = found;
  } catch (error) {
    if (mine !== objectAsked) return;
    current = null;
    objectError = message(error);
  }
}

/**
 * Sets a field of the object shown (or, with null, puts it back to the standard value). The object shown and its
 * tree are updated from the answer; answers with why the app refused the value, or null.
 */
async function edit(field: FieldView, value: string | null): Promise<string | null> {
  const shown = current;
  if (!shown) return "No object is shown.";
  const target: FieldRef = { kind: shown.kind, id: shown.id, field: field.id, level: field.level };
  try {
    const view = await queued(() =>
      value === null ? resetField(target) : setField(target, value),
    );
    if (current?.kind === view.kind && current.id === view.id) current = view;
    refreshTrees(view.kind);
    return null;
  } catch (error) {
    return message(error);
  }
}

async function undoOrRedo(again: boolean): Promise<void> {
  if (!map) return;
  try {
    const done = await queued(again ? redo : undo);
    if (!done) {
      say(again ? "Nothing to redo" : "Nothing to undo");
      return;
    }
    say(again ? "Redone" : "Undone");
    await Promise.all([refreshTrees(), refreshObject()]);
  } catch (error) {
    say(message(error), { warn: true });
  }
}

/** Saves the map (Ctrl S). A value still being typed is sent first. True when it saved. */
async function saveMap(): Promise<boolean> {
  if (!map || saving) return false;
  sendTyped(); // what was typed goes on the queue before the save
  saving = true;
  try {
    const report = await queued(save);
    const files = report.written.join(", ");
    say(savedText(report), {
      title: report.backup ? `${files}\nBackup: ${report.backup}` : files || undefined,
    });
    return true;
  } catch (error) {
    if (error instanceof BridgeError && error.code === "changedOnDisk") diskChanged = error.message;
    else say(`Not saved: ${message(error)}`, { warn: true });
    return false;
  } finally {
    saving = false;
  }
}

/** Reads the map again from disk, after asking about unsaved edits (with no Save after a refused one). */
async function reloadMap(afterDiskChange = false): Promise<void> {
  if (!map) return;
  const question = afterDiskChange
    ? "Reloading the map from disk drops them."
    : "Save them before reloading the map from disk?";
  try {
    const done = await dropping(question, !afterDiskChange, async (discard) => {
      const info = await queued(() => reload(discard));
      map = info;
      showProblems = info.problems.length > 0;
    });
    if (!done) return;
    await Promise.all([refreshTrees(), refreshObject()]);
    say("Reloaded the map from disk");
  } catch (error) {
    say(message(error), { warn: true });
  }
}

/** The new-object dialog (Ctrl Shift N), based on the object shown (or its base), with the next free id. */
async function startNew(): Promise<void> {
  const shown = current;
  if (!shown) {
    say("Pick an object to base the new one on.");
    return;
  }
  if (shown.readOnly) {
    say(`Read only: ${shown.readOnly}`, { warn: true });
    return;
  }
  const baseId = shown.baseId ?? shown.id;
  const baseName = shown.baseId ? (names[shown.kind]?.get(baseId) ?? baseId) : shown.name;
  let id = "";
  let error = "";
  try {
    id = await nextId(shown.kind, baseId);
  } catch (problem) {
    error = message(problem);
  }
  creating = { kind: shown.kind, baseId, baseName, id, error, busy: false };
}

async function create(): Promise<void> {
  const asked = creating;
  if (!asked || asked.busy) return;
  asked.busy = true;
  asked.error = "";
  const { kind: each, baseId, baseName } = asked;
  const typed = asked.id.trim();
  try {
    const id = await queued(() => createObject(each, baseId, typed === "" ? undefined : typed));
    creating = null;
    await refreshTrees(each);
    await show(each, id);
    say(`Made ${id}, based on ${baseName}`);
  } catch (error) {
    asked.error = message(error);
    asked.busy = false;
  }
}

/** Copies the custom object shown, with the next free id, and shows the copy. */
async function copyShown(): Promise<void> {
  const shown = current;
  if (!shown?.baseId) return;
  try {
    const id = await queued(() => copyObject(shown.kind, shown.id));
    await refreshTrees(shown.kind);
    await show(shown.kind, id);
    say(`Copied ${shown.name} (${shown.id}) to ${id}`);
  } catch (error) {
    say(message(error), { warn: true });
  }
}

function askDelete(): void {
  const shown = current;
  if (!shown?.baseId) return;
  deleting = { kind: shown.kind, id: shown.id, name: shown.name, baseId: shown.baseId };
}

/** The Delete key (Ctrl Shift D with `copy`): deletes (asking first) or copies the custom object shown. */
function deleteOrCopyKey(copy: boolean): void {
  const shown = current;
  if (!shown) return;
  if (!shown.baseId) {
    say(copy ? "Only custom objects can be copied." : "Only custom objects can be deleted.");
    return;
  }
  if (shown.readOnly) {
    say(`Read only: ${shown.readOnly}`, { warn: true });
    return;
  }
  if (copy) copyShown();
  else askDelete();
}

async function deleteAsked(): Promise<void> {
  const asked = deleting;
  deleting = null;
  if (!asked) return;
  try {
    await queued(() => deleteObject(asked.kind, asked.id));
    say(`Deleted ${asked.name} (${asked.id}) · Ctrl Z brings it back`);
    await refreshTrees(asked.kind);
    if (current?.kind === asked.kind && current.id === asked.id && asked.baseId) {
      await show(asked.kind, asked.baseId);
    }
  } catch (error) {
    say(message(error), { warn: true });
  }
}

/** F2: the cursor in the name field of the object shown. */
async function focusName(): Promise<void> {
  const shown = current;
  if (!shown) return;
  if (shown.readOnly) {
    say(`Read only: ${shown.readOnly}`, { warn: true });
    return;
  }
  const field = nameField(shown);
  if (field) await details?.focusField(field);
}

/** True when a key goes to a box the user types in, which has its own undo. */
function typingIn(target: EventTarget | null): boolean {
  if (target instanceof HTMLTextAreaElement) return true;
  if (target instanceof HTMLInputElement) {
    return !["checkbox", "radio", "button", "submit", "reset"].includes(target.type);
  }
  return target instanceof HTMLElement && target.isContentEditable;
}

/** True when a key goes to a popover or dialog (a list of choices, a checkbox), not to the object shown. */
function inPopup(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="dialog"]') !== null;
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
  if (unsaved || diskChanged || creating || deleting) return; // and so do the dialogs
  const key = event.key.toLowerCase();
  const ctrl = event.ctrlKey && !event.altKey;
  if (ctrl && key === "s") {
    event.preventDefault();
    saveMap();
  } else if (ctrl && event.shiftKey && key === "n") {
    event.preventDefault();
    if (map) startNew();
  } else if (ctrl && (key === "y" || key === "z")) {
    if (typingIn(event.target)) return; // the box's own undo
    event.preventDefault();
    undoOrRedo(key === "y" || event.shiftKey);
  } else if (event.key === "F2") {
    event.preventDefault();
    focusName();
  } else if (event.ctrlKey && !event.altKey && key === "k") {
    event.preventDefault();
    if (map) paletteOpen = true;
  } else if (event.ctrlKey && !event.altKey && key === "f") {
    event.preventDefault();
    details?.focusFilter();
  } else if (ctrl && event.shiftKey && key === "d") {
    event.preventDefault();
    deleteOrCopyKey(true);
  } else if (event.ctrlKey && !event.altKey && key === "d") {
    event.preventDefault();
    showIds = !showIds;
  } else if (event.key === "Delete" && !event.ctrlKey && !event.altKey && !event.shiftKey) {
    if (typingIn(event.target) || inPopup(event.target)) return; // the box's own Delete
    event.preventDefault();
    deleteOrCopyKey(false);
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
      onCloseRequested(async (request) => {
        // At once, so the window does not ask on its own; then, if needed, the dialog, and close only on its answer.
        closeAsking().catch(() => {});
        if (closing) return;
        closing = true;
        try {
          // It asks when the window saw the map dirty (the page may not have heard yet) or a box holds a typed value
          // (even one the app may refuse); otherwise it closes at once, once what was typed has been sent.
          const always = request.dirty || hasTyped();
          if ((await settle("Save them before closing the window?", true, always)) !== "stopped") {
            await closeWindow();
          }
        } finally {
          closing = false;
        }
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
      // A page loaded again (reloaded, or after its process failed) shows the map the app has open, edits and all.
      const open = await currentMap();
      if (open && !map) {
        session = open.session;
        await showMap(open.map);
      }
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
      onclick={openAnother}
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
      <button
        type="button"
        class="shrink-0 rounded-md p-1 text-muted hover:bg-hover hover:text-fg"
        title="Reload the map from disk"
        aria-label="Reload the map from disk"
        onclick={() => reloadMap()}
      >
        <RotateCw size={15} />
      </button>
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
    <button
      type="button"
      class="shrink-0 rounded-[5px] bg-accent px-3 py-[5px] font-bold text-bg hover:bg-accent-hi disabled:opacity-40"
      disabled={!map || saving || !session?.dirty}
      title="Save the map (Ctrl S)"
      onclick={saveMap}
    >
      {saving ? "Saving…" : "Save"}
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
          onclick={openAnother}
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
            onset={(field, value) => edit(field, value)}
            onreset={(field) => edit(field, null)}
            onnew={startNew}
            oncopy={copyShown}
            ondelete={askDelete}
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
    {notice}
  />
</div>

{#if paletteOpen}
  <Palette onpick={pick} onclose={() => (paletteOpen = false)} />
{/if}

{#snippet button(text: string, onclick: () => void, look: "primary" | "plain" | "danger" = "plain")}
  <button
    type="button"
    class="rounded-[5px] px-3 py-1 {look === 'primary'
      ? 'bg-accent font-bold text-bg hover:bg-accent-hi'
      : look === 'danger'
        ? 'border border-warn font-bold text-warn hover:bg-warn-lo'
        : 'border border-line bg-raised text-fg hover:border-accent'}"
    data-primary={look === "primary" ? "" : undefined}
    {onclick}
  >
    {text}
  </button>
{/snippet}

{#if creating}
  {@const asked = creating}
  <Dialog title="New {asked.kind}" oncancel={() => (creating = null)}>
    <p class="m-0">
      Based on <b class="font-semibold text-fg">{asked.baseName}</b>
      <span class="font-mono text-[12.5px] text-muted">{asked.baseId}</span>
    </p>
    <label class="mt-3 flex items-center gap-2">
      Id
      <input
        class="w-28 rounded-[5px] border bg-bg px-2 py-1 font-mono text-fg outline-none focus:border-accent {asked.error
          ? 'border-warn'
          : 'border-line'}"
        maxlength="4"
        spellcheck="false"
        bind:value={asked.id}
        onkeydown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            create();
          }
        }}
      />
      <span class="text-xs text-muted">The next free id; type another to use it.</span>
    </label>
    {#if asked.error}
      <p class="mt-2 mb-0 text-[12.5px] text-warn" role="alert">{asked.error}</p>
    {/if}
    {#snippet actions()}
      {@render button("Cancel", () => (creating = null))}
      {@render button(asked.busy ? "Making…" : "Make", create, "primary")}
    {/snippet}
  </Dialog>
{/if}

{#if deleting}
  {@const asked = deleting}
  <Dialog title="Delete {asked.name}?" oncancel={() => (deleting = null)}>
    <p class="m-0">
      The map loses the custom {asked.kind}
      <b class="font-semibold text-fg">{asked.name}</b>
      <span class="font-mono text-[12.5px] text-muted">{asked.id}</span>. Ctrl Z brings it back until the map is
      closed.
    </p>
    {#snippet actions()}
      {@render button("Cancel", () => (deleting = null))}
      {@render button("Delete", deleteAsked, "danger")}
    {/snippet}
  </Dialog>
{/if}

{#if diskChanged}
  <Dialog title="The map changed on disk" oncancel={() => (diskChanged = "")}>
    <p class="m-0">{diskChanged}</p>
    {#snippet actions()}
      {@render button("Cancel", () => (diskChanged = ""))}
      {@render button(
        "Reload…",
        () => {
          diskChanged = "";
          reloadMap(true);
        },
        "primary",
      )}
    {/snippet}
  </Dialog>
{/if}

{#if unsaved}
  {@const asked = unsaved}
  {@const count = session?.changedObjects ?? 0}
  <Dialog title="Unsaved changes" oncancel={() => asked.answer("cancel")}>
    <p class="m-0">
      The map has unsaved changes{count > 0
        ? ` (${count} ${count === 1 ? "object" : "objects"})`
        : ""}. {asked.question}
    </p>
    {#snippet actions()}
      {@render button("Cancel", () => asked.answer("cancel"))}
      {@render button("Discard", () => asked.answer("discard"), asked.canSave ? "plain" : "danger")}
      {#if asked.canSave}
        {@render button("Save", () => asked.answer("save"), "primary")}
      {/if}
    {/snippet}
  </Dialog>
{/if}
