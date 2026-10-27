<script lang="ts">
import FolderOpen from "@lucide/svelte/icons/folder-open";
import RotateCw from "@lucide/svelte/icons/rotate-cw";
import Settings from "@lucide/svelte/icons/settings";
import { onMount, tick } from "svelte";
import AlphaNotice, { alphaUnderstood } from "./lib/AlphaNotice.svelte";
import {
  type CellEdit,
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
  frameColors,
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
  setCells,
  setField,
  startup,
  type TreeEntry,
  tree,
  uiScale,
  undo,
} from "./lib/api.ts";
import { loadAssetNames } from "./lib/assetNames.svelte.ts";
import { BridgeError, call, on } from "./lib/bridge.ts";
import ContextMenu, { type MenuItem } from "./lib/ContextMenu.svelte";
import Details from "./lib/Details.svelte";
import Dialog from "./lib/Dialog.svelte";
import { type Lookup, listKinds } from "./lib/fields.ts";
import { chooseGameData, type GameDataState, gameData, newer, viewOf } from "./lib/gameData.ts";
import { graphics, viewInMode } from "./lib/graphics.svelte.ts";
import KindBar from "./lib/KindBar.svelte";
import { actionOf, loadKeymap } from "./lib/keymap.ts";
import OpenMapMenu from "./lib/OpenMapMenu.svelte";
import { type OpenEntry, openFrom } from "./lib/openMenu.ts";
import { prefs } from "./lib/prefs.svelte.ts";
import SettingsDialog from "./lib/SettingsDialog.svelte";
import SidePanel from "./lib/SidePanel.svelte";
import StatusBar, { type Notice } from "./lib/StatusBar.svelte";
import {
  type Choice,
  dropUnsaved,
  type Settled,
  type Settling,
  settleUnsaved,
} from "./lib/settle.ts";
import TableView, { type EditResult } from "./lib/TableView.svelte";
import TitleBar, { type Visit } from "./lib/TitleBar.svelte";
import TooltipEditor from "./lib/TooltipEditor.svelte";
import Tree from "./lib/Tree.svelte";
import { newTableState, type TableState } from "./lib/table.ts";
import { t, tCount, tParts } from "./lib/text.ts";
import { applyTheme, savedTheme, saveTheme, type Theme } from "./lib/themes.ts";
import { windowTitle } from "./lib/title.ts";
import { hasTyped, sendTyped } from "./lib/typing.ts";
import { nameField, savedText } from "./lib/values.ts";

/** A place in the visited-objects history (Alt ← and Alt →), with where the details panel was scrolled to. */
type Place = { kind: Kind; id: string; scroll?: number };

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
  /** For a paste: the object copied (or cut, to be deleted once its copy is made); absent for a new object. */
  from?: Clip;
};

/** An object copied or cut (Ctrl C, Ctrl X, the context menu), for Ctrl V to paste as a new custom object. */
type Clip = { kind: Kind; id: string; name: string; baseId: string; cut: boolean };

/** The object the delete dialog asks about. */
type Deleting = { kind: Kind; id: string; name: string; baseId: string | null };

let connection = $state("");
let data = $state<GameDataState | null>(null);
let problem = $state("");

let map = $state.raw<MapInfo | null>(null);
let session = $state.raw<SessionState | null>(null);
let mapProblem = $state("");
let showProblems = $state(false);
// Where the user last clicked or moved the focus: the tree (Ctrl C copies the object) or the details (a selected
// field's value); the tree's selection looks active only while it is the tree (Sven, 2026-10-27).
let zone = $state<"tree" | "details">("tree");
// The settings dialog, and the theme on the page (Sven, 2026-10-27).
let settingsOpen = $state(false);
// The question before a reload from disk with nothing unsaved.
let confirmingReload = $state(false);
// The alpha notice the editor opens with, until the user ticks "Don't show this again" (Sven, 2026-10-27).
let alphaOpen = $state(!alphaUnderstood());
// The hotkeys, which the settings change.
let keymap = $state(loadKeymap());
// Put on before the first paint, so the page never shows the default theme first.
const startTheme = savedTheme();
applyTheme(startTheme);
let theme = $state(startTheme);

/** Puts a theme on the page and the window's frame, and remembers it. */
function useTheme(chosen: Theme): void {
  theme = chosen;
  applyTheme(chosen);
  saveTheme(chosen);
  frameColors(chosen.colors.surface, chosen.colors.border).catch(() => {});
}
let opening = $state(false);

let kind = $state<Kind>("unit");
// Each kind's tree, fetched when first needed; also where the names of a list's objects come from.
let trees = $state.raw<Partial<Record<Kind, TreeEntry[]>>>({});
let treeErrors = $state<Partial<Record<Kind, string>>>({});
// The object last picked in each kind.
let picked = $state<Partial<Record<Kind, string>>>({});
let current = $state.raw<ObjectView | null>(null);
// The icons the shown kind's objects use, in the tree's order (each once), for the icon picker's order.
const iconsOfKind = $derived([
  ...new Set(
    (current ? (trees[current.kind] ?? []) : [])
      .map((entry) => entry.iconModes?.[graphics.mode] ?? entry.icon ?? "")
      .filter(Boolean),
  ),
]);
// The object as the graphics mode shows it (the values the game keeps per mode are the mode's).
const shown = $derived(current ? viewInMode(current, graphics.mode) : null);
let objectError = $state("");
let choiceLists = $state.raw<Record<string, ChoiceList> | null>(null);

// Which view the middle of the window shows: the selected object's details, or the kind's table.
let page = $state<"details" | "table">("details");
// The tree's filter text; the table filters its rows by it too.
let treeFilter = $state("");
// The table's chips, sort and columns per kind, while the map is open.
let tableState = $state<TableState>(newTableState());
let tableView: { focusFilter(): void } | undefined = $state();

let fieldFilter = $state("");
let changedOnly = $state(false);
let showIds = $state(false);
let details:
  | {
      focusFilter(): void;
      focusField(field: FieldView): Promise<boolean>;
      clipKey(key: "c" | "x" | "v"): boolean;
      scrollPosition(): number;
      scrollTo(top: number): void;
    }
  | undefined = $state();

let notice = $state.raw<Notice | null>(null);
let noticeTimer: ReturnType<typeof setTimeout> | undefined;
let saving = $state(false);
let unsaved = $state.raw<Unsaved | null>(null);
// The message of a save refused because the map changed on disk; "" when there is none to show.
let diskChanged = $state("");
let creating = $state<Creating | null>(null);
let clipboard = $state.raw<Clip | null>(null);
let menu = $state.raw<{ x: number; y: number; items: MenuItem[] } | null>(null);
// The tooltip editor is open on the object shown (Sven, 2026-10-27).
let editingTooltip = $state(false);
let deleting = $state.raw<Deleting | null>(null);
let closing = false;
// The app's commands that change the map run one after another, in the order asked (a save after the edit typed).
let queue: Promise<unknown> = Promise.resolve();

// The title bar's back and forward buttons follow these.
let history = $state<Place[]>([]);
let at = $state(-1);
// Answers that come back after a newer request (another map, another object) are dropped.
let mapGeneration = 0;
let objectAsked = 0;
const treesAsked = new Set<Kind>();
let startupAsked = false;

const view = $derived(data ? viewOf(data) : null);

// Names by kind and id, from the trees fetched so far; and the names alone, without their suffixes.
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
const bareNames = $derived.by(() => {
  const result: Partial<Record<Kind, Map<string, string>>> = {};
  for (const [each, entries] of Object.entries(trees) as [Kind, TreeEntry[]][]) {
    result[each] = new Map(entries.map((entry) => [entry.id, entry.name]));
  }
  return result;
});

const lookup: Lookup = (listOf, id) => {
  for (const each of listKinds(listOf)) {
    const name = names[each]?.get(id);
    if (name !== undefined) return { kind: each, name, bare: bareNames[each]?.get(id) };
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

/** "Open map…" › Folder… or Archive…: asks about unsaved edits first, then the entry's dialog picks the map. */
async function openAnother(entry: OpenEntry): Promise<void> {
  try {
    await dropping(t("ask.beforeOpen"), true, (discard) =>
      openMapWith(() => openFrom(entry, discard)),
    );
  } catch (error) {
    mapProblem = message(error);
  }
}

/**
 * Opens a map (a folder or an archive) with `open`, which asks the app (null when the user cancelled its dialog). An
 * `unsaved` refusal is passed on (the open did not say to discard); every other failure is shown.
 */
async function openMapWith(open: () => Promise<MapInfo | null>): Promise<void> {
  mapProblem = "";
  opening = true;
  try {
    const started = performance.now();
    const info = await open();
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
  tableState = newTableState();
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
  if (!shown) return t("notice.noObject");
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

/** Changes several fields of the object shown as one edit (the command card's X and Y); null when it was taken. */
async function editFields(edits: { field: FieldView; value: string }[]): Promise<string | null> {
  const shown = current;
  if (!shown) return t("notice.noObject");
  const result = await editCells(
    shown.kind,
    edits.map(({ field, value }) => ({ id: shown.id, field: field.id, level: field.level, value })),
  );
  return "error" in result ? result.error : null;
}

/**
 * Changes cells of the table as one edit, after the edits asked before it (it goes on the queue at once, so a save
 * asked after it waits for it). The table fetches its rows again on the `sessionChanged` that follows; the trees and
 * the object shown are fetched here, as an edit there may rename or change them.
 */
async function editCells(each: Kind, edits: CellEdit[]): Promise<EditResult> {
  try {
    const result = await queued(() => setCells(each, edits));
    if (result.changed > 0) {
      refreshTrees(each);
      refreshObject();
    }
    return result;
  } catch (error) {
    return { error: message(error) };
  }
}

async function undoOrRedo(again: boolean): Promise<void> {
  if (!map) return;
  try {
    const done = await queued(again ? redo : undo);
    if (!done) {
      say(again ? t("notice.nothingToRedo") : t("notice.nothingToUndo"));
      return;
    }
    say(again ? t("notice.redone") : t("notice.undone"));
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
      title: report.backup
        ? t("notice.savedTitleBackup", { files, backup: report.backup })
        : files || undefined,
      warn: report.note !== "",
    });
    return true;
  } catch (error) {
    if (error instanceof BridgeError && error.code === "changedOnDisk") diskChanged = error.message;
    else say(t("notice.notSaved", { error: message(error) }), { warn: true });
    return false;
  } finally {
    saving = false;
  }
}

/**
 * The reload button (Sven, 2026-10-27): one question, "Are you sure...?", which says so when there are unsaved
 * changes; the settings can turn the question off, but unsaved changes are always asked about.
 */
function askReload(): void {
  if (!prefs.confirmReload && !session?.dirty) reloadFromDisk(false);
  else confirmingReload = true;
}

/** Reads the map again from disk; with `discard`, unsaved edits are dropped. */
async function reloadFromDisk(discard: boolean): Promise<void> {
  if (!map) return;
  try {
    const info = await queued(() => reload(discard));
    map = info;
    showProblems = info.problems.length > 0;
    await Promise.all([refreshTrees(), refreshObject()]);
    say(t("notice.reloaded"));
  } catch (error) {
    say(message(error), { warn: true });
  }
}

/** Reads the map again from disk, after asking about unsaved edits (with no Save after a refused one). */
async function reloadMap(afterDiskChange = false): Promise<void> {
  if (!map) return;
  const question = afterDiskChange ? t("ask.reloadDrops") : t("ask.beforeReload");
  try {
    const done = await dropping(question, !afterDiskChange, async (discard) => {
      const info = await queued(() => reload(discard));
      map = info;
      showProblems = info.problems.length > 0;
    });
    if (!done) return;
    await Promise.all([refreshTrees(), refreshObject()]);
    say(t("notice.reloaded"));
  } catch (error) {
    say(message(error), { warn: true });
  }
}

/** The new-object dialog (Ctrl Shift N), based on the object shown (or its base), with the next free id. */
async function startNew(): Promise<void> {
  const shown = current;
  if (!shown) {
    say(t("notice.pickBase"));
    return;
  }
  if (shown.readOnly) {
    say(t("notice.readOnly", { reason: shown.readOnly }), { warn: true });
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

/**
 * Why an id typed for a new object cannot be used, as it is typed: not four letters or digits, or another object of
 * the kind has it; null when it can (or is empty, which takes the next free id).
 */
function idProblem(each: Kind, typed: string): string | null {
  const id = typed.trim();
  if (id === "") return null;
  if (!/^[0-9A-Za-z]{4}$/.test(id)) return t("notice.idInvalid");
  const taken = trees[each]?.find((entry) => entry.id === id);
  return taken ? t("notice.idTaken", { id, name: taken.name || id }) : null;
}

async function create(): Promise<void> {
  const asked = creating;
  if (!asked || asked.busy) return;
  asked.busy = true;
  asked.error = "";
  const { kind: each, baseId, baseName, from } = asked;
  const typed = asked.id.trim();
  const wanted = typed === "" ? undefined : typed;
  try {
    const id = await queued(() =>
      from ? copyObject(each, from.id, wanted) : createObject(each, baseId, wanted),
    );
    creating = null;
    if (from?.cut) {
      // A cut object goes once its copy is made: it moved to the new id.
      await queued(() => deleteObject(each, from.id));
      clipboard = null;
    }
    await refreshTrees(each);
    await show(each, id);
    say(
      from
        ? t(from.cut ? "notice.moved" : "notice.pasted", { name: from.name, from: from.id, id })
        : t("notice.made", { id, base: baseName }),
    );
  } catch (error) {
    asked.error = message(error);
    asked.busy = false;
  }
}

/** The tree entry of an object, when its kind's tree is loaded. */
function entryOf(each: Kind, id: string): TreeEntry | undefined {
  return trees[each]?.find((entry) => entry.id === id);
}

/** Ctrl C, or Ctrl X with `cut` (custom objects only): keeps the object for Ctrl V. */
function clip(each: Kind, id: string, cut: boolean): void {
  const entry = entryOf(each, id);
  if (!entry) return;
  if (cut && !entry.baseId) {
    say(t("notice.onlyCustomCut"));
    return;
  }
  clipboard = { kind: each, id, name: entry.name, baseId: entry.baseId ?? id, cut };
  say(t(cut ? "notice.cut" : "notice.copied", { name: entry.name, id }));
}

/** Ctrl V: the id dialog for a copy of the object copied or cut, with the next free id. */
async function paste(): Promise<void> {
  const from = clipboard;
  if (!from || !map) {
    say(t("notice.nothingCopied"));
    return;
  }
  let id = "";
  let error = "";
  try {
    id = await nextId(from.kind, from.baseId);
  } catch (problem) {
    error = message(problem);
  }
  const baseName = names[from.kind]?.get(from.baseId) ?? from.baseId;
  creating = { kind: from.kind, baseId: from.baseId, baseName, id, error, busy: false, from };
}

/** Puts every field the map changes on the object back to its standard (or base) value, as one edit. */
async function resetObject(each: Kind, id: string): Promise<void> {
  try {
    const view = current?.kind === each && current.id === id ? current : await object(each, id);
    if (view.readOnly) {
      say(t("notice.readOnly", { reason: view.readOnly }), { warn: true });
      return;
    }
    const edits = view.fields
      .filter((field) => field.changed)
      .map((field) => ({ id, field: field.id, level: field.level, value: null }));
    if (edits.length === 0) {
      say(t("notice.noChangedFields", { name: view.name }));
      return;
    }
    await queued(() => setCells(each, edits));
    await Promise.all([refreshTrees(each), refreshObject()]);
    say(tCount("notice.reset", edits.length, { name: view.name }));
  } catch (error) {
    say(message(error), { warn: true });
  }
}

/** True for a kind whose objects have a tooltip the tooltip editor edits. */
function hasTooltip(each: Kind): boolean {
  return each !== "destructable" && each !== "doodad";
}

/** The context menu of an object in the tree; the object is shown first, so every entry acts on it. */
async function objectMenu(each: Kind, id: string, x: number, y: number): Promise<void> {
  const entry = entryOf(each, id);
  if (!entry) return;
  const custom = entry.baseId !== null;
  menu = {
    x,
    y,
    items: [
      { label: t("menu.copy"), shortcut: "Ctrl C", run: () => clip(each, id, false) },
      {
        label: t("menu.cut"),
        shortcut: "Ctrl X",
        disabled: !custom,
        run: () => clip(each, id, true),
      },
      {
        label: clipboard ? t("menu.pasteObject", { name: clipboard.name }) : t("menu.paste"),
        shortcut: "Ctrl V",
        disabled: !clipboard || clipboard.kind !== each,
        run: paste,
      },
      { separator: true },
      { label: t("menu.rename"), shortcut: "F2", run: focusName },
      {
        label: t("menu.reset"),
        disabled: !entry.changed && !custom,
        run: () => resetObject(each, id),
      },
      {
        label: t("menu.editTooltip"),
        disabled: !hasTooltip(each),
        run: () => (editingTooltip = true),
      },
      { separator: true },
      { label: t("menu.newBased"), shortcut: "Ctrl Shift N", run: startNew },
      {
        label: t("menu.delete"),
        shortcut: "Del",
        disabled: !custom,
        danger: true,
        run: askDelete,
      },
    ],
  };
  await show(each, id);
}

/** Copies the custom object shown, with the next free id, and shows the copy. */
async function copyShown(): Promise<void> {
  const shown = current;
  if (!shown?.baseId) return;
  try {
    const id = await queued(() => copyObject(shown.kind, shown.id));
    await refreshTrees(shown.kind);
    await show(shown.kind, id);
    say(t("notice.copiedTo", { name: shown.name, from: shown.id, id }));
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
    say(copy ? t("notice.onlyCustomCopy") : t("notice.onlyCustomDelete"));
    return;
  }
  if (shown.readOnly) {
    say(t("notice.readOnly", { reason: shown.readOnly }), { warn: true });
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
    say(t("notice.deleted", { name: asked.name, id: asked.id }));
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
    say(t("notice.readOnly", { reason: shown.readOnly }), { warn: true });
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

/** A history entry for the title bar's list: its place and its object's name. */
function visitOf(place: Place, index: number): Visit {
  return { at: index, id: place.id, name: names[place.kind]?.get(place.id) || place.id };
}

function visit(place: Place): void {
  remember();
  const here = history[at];
  if (here && here.kind === place.kind && here.id === place.id) return;
  history = history.slice(0, at + 1);
  history.push(place);
  if (history.length > 100) history.shift();
  at = history.length - 1;
}

// Keeps where the details panel is scrolled to with the place shown, for going back to it.
function remember(): void {
  const here = history[at];
  if (here && details) here.scroll = details.scrollPosition();
}

async function walk(by: number): Promise<void> {
  const place = history[at + by];
  if (!place) return;
  remember();
  at += by;
  await show(place.kind, place.id, false);
  await tick();
  if (place.scroll !== undefined) details?.scrollTo(place.scroll);
}

// The mouse's back and forward buttons walk the history too, as in a browser.
function walkByMouse(event: MouseEvent): void {
  if (event.button !== 3 && event.button !== 4) return;
  event.preventDefault();
  walk(event.button === 3 ? -1 : 1);
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

/** Opens an object in the details view (a row's name in the table). */
function openInDetails(each: Kind, id: string): void {
  page = "details";
  show(each, id);
}

/**
 * The keys that act differently in the table: Ctrl F goes to its filter. The grid takes F2, Ctrl D and Delete for its
 * cells (and they never reach here); anywhere else in the table they, and the details view's other keys for the
 * object shown (Ctrl Shift D, Ctrl Shift N, Alt ←/→), do nothing, as that object is not in view. True when handled.
 */
function tableKey(event: KeyboardEvent): boolean {
  const action = actionOf(event, keymap);
  if (action === "filterFields") {
    event.preventDefault();
    tableView?.focusFilter();
    return true;
  }
  if (action === "deleteObject") {
    return !typingIn(event.target) && !inPopup(event.target);
  }
  if (
    action &&
    ["rename", "fieldIds", "newObject", "copyObject", "back", "forward"].includes(action)
  ) {
    event.preventDefault();
    return true;
  }
  return false;
}

function onkeydown(event: KeyboardEvent): void {
  if (
    unsaved ||
    diskChanged ||
    creating ||
    deleting ||
    settingsOpen ||
    alphaOpen ||
    confirmingReload
  )
    return; // and so do the dialogs
  if (page === "table" && tableKey(event)) return;
  if (event.key === "Escape") {
    showProblems = false;
    return;
  }
  // The keys of the keymap, which the settings change (Sven, 2026-10-27).
  const action = actionOf(event, keymap);
  if (!action) return;
  switch (action) {
    case "save":
      event.preventDefault();
      saveMap();
      break;
    case "newObject":
      event.preventDefault();
      if (map) startNew();
      break;
    case "undo":
    case "redo":
      if (typingIn(event.target)) return; // the box's own undo
      event.preventDefault();
      undoOrRedo(action === "redo");
      break;
    case "copy":
    case "cut":
    case "paste": {
      if (page !== "details" || !map || typingIn(event.target) || inPopup(event.target)) return;
      if (action !== "paste" && (window.getSelection()?.toString() ?? "") !== "") return; // copying text
      event.preventDefault();
      const clipboardKey = action === "copy" ? "c" : action === "cut" ? "x" : "v";
      if (zone === "details" && details?.clipKey(clipboardKey)) return; // a selected field's value
      if (action === "paste") paste();
      else if (current) clip(current.kind, current.id, action === "cut");
      break;
    }
    case "rename":
      event.preventDefault();
      focusName();
      break;
    case "filterFields":
      event.preventDefault();
      details?.focusFilter();
      break;
    case "copyObject":
      event.preventDefault();
      deleteOrCopyKey(true);
      break;
    case "fieldIds":
      event.preventDefault();
      showIds = !showIds;
      break;
    case "deleteObject":
      if (typingIn(event.target) || inPopup(event.target)) return; // the box's own Delete
      event.preventDefault();
      deleteOrCopyKey(false);
      break;
    case "back":
    case "forward":
      event.preventDefault();
      walk(action === "back" ? -1 : 1);
      break;
  }
}

// Once the game data is ready: the choice lists, and the map the app was started with, if any.
$effect(() => {
  if (view !== "ready" || startupAsked) return;
  startupAsked = true;
  (async () => {
    try {
      choiceLists = await choices();
      void loadAssetNames();
      const started = await startup();
      const path = started.map;
      if (path) await openMapWith(() => openMap({ path }));
    } catch (error) {
      mapProblem = message(error);
    }
  })();
});

// The window shows the open map's path in its title (the app copies the page's title to the window).
$effect(() => {
  document.title = windowTitle(map?.path ?? null);
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
  useTheme(theme);
  if (prefs.uiScale !== 100) uiScale(prefs.uiScale).catch(() => {});
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
          if ((await settle(t("ask.beforeClose"), true, always)) !== "stopped") {
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

<svelte:window
  {onkeydown}
  onmouseup={walkByMouse}
  oncontextmenu={(event) => !typingIn(event.target) && event.preventDefault()}
/>

<div class="grid h-full grid-cols-1 grid-rows-[23px_44px_1fr_26px]">
  <TitleBar
    {map}
    back={history
      .slice(0, Math.max(0, at))
      .map((place, index) => visitOf(place, index))
      .reverse()}
    forward={history.slice(at + 1).map((place, index) => visitOf(place, at + 1 + index))}
    onjump={(to) => walk(to - at)}
  />
  <header class="flex items-center gap-2.5 border-b border-line bg-surface px-3">
    <OpenMapMenu
      buttonClass="flex shrink-0 items-center rounded-[5px] p-1.5 text-muted hover:bg-hover hover:text-fg disabled:opacity-50"
      disabled={view !== "ready" || opening}
      onpick={openAnother}
    />
    <span class="h-5 w-px shrink-0 bg-line" aria-hidden="true"></span>
    <KindBar {kind} disabled={!map} onkind={switchKind} />
    {#if map}
      <span class="h-5 w-px shrink-0 bg-line" aria-hidden="true"></span>
      <button
        type="button"
        class="shrink-0 rounded-[5px] p-1.5 text-muted hover:bg-hover hover:text-fg"
        title={t("header.reload")}
        aria-label={t("header.reload")}
        onclick={askReload}
      >
        <RotateCw size={17} />
      </button>
    {/if}
    <div class="ml-auto flex shrink-0 gap-0.5" role="group" aria-label={t("header.views")}>
      {#each [{ key: "details", name: t("header.details") }, { key: "table", name: t("header.table") }] as const as each (each.key)}
        <button
          type="button"
          class="rounded-[5px] px-2.5 py-1.5 {page === each.key
            ? 'bg-accent-lo text-accent-hi'
            : 'text-muted hover:text-fg disabled:opacity-50 disabled:hover:text-muted'}"
          aria-pressed={page === each.key}
          disabled={!map}
          onclick={() => (page = each.key)}
        >
          {each.name}
        </button>
      {/each}
      <button
        type="button"
        class="rounded-[5px] px-2.5 py-1.5 text-muted opacity-50"
        disabled
        title={t("header.compareTitle")}
      >
        {t("header.compare")}
      </button>
    </div>
    <button
      type="button"
      class="shrink-0 rounded-[5px] bg-accent px-3 py-[5px] font-bold text-bg hover:bg-accent-hi disabled:opacity-40"
      disabled={!map || saving || !session?.dirty}
      title={t("header.saveTitle")}
      onclick={saveMap}
    >
      {saving ? t("header.saving") : t("header.save")}
    </button>
    <button
      type="button"
      class="shrink-0 rounded-[5px] p-1.5 text-muted hover:bg-hover hover:text-fg"
      title={t("header.settings")}
      aria-label={t("header.settings")}
      onclick={() => (settingsOpen = true)}
    >
      <Settings size={17} />
    </button>
  </header>

  {#if view === "missing" || view === "failed"}
    <main class="grid min-h-0 place-items-center" data-testid="first-start">
      <div class="flex max-w-lg flex-col gap-3 px-6">
        <h1 class="font-heading text-2xl font-bold text-fg">{t("welcome.title")}</h1>
        <p>
          {#each tParts("welcome.text") as part, index (index)}{#if part.slot === "file"}<span class="font-mono text-fg">war3.w3mod</span>{:else}{part.text}{/if}{/each}
        </p>
        {#if view === "failed" && data}
          <p class="text-warn" data-testid="game-data-error">
            {#each tParts("welcome.failed", { error: data.error ?? "" }) as part, index (index)}{#if part.slot === "folder"}<span class="font-mono">{data.source}</span>{:else}{part.text}{/if}{/each}
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
            {t("welcome.choose")}
          </button>
        </div>
      </div>
    </main>
  {:else if !map}
    <main class="grid min-h-0 place-items-center" data-testid="no-map">
      <div class="flex max-w-lg flex-col items-start gap-3 px-6">
        <h1 class="font-heading text-2xl font-bold text-fg">{t("noMap.title")}</h1>
        {#if view === "ready"}
          <p>{t("noMap.text")}</p>
        {:else}
          <p>{t("noMap.loading")}</p>
        {/if}
        {#if mapProblem}
          <p class="text-warn" data-testid="map-error">{mapProblem}</p>
        {/if}
        <OpenMapMenu
          label={opening ? t("noMap.opening") : t("noMap.open")}
          buttonClass="inline-flex items-center gap-2 rounded-md border border-accent bg-raised px-3 py-1.5 text-fg hover:bg-hover disabled:opacity-50"
          disabled={view !== "ready" || opening}
          onpick={openAnother}
        />
      </div>
    </main>
  {:else}
    {#if page === "table"}
      <main class="min-h-0" aria-label={t("main.table")}>
        <TableView
          bind:this={tableView}
          {kind}
          entries={trees[kind] ?? null}
          treeError={treeErrors[kind] ?? ""}
          selected={picked[kind] ?? null}
          bind:filter={treeFilter}
          bind:tableState
          {session}
          choices={choiceLists}
          {lookup}
          onneed={ensureTree}
          onopen={openInDetails}
          onedit={editCells}
          onnotice={(text, warn) => say(text, { warn })}
        />
      </main>
    {/if}
    <!-- The details view stays in the page while the table shows, so it comes back as it was. -->
    <main
      class="grid min-h-0 grid-cols-[290px_1fr_300px] max-[1100px]:grid-cols-[230px_1fr_240px] {page ===
      'table'
        ? 'hidden'
        : ''}"
    >
      <aside
        class="min-h-0 overflow-hidden border-r border-line bg-surface"
        aria-label={t("main.objects")}
        onpointerdown={() => (zone = "tree")}
        onfocusin={() => (zone = "tree")}
      >
        <Tree
          {kind}
          entries={trees[kind] ?? null}
          error={treeErrors[kind] ?? ""}
          selected={picked[kind] ?? null}
          onselect={(id) => show(kind, id)}
          oncontext={(id, x, y) => objectMenu(kind, id, x, y)}
          bind:filter={treeFilter}
          active={zone === "tree"}
        />
      </aside>
      <!-- The details panel scrolls its own fields, under a header and a bar that stay put. -->
      <section
        class="flex min-h-0 flex-col overflow-hidden"
        aria-label={t("main.details")}
        onpointerdown={() => (zone = "details")}
        onfocusin={() => (zone = "details")}
      >
        {#if mapProblem}
          <p class="mx-[18px] mt-3.5 shrink-0 text-warn" data-testid="map-error">{mapProblem}</p>
        {/if}
        {#if objectError}
          <p class="mx-[18px] mt-3.5 text-warn">{objectError}</p>
        {:else if current}
          <Details
            bind:this={details}
            view={shown ?? current}
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
            onmenu={(x, y, items) => (menu = { x, y, items })}
            onnotice={(text, warn) => say(text, { warn })}
            onfields={editFields}
            kindIcons={iconsOfKind}
          />
        {:else}
          <p class="mx-[18px] mt-3.5 text-muted">
            {t("main.pickObject")}
          </p>
        {/if}
      </section>
      <aside
        class="min-h-0 overflow-auto border-l border-line bg-surface p-3"
        aria-label={t("main.side")}
      >
        {#if current}
          <SidePanel
            view={shown ?? current}
            {lookup}
            onopen={(each, id) => show(each, id)}
            onedittooltip={hasTooltip(current.kind) && current.readOnly === null
              ? () => (editingTooltip = true)
              : undefined}
          />
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
    options={map?.options ?? null}
    {connection}
    {notice}
  />
</div>

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

{#if menu}
  <ContextMenu {...menu} onclose={() => (menu = null)} />
{/if}

{#if editingTooltip && current}
  <TooltipEditor view={shown ?? current} onfields={editFields} onclose={() => (editingTooltip = false)} />
{/if}

{#if creating}
  {@const asked = creating}
  <!-- "New Sorceress hsor" (Sven, 2026-10-27): the id is checked as it is typed. -->
  {@const typedProblem = idProblem(asked.kind, asked.id)}
  <Dialog
    title={asked.from
      ? t(asked.from.cut ? "dialog.new.move" : "dialog.new.paste", { name: asked.from.name })
      : t("dialog.new.title", { name: asked.baseName })}
    note={asked.from ? asked.from.id : asked.baseId}
    oncancel={() => (creating = null)}
  >
    <label class="flex items-center gap-2">
      {t("dialog.new.id")}
      <input
        class="w-28 rounded-[5px] border bg-bg px-2 py-1 font-mono text-fg outline-none focus:border-accent {asked.error ||
        typedProblem
          ? 'border-warn'
          : 'border-line'}"
        maxlength="4"
        spellcheck="false"
        bind:value={asked.id}
        oninput={() => (asked.error = "")}
        onkeydown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            if (!typedProblem) create();
          }
        }}
      />
    </label>
    {#if asked.error || typedProblem}
      <p class="mt-2 mb-0 text-[12.5px] text-warn" role="alert">{asked.error || typedProblem}</p>
    {/if}
    {#snippet actions()}
      {@render button(t("dialog.cancel"), () => (creating = null))}
      {@render button(
        asked.busy
          ? t("dialog.new.making")
          : asked.from
            ? t("dialog.new.pasteButton")
            : t("dialog.new.make"),
        () => !typedProblem && create(),
        "primary",
      )}
    {/snippet}
  </Dialog>
{/if}

{#if deleting}
  {@const asked = deleting}
  <Dialog title={t("dialog.delete.title", { name: asked.name })} oncancel={() => (deleting = null)}>
    <p class="m-0">
      {#each tParts("dialog.delete.text", { kind: asked.kind }) as part, index (index)}{#if part.slot === "name"}<b class="font-semibold text-fg">{asked.name}</b>{:else if part.slot === "id"}<span class="font-mono text-[12.5px] text-muted">{asked.id}</span>{:else}{part.text}{/if}{/each}
    </p>
    {#snippet actions()}
      {@render button(t("dialog.cancel"), () => (deleting = null))}
      {@render button(t("dialog.delete.button"), deleteAsked, "danger")}
    {/snippet}
  </Dialog>
{/if}

{#if confirmingReload}
  <Dialog title={t("dialog.reload.title")} oncancel={() => (confirmingReload = false)}>
    <p class="m-0">{t("dialog.reload.text")}</p>
    {#if session?.dirty}
      <p class="mt-2 mb-0 flex items-center gap-1.5 text-changed" data-testid="reload-unsaved">
        <span class="size-1.5 shrink-0 rounded-full bg-changed"></span>
        {tCount("dialog.reload.unsaved", session.changedObjects)}
      </p>
    {/if}
    {#snippet actions()}
      {@render button(t("dialog.cancel"), () => (confirmingReload = false))}
      {@render button(
        t("dialog.reload.button"),
        () => {
          confirmingReload = false;
          reloadFromDisk(session?.dirty ?? false);
        },
        "primary",
      )}
    {/snippet}
  </Dialog>
{/if}

{#if diskChanged}
  <Dialog title={t("dialog.diskChanged.title")} oncancel={() => (diskChanged = "")}>
    <p class="m-0">{diskChanged}</p>
    {#snippet actions()}
      {@render button(t("dialog.cancel"), () => (diskChanged = ""))}
      {@render button(
        t("dialog.diskChanged.reload"),
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
  <Dialog title={t("dialog.unsaved.title")} oncancel={() => asked.answer("cancel")}>
    <p class="m-0">
      {count > 0
        ? tCount("dialog.unsaved.textCount", count, { question: asked.question })
        : t("dialog.unsaved.text", { question: asked.question })}
    </p>
    {#snippet actions()}
      {@render button(t("dialog.cancel"), () => asked.answer("cancel"))}
      {@render button(
        t("dialog.unsaved.discard"),
        () => asked.answer("discard"),
        asked.canSave ? "plain" : "danger",
      )}
      {#if asked.canSave}
        {@render button(t("dialog.unsaved.save"), () => asked.answer("save"), "primary")}
      {/if}
    {/snippet}
  </Dialog>
{/if}

{#if settingsOpen}
  <SettingsDialog
    {theme}
    ontheme={useTheme}
    {keymap}
    onkeymap={(changed) => (keymap = changed)}
    {map}
    {data}
    onclose={() => (settingsOpen = false)}
  />
{/if}

{#if alphaOpen}
  <AlphaNotice onclose={() => (alphaOpen = false)} />
{/if}
