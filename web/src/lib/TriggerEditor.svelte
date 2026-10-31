<script lang="ts">
/**
 * The trigger editor (Sven, 2026-10-31): the map's categories, triggers, comments and variables at the left, and a
 * category's grid of cards at the right. A selected trigger shows on its category's grid. The app keeps the triggers and
 * works out their sentences; this view asks again after each edit. Parameters that name a thing on the map are picked
 * in the map view.
 */
import Braces from "@lucide/svelte/icons/braces";
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import FileCode from "@lucide/svelte/icons/file-code";
import Folder from "@lucide/svelte/icons/folder";
import FolderPlus from "@lucide/svelte/icons/folder-plus";
import MessageSquare from "@lucide/svelte/icons/message-square";
import MessageSquarePlus from "@lucide/svelte/icons/message-square-plus";
import Plus from "@lucide/svelte/icons/plus";
import SquareFunction from "@lucide/svelte/icons/square-function";
import Workflow from "@lucide/svelte/icons/workflow";
import { onMount, tick, untrack } from "svelte";
import ContextMenu, { type MenuItem } from "./ContextMenu.svelte";
import Dialog from "./Dialog.svelte";
import { t } from "./text.ts";
import {
  addFunction,
  addItem,
  addVariable,
  type BlockView,
  type CardMove,
  type CardView,
  type Catalog,
  changeFunction,
  copyFunction,
  deleteFunction,
  deleteItem,
  enableFunction,
  type FunctionKind,
  type FunctionPlace,
  type FunctionView,
  moveFunction,
  moveItem,
  type ParameterKind,
  type ParameterView,
  pasteFunction,
  renameItem,
  setCode,
  setFolded,
  setHeader,
  setOptions,
  setParameter,
  setPlaces,
  setVariable,
  type TreeItem,
  type TreeKind,
  type TriggerOptions,
  type TriggerTree,
  type TriggerView,
  triggerCards,
  triggerCatalog,
  trigger as triggerOf,
  triggerTree,
} from "./triggers/api.ts";
import type { CardActions } from "./triggers/cards.ts";
import FunctionDialog from "./triggers/FunctionDialog.svelte";
import {
  type DropPlace,
  dropPlaceAt,
  findParameter,
  treeDrop,
  treeRows,
} from "./triggers/lists.ts";
import ParameterDialog from "./triggers/ParameterDialog.svelte";
import TriggerCanvas from "./triggers/TriggerCanvas.svelte";
import { typedBox } from "./triggers/typedBox.ts";
import VariableForm from "./triggers/VariableForm.svelte";

type Props = {
  mode: string;
  /** Puts a command on the app's queue, so it runs after what is being sent. */
  queued: <T>(run: () => Promise<T>) => Promise<T>;
  onnotice: (text: string, warn?: boolean) => void;
};

let { mode, queued, onnotice }: Props = $props();

let catalog = $state.raw<Catalog | null>(null);
let tree = $state.raw<TriggerTree | null>(null);
let problem = $state("");
let selected = $state<number | null>(null);
// The category on the grid with its cards, which come after the selection moved: the grid takes the two at once.
let shown = $state.raw<{ parent: number; cards: CardView[] } | null>(null);
let flyTo = $state<{ id: number; count: number } | null>(null);
let flights = 0;
let commenting = $state<Record<number, boolean>>({});
let filter = $state("");
let toggled = $state<Record<number, boolean>>({});
let menu = $state<{ x: number; y: number; items: MenuItem[] } | null>(null);
// The kind of function copied: the app keeps the copy.
let clipboard = $state<FunctionKind | null>(null);
let adding = $state<{
  owner: number;
  title: string;
  kind: FunctionKind;
  place: FunctionPlace;
} | null>(null);
let changing = $state<{ owner: number; function_: FunctionView } | null>(null);
// The parameters open, each inside the one before it, of the card `owner`.
let parameters = $state<{ owner: number; paths: string[] }>({ owner: -1, paths: [] });
let picking = $state(false);
let deleting = $state<TreeItem | null>(null);
let editingVariable = $state<CardView | null>(null);
// The tree item renamed in its row.
let renaming = $state<number | null>(null);
// The tree item dragged, and where it would land.
let draggedItem = $state<TreeItem | null>(null);
let dropAt = $state<{ id: number; place: DropPlace } | null>(null);
// The function dragged, and where it would land: next to a function, or at a place of a block or a list.
let draggedFunction = $state.raw<{ owner: number; function_: FunctionView } | null>(null);
let functionDrop = $state.raw<{
  key: string;
  owner: number;
  target?: FunctionView;
  after?: boolean;
  place?: FunctionPlace;
} | null>(null);

const readOnly = $derived(tree?.readOnly ?? true);
const item = $derived(tree?.items.find((each) => each.id === selected) ?? null);
// The category whose grid shows: the one selected, or the one what is selected is in.
const gridParent = $derived(
  !item || item.kind === "root" ? null : item.kind === "category" ? item.id : item.parent,
);
const rows = $derived(tree ? treeRows(tree.items, isOpen, filter) : []);
const openParameter = $derived.by<ParameterView | null>(() => {
  const path = parameters.paths.at(-1);
  const trigger = triggerIn(parameters.owner);
  return path && trigger ? findParameter(trigger, path) : null;
});

function isOpen(each: TreeItem): boolean {
  return toggled[each.id] ?? each.expanded;
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function triggerIn(id: number): TriggerView | null {
  return shown?.cards.find((card) => card.id === id)?.trigger ?? null;
}

/** Asks for the tree and the grid again: after an edit, an undo, a save. */
export async function refresh(): Promise<void> {
  try {
    tree = await queued(triggerTree);
    const present = (id: number | null | undefined) => tree?.items.some((each) => each.id === id);
    // What was selected is gone: its category stays on the grid, if it is still there.
    if (selected !== null && !present(selected)) {
      selected = present(shown?.parent) ? (shown?.parent ?? null) : null;
    }

    await loadCards();
    problem = "";
  } catch (error) {
    problem = message(error);
  }
}

async function loadCards(): Promise<void> {
  const parent = gridParent;
  shown = parent === null ? null : { parent, cards: await queued(() => triggerCards(parent)) };
}

// Asks again for the one card an edit changed, which on a large grid is much less than all of them.
async function reloadCard(id: number): Promise<void> {
  const fresh = await queued(() => triggerOf(id));
  if (shown) {
    const cards = shown.cards.map((card) =>
      card.id === id ? { ...card, name: fresh.name, trigger: fresh } : card,
    );
    shown = { parent: shown.parent, cards };
  }
}

// Opens the categories an item is in, so its row shows.
function reveal(id: number): void {
  let at = tree?.items.find((each) => each.id === id);
  while (at && at.parent >= 0) {
    const up = at.parent;
    toggled[up] = true;
    at = tree?.items.find((each) => each.id === up);
  }
}

async function select(id: number, fly: boolean): Promise<void> {
  const before = gridParent;
  selected = id;
  reveal(id);
  try {
    if (gridParent !== before) await loadCards();
    if (fly && gridParent !== id) flyTo = { id, count: ++flights };
  } catch (error) {
    onnotice(message(error), true);
  }
}

// Makes an edit, then shows the triggers as the app has them: only the card `owner` when it is given. True when it worked.
async function edit(change: () => Promise<unknown>, owner?: number): Promise<boolean> {
  try {
    await queued(change);
    if (owner === undefined) await refresh();
    else await reloadCard(owner);
    return true;
  } catch (error) {
    onnotice(message(error), true);
    await refresh();
    return false;
  }
}

function targetCategory(): number {
  if (!tree) return -1;
  const at = tree.items.find((each) => each.id === selected);
  if (at?.kind === "category" || at?.kind === "root") return at.id;
  return at?.parent ?? tree.items.find((each) => each.kind === "root")?.id ?? -1;
}

async function add(kind: TreeKind): Promise<void> {
  const parent = targetCategory();
  let made = -1;
  if (await edit(async () => (made = await addItem(kind, parent)))) await select(made, true);
}

async function addNewVariable(): Promise<void> {
  const parent = targetCategory();
  const taken = new Set(tree?.variables.map((each) => each.name.toLowerCase()));
  let number = 1;
  while (taken.has(`variable_${String(number).padStart(3, "0")}`)) number++;
  const name = `Variable_${String(number).padStart(3, "0")}`;
  let made = -1;
  const input = { name, type: "integer", isArray: false, size: 1, initial: "" };
  if (await edit(async () => (made = await addVariable(input, parent)))) await select(made, true);
}

function confirmDelete(): void {
  const each = deleting;
  deleting = null;
  if (each) edit(() => deleteItem(each.id));
}

/** Starts renaming what is selected in its row (F2). */
export function startRename(): void {
  const shown = tree?.items.find((each) => each.id === selected);
  if (shown && shown.kind !== "root" && !readOnly) renaming = shown.id;
}

// The rename box takes the cursor with its name selected; Enter or leaving it renames, Esc gives up.
function focusRename(input: HTMLInputElement): void {
  input.focus();
  input.select();
}

function finishRename(each: TreeItem, name: string | null): void {
  if (renaming !== each.id) return;
  renaming = null;
  const typed = name?.trim();
  if (!typed || typed === each.name) return;
  const variable = tree?.variables.find((other) => other.id === each.id);
  if (each.kind === "variable" && variable) {
    const { id, ...fields } = variable;
    edit(() => setVariable(id, { ...fields, name: typed }));
  } else {
    edit(() => renameItem(each.id, typed));
  }
}

// How long a drag rests on a closed category before it opens.
const openAfter = 600;
let opening: ReturnType<typeof setTimeout> | undefined;

function rowDragOver(event: DragEvent, row: TreeItem): void {
  const dragged = draggedItem;
  if (!dragged || !tree) return;
  const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
  const holds = row.kind === "category" || row.kind === "root";
  const place = dropPlaceAt(event.clientY - box.top, box.height, holds);
  const found = treeDrop(tree.items, dragged, row, place, isOpen);
  if (!found) {
    dropAt = null;
    return;
  }

  event.preventDefault();
  if (dropAt?.id !== row.id || dropAt.place !== place) {
    clearTimeout(opening);
    if (place === "inside" && row.kind === "category" && !isOpen(row)) {
      opening = setTimeout(() => (toggled[row.id] = true), openAfter);
    }
  }

  dropAt = { id: row.id, place };
}

function rowDrop(event: DragEvent, row: TreeItem): void {
  event.preventDefault();
  const dragged = draggedItem;
  const place = dropAt?.id === row.id ? dropAt.place : null;
  endDrag();
  if (!dragged || !tree || !place) return;
  const found = treeDrop(tree.items, dragged, row, place, isOpen);
  if (found) {
    toggled[found.parent] = true;
    edit(() => moveItem(dragged.id, found.parent, found.index));
  }
}

function endDrag(): void {
  clearTimeout(opening);
  draggedItem = null;
  dropAt = null;
  draggedFunction = null;
  functionDrop = null;
}

// True when the function dragged may go next to or into `path` of the card `owner`: not inside itself.
function outsideDragged(owner: number, path: string): boolean {
  const dragged = draggedFunction;
  if (!dragged) return false;
  if (dragged.owner !== owner) return true;
  return path !== dragged.function_.path && !path.startsWith(`${dragged.function_.path}/`);
}

function overFunction(event: DragEvent, owner: number, target: FunctionView): void {
  if (draggedFunction?.function_.kind !== target.kind || !outsideDragged(owner, target.path))
    return;
  event.preventDefault();
  const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
  const after = event.clientY - box.top > box.height / 2;
  functionDrop = {
    key: `${owner}:${target.path}:${after ? "after" : "before"}`,
    owner,
    target,
    after,
  };
}

function overBlock(event: DragEvent, owner: number, parent: FunctionView, block: BlockView): void {
  if (draggedFunction?.function_.kind !== block.kind || !outsideDragged(owner, parent.path)) return;
  event.preventDefault();
  functionDrop = {
    key: `${owner}:${parent.path}:block${block.branch}`,
    owner,
    place: blockPlace(parent, block),
  };
}

function overSection(event: DragEvent, owner: number, kind: FunctionKind): void {
  if (draggedFunction?.function_.kind !== kind) return;
  event.preventDefault();
  functionDrop = {
    key: `${owner}:section:${kind}`,
    owner,
    place: { parent: null, branch: 0, index: atEnd },
  };
}

function dropOn(event: DragEvent): void {
  event.preventDefault();
  const dragged = draggedFunction;
  const at = functionDrop;
  endDrag();
  if (!dragged || !at) return;
  let place = at.place;
  if (at.target) {
    const target = at.target;
    const siblings = siblingsOf(at.owner, target).filter(
      (each) => dragged.owner !== at.owner || each.path !== dragged.function_.path,
    );
    const index = siblings.findIndex((each) => each.path === target.path) + (at.after ? 1 : 0);
    place = { parent: parentOf(target), branch: target.branch ?? 0, index };
  }

  if (place) {
    const to = place;
    const sameCard = dragged.owner === at.owner;
    edit(
      () => moveFunction(dragged.owner, dragged.function_.path, at.owner, to),
      sameCard ? at.owner : undefined,
    );
  }
}

// The path of the function a function is in; null for one in a trigger's own list.
function parentOf(function_: FunctionView): string | null {
  const parts = function_.path.split("/");
  return parts.length > 1 ? parts.slice(0, -1).join("/") : null;
}

function itemMenu(each: TreeItem, x: number, y: number, extra: MenuItem[] = []): void {
  const creating: MenuItem[] = [
    { label: t("triggers.new.category"), disabled: readOnly, run: () => add("category") },
    { label: t("triggers.new.trigger"), disabled: readOnly, run: () => add("trigger") },
    { label: t("triggers.new.script"), disabled: readOnly, run: () => add("script") },
    { label: t("triggers.new.comment"), disabled: readOnly, run: () => add("comment") },
    { label: t("triggers.new.variable"), disabled: readOnly, run: addNewVariable },
  ];
  menu = {
    x,
    y,
    items:
      each.kind === "root"
        ? creating
        : [
            ...creating,
            { separator: true },
            {
              label: t("menu.rename"),
              shortcut: "F2",
              disabled: readOnly,
              run: () => {
                reveal(each.id);
                renaming = each.id;
              },
            },
            ...extra,
            {
              label: t("triggers.delete"),
              danger: true,
              disabled: readOnly,
              run: () => (deleting = each),
            },
          ],
  };
}

function cardMenu(card: CardView, x: number, y: number): void {
  const each = tree?.items.find((other) => other.id === card.id);
  if (!each) return;
  select(card.id, false);
  const trigger = card.trigger;
  const extra: MenuItem[] = [];
  if (trigger && (card.kind === "trigger" || card.kind === "custom")) {
    extra.push({
      label: t("triggers.comment"),
      disabled: readOnly,
      run: () => (commenting[card.id] = true),
    });
  }

  if (trigger && card.kind === "custom") {
    extra.push({
      label: t("triggers.runOnInit"),
      shortcut: trigger.runOnInit ? "✓" : undefined,
      disabled: readOnly,
      run: () => options(card.id, { runOnInit: !trigger.runOnInit }),
    });
  }

  itemMenu(each, x, y, extra);
}

function gridMenu(x: number, y: number): void {
  const each = tree?.items.find((other) => other.id === gridParent);
  if (each) {
    const items: MenuItem[] = [
      { label: t("triggers.new.trigger"), disabled: readOnly, run: () => add("trigger") },
      { label: t("triggers.new.script"), disabled: readOnly, run: () => add("script") },
      { label: t("triggers.new.comment"), disabled: readOnly, run: () => add("comment") },
      { label: t("triggers.new.variable"), disabled: readOnly, run: addNewVariable },
      { label: t("triggers.new.category"), disabled: readOnly, run: () => add("category") },
    ];
    selected = each.id;
    menu = { x, y, items };
  }
}

// A place after everything in a list.
const atEnd = 2147483647;

function addTo(owner: number, kind: FunctionKind, place: FunctionPlace): void {
  const title =
    kind === "event"
      ? t("triggers.addEvent")
      : kind === "condition"
        ? t("triggers.addCondition")
        : t("triggers.addAction");
  adding = { owner, title, kind, place };
}

function blockPlace(parent: FunctionView, block: BlockView): FunctionPlace {
  return { parent: parent.path, branch: block.branch, index: atEnd };
}

async function pickedFunction(name: string): Promise<void> {
  const asked = adding;
  const changed = changing;
  adding = null;
  changing = null;
  if (asked) await edit(() => addFunction(asked.owner, asked.place, asked.kind, name), asked.owner);
  else if (changed) {
    const { owner, function_ } = changed;
    await edit(() => changeFunction(owner, function_.path, name), owner);
  }
}

// The functions next to a function in its list: a trigger's of its kind, or a block's.
function siblingsOf(owner: number, function_: FunctionView): FunctionView[] {
  const trigger = triggerIn(owner);
  if (!trigger) return [];
  const search = (lists: FunctionView[][]): FunctionView[] | null => {
    for (const list of lists) {
      if (list.some((each) => each.path === function_.path)) return list;
      for (const each of list) {
        const found = search(each.blocks.map((block) => block.functions));
        if (found) return found;
      }
    }
    return null;
  };
  return search([trigger.events, trigger.conditions, trigger.actions]) ?? [];
}

function functionMenu(owner: number, function_: FunctionView, x: number, y: number): void {
  const at = siblingsOf(owner, function_).findIndex((each) => each.path === function_.path);
  menu = {
    x,
    y,
    items: [
      {
        label: t("triggers.changeFunction"),
        disabled: readOnly,
        run: () => (changing = { owner, function_ }),
      },
      { separator: true },
      {
        label: t("triggers.copy"),
        run: () => copy(owner, function_),
      },
      {
        label: t("triggers.paste"),
        disabled: readOnly || clipboard !== function_.kind,
        run: () => paste(owner, function_, at + 1),
      },
      { separator: true },
      {
        label: function_.enabled ? t("triggers.disable") : t("triggers.enable"),
        disabled: readOnly,
        run: () => edit(() => enableFunction(owner, function_.path, !function_.enabled), owner),
      },
      { separator: true },
      {
        label: t("triggers.delete"),
        danger: true,
        disabled: readOnly,
        run: () => edit(() => deleteFunction(owner, function_.path), owner),
      },
    ],
  };
}

async function copy(owner: number, function_: FunctionView): Promise<void> {
  try {
    await queued(() => copyFunction(owner, function_.path));
    clipboard = function_.kind;
  } catch (error) {
    onnotice(message(error), true);
  }
}

function paste(owner: number, near: FunctionView, index: number): void {
  const place = { parent: parentOf(near), branch: near.branch ?? 0, index };
  edit(() => pasteFunction(owner, place), owner);
}

function pasteInto(owner: number, kind: FunctionKind, place: FunctionPlace): void {
  if (clipboard === kind) edit(() => pasteFunction(owner, place), owner);
}

function blockMenu(
  owner: number,
  parent: FunctionView,
  block: BlockView,
  x: number,
  y: number,
): void {
  menu = {
    x,
    y,
    items: [
      {
        label: block.kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction"),
        disabled: readOnly,
        run: () => addTo(owner, block.kind, blockPlace(parent, block)),
      },
      {
        label: t("triggers.paste"),
        disabled: readOnly || clipboard !== block.kind,
        run: () => pasteInto(owner, block.kind, blockPlace(parent, block)),
      },
    ],
  };
}

async function setParameterValue(path: string, kind: ParameterKind, value: string): Promise<void> {
  const owner = parameters.owner;
  await queued(() => setParameter(owner, path, kind, value));
  await reloadCard(owner);
}

async function setPoint(path: string, x: number, y: number): Promise<void> {
  const owner = parameters.owner;
  await queued(async () => {
    await setParameter(owner, path, "function", "Location");
    await setParameter(owner, `${path}/p0`, "value", x.toFixed(1));
    await setParameter(owner, `${path}/p1`, "value", y.toFixed(1));
  });
  await reloadCard(owner);
}

function options(id: number, change: TriggerOptions): void {
  edit(() => setOptions(id, change));
}

function inEvent(path: string): boolean {
  const first = Number(path.split("/")[0]);
  return triggerIn(parameters.owner)?.events.some((each) => Number(each.path) === first) ?? false;
}

const cardActions: CardActions = {
  parameter: (owner, parameter) => (parameters = { owner, paths: [parameter.path] }),
  change: (owner, function_) => (changing = { owner, function_ }),
  functionMenu,
  add: (owner, kind) => addTo(owner, kind, { parent: null, branch: 0, index: atEnd }),
  addToBlock: (owner, parent, block) => addTo(owner, block.kind, blockPlace(parent, block)),
  blockMenu,
  dragFunction: (owner, function_) => (draggedFunction = { owner, function_ }),
  overFunction,
  overBlock,
  overSection,
  dropFunction: dropOn,
  dragEnd: endDrag,
  select: (id) => select(id, false),
  open: (id) => select(id, true),
  cardMenu,
  options,
  code: (id, text) => edit(() => setCode(id, text), id),
  variable: (card) => (editingVariable = card),
};

// Shows where the cards now stand, then tells the app: on a large grid it takes a moment, and nothing else changes.
async function place(
  places: CardMove[],
  change: () => Promise<unknown>,
  fold?: { id: number; folded: boolean },
): Promise<void> {
  if (shown) {
    const cards = shown.cards.map((card) => {
      const at = places.find((each) => each.id === card.id);
      const folded = card.id === fold?.id ? fold.folded : (card.place?.folded ?? false);
      return at ? { ...card, place: { x: at.x, y: at.y, folded } } : card;
    });
    shown = { parent: shown.parent, cards };
  }

  try {
    await queued(change);
  } catch (error) {
    onnotice(message(error), true);
    await refresh();
  }
}

function moved(places: CardMove[]): void {
  place(places, () => setPlaces(places));
}

function folded(id: number, value: boolean, places: CardMove[]): void {
  place(places, () => setFolded(id, value, places), { id, folded: value });
}

onMount(() => {
  untrack(async () => {
    try {
      catalog = await queued(triggerCatalog);
      await refresh();
      const root = tree?.items.find((each) => each.kind === "root");
      if (root && selected === null) await select(root.id, false);
    } catch (error) {
      problem = message(error);
    }
    await tick();
  });
});

const iconOf: Record<TreeKind, typeof Folder> = {
  root: FileCode,
  category: Folder,
  trigger: Workflow,
  custom: FileCode,
  script: FileCode,
  comment: MessageSquare,
  variable: Braces,
};
const toolButton =
  "rounded-[5px] p-1.5 text-muted hover:bg-hover hover:text-fg disabled:opacity-50 disabled:hover:bg-transparent";
const box =
  "w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent disabled:opacity-60";
const code = `${box} min-h-[320px] flex-1 resize-none font-mono text-[12.5px] leading-[1.45]`;
</script>

<div class="grid h-full min-h-0 grid-cols-[320px_1fr] max-[1100px]:grid-cols-[250px_1fr]" data-testid="trigger-editor">
  <aside class="flex min-h-0 flex-col border-r border-line bg-surface" aria-label={t("triggers.tree")}>
    <div class="flex items-center gap-0.5 border-b border-line px-2 py-1">
      <button type="button" class={toolButton} disabled={readOnly} title={t("triggers.new.category")} aria-label={t("triggers.new.category")} onclick={() => add("category")}>
        <FolderPlus size={16} />
      </button>
      <button type="button" class={toolButton} disabled={readOnly} title={t("triggers.new.trigger")} aria-label={t("triggers.new.trigger")} onclick={() => add("trigger")}>
        <Plus size={16} />
      </button>
      <button type="button" class={toolButton} disabled={readOnly} title={t("triggers.new.script")} aria-label={t("triggers.new.script")} onclick={() => add("script")}>
        <FileCode size={16} />
      </button>
      <button type="button" class={toolButton} disabled={readOnly} title={t("triggers.new.comment")} aria-label={t("triggers.new.comment")} onclick={() => add("comment")}>
        <MessageSquarePlus size={16} />
      </button>
      <button type="button" class={toolButton} disabled={readOnly} title={t("triggers.new.variable")} aria-label={t("triggers.new.variable")} onclick={addNewVariable}>
        <SquareFunction size={16} />
      </button>
    </div>
    <div class="border-b border-line p-2">
      <input class={box} placeholder={t("triggers.filter")} aria-label={t("triggers.filter")} bind:value={filter} />
    </div>
    <div class="min-h-0 flex-1 overflow-auto py-1" role="tree" aria-label={t("triggers.tree")}>
      {#each rows as row (row.item.id)}
        {@const Icon = iconOf[row.item.kind]}
        <div
          class="flex h-[26px] items-center gap-1 pr-2 {dropAt?.id === row.item.id && dropAt.place === 'inside' ? 'bg-accent-lo text-fg outline-1 -outline-offset-1 outline-accent' : selected === row.item.id ? 'bg-accent-lo text-fg' : 'text-body hover:bg-hover'}"
          class:shadow-[inset_0_2px_0_var(--accent)]={dropAt?.id === row.item.id && dropAt.place === "before"}
          class:shadow-[inset_0_-2px_0_var(--accent)]={dropAt?.id === row.item.id && dropAt.place === "after"}
          style:padding-left={`${row.depth * 16 + 4}px`}
          role="treeitem"
          aria-selected={selected === row.item.id}
          aria-expanded={row.children ? isOpen(row.item) : undefined}
          tabindex="-1"
          draggable={!readOnly && row.item.kind !== "root" && renaming !== row.item.id}
          onclick={() => select(row.item.id, true)}
          onkeydown={(event) => event.key === "Enter" && renaming !== row.item.id && select(row.item.id, true)}
          oncontextmenu={(event) => {
            event.preventDefault();
            select(row.item.id, false);
            itemMenu(row.item, event.clientX, event.clientY);
          }}
          ondragstart={(event) => {
            draggedItem = row.item;
            if (event.dataTransfer) {
              event.dataTransfer.setData("text/plain", row.item.name);
              event.dataTransfer.effectAllowed = "move";
            }
          }}
          ondragover={(event) => rowDragOver(event, row.item)}
          ondragleave={() => {
            clearTimeout(opening);
            dropAt = null;
          }}
          ondrop={(event) => rowDrop(event, row.item)}
          ondragend={endDrag}
        >
          {#if row.children && row.item.kind !== "root"}
            <button
              type="button"
              class="shrink-0 text-muted hover:text-fg"
              aria-label={row.item.name}
              onclick={(event) => {
                event.stopPropagation();
                toggled[row.item.id] = !isOpen(row.item);
              }}
            >
              {#if isOpen(row.item)}<ChevronDown size={14} />{:else}<ChevronRight size={14} />{/if}
            </button>
          {:else}
            <span class="w-[14px] shrink-0"></span>
          {/if}
          <Icon size={15} class="shrink-0 {row.item.enabled ? 'text-muted' : 'text-faint'}" />
          {#if renaming === row.item.id}
            <input
              class="min-w-0 flex-1 rounded-[3px] border border-accent bg-bg px-1 py-0 text-fg outline-none"
              value={row.item.name}
              aria-label={t("triggers.name")}
              spellcheck="false"
              {@attach focusRename}
              onclick={(event) => event.stopPropagation()}
              onkeydown={(event) => {
                event.stopPropagation();
                if (event.key === "Enter") finishRename(row.item, event.currentTarget.value);
                else if (event.key === "Escape") finishRename(row.item, null);
              }}
              onblur={(event) => finishRename(row.item, event.currentTarget.value)}
            />
          {:else}
            <span class="truncate {row.item.enabled ? '' : 'text-faint'}">{row.item.kind === "root" ? t("triggers.mapHeader") : row.item.name}</span>
          {/if}
        </div>
      {/each}
    </div>
  </aside>

  {#if problem}
    <div class="bg-bg p-4"><p class="m-0 text-warn">{problem}</p></div>
  {:else if !tree || !catalog}
    <div class="bg-bg p-4"><p class="m-0 text-muted">{t("triggers.loading")}</p></div>
  {:else if shown}
    <div class="flex min-h-0 flex-col">
      {#if tree.problem}
        <p class="m-0 border-b border-line bg-surface px-3 py-1 text-warn">{tree.problem}</p>
      {/if}
      <div class="min-h-0 flex-1">
        <TriggerCanvas
          parent={shown.parent}
          cards={shown.cards}
          {catalog}
          {mode}
          {readOnly}
          {selected}
          {flyTo}
          {commenting}
          dropKey={functionDrop?.key ?? null}
          actions={cardActions}
          onmoved={moved}
          onfold={folded}
          onmenu={gridMenu}
        />
      </div>
    </div>
  {:else if item?.kind === "root"}
    <div class="flex min-h-0 flex-col overflow-auto bg-bg p-4">
      {#if tree.problem}
        <p class="m-0 mb-3 text-warn">{tree.problem}</p>
      {/if}
      <h2 class="m-0 mb-1 font-heading text-lg font-bold text-fg">{t("triggers.mapHeader")}</h2>
      <p class="m-0 mb-2 text-muted">{t("triggers.mapHeaderHint")}</p>
      <label class="mb-2">
        <span class="mb-1 block text-muted">{t("triggers.comment")}</span>
        <textarea class="{box} h-[64px] resize-none" value={tree.headerComment} disabled={readOnly} use:typedBox={(comment) => { const header = tree?.headerCode ?? ""; edit(() => setHeader(comment, header)); }}></textarea>
      </label>
      <label class="flex min-h-0 flex-1 flex-col">
        <span class="mb-1 block text-muted">{t("triggers.code")}</span>
        <textarea class={code} spellcheck="false" value={tree.headerCode} disabled={readOnly} use:typedBox={(text) => { const comment = tree?.headerComment ?? ""; edit(() => setHeader(comment, text)); }}></textarea>
      </label>
    </div>
  {:else if gridParent === null}
    <div class="bg-bg p-4"><p class="m-0 text-muted">{t("triggers.nothingSelected")}</p></div>
  {/if}
</div>

{#if editingVariable?.variable && catalog}
  {@const variable = editingVariable.variable}
  <Dialog title={variable.name} oncancel={() => (editingVariable = null)}>
    <VariableForm
      {catalog}
      value={{ name: variable.name, type: variable.type, isArray: variable.isArray, size: variable.size, initial: variable.initial }}
      {readOnly}
      onsave={async (input) => {
        if (await edit(() => setVariable(variable.id, input))) editingVariable = null;
      }}
    />
    {#snippet actions()}
      <button type="button" class="rounded-md border border-line bg-raised px-3 py-1.5 text-fg hover:bg-hover" onclick={() => (editingVariable = null)}>
        {t("triggers.close")}
      </button>
    {/snippet}
  </Dialog>
{/if}
{#if deleting}
  <Dialog title={t("triggers.delete")} oncancel={() => (deleting = null)}>
    {deleting.kind === "category"
      ? t("triggers.deleteAskCategory", { name: deleting.name })
      : t("triggers.deleteAsk", { name: deleting.name })}
    {#snippet actions()}
      <button type="button" class="rounded-md border border-line bg-raised px-3 py-1.5 text-fg hover:bg-hover" onclick={() => (deleting = null)}>
        {t("triggers.close")}
      </button>
      <button type="button" data-primary class="rounded-md border border-accent bg-raised px-3 py-1.5 text-fg hover:bg-hover" onclick={confirmDelete}>
        {t("triggers.delete")}
      </button>
    {/snippet}
  </Dialog>
{/if}
{#if menu}
  <ContextMenu x={menu.x} y={menu.y} items={menu.items} onclose={() => (menu = null)} />
{/if}
{#if catalog && adding}
  <FunctionDialog {catalog} kind={adding.kind} title={adding.title} onpick={pickedFunction} oncancel={() => (adding = null)} />
{:else if catalog && changing}
  <FunctionDialog {catalog} kind={changing.function_.kind} title={t("triggers.function.change")} current={changing.function_.name} onpick={pickedFunction} oncancel={() => (changing = null)} />
{/if}
{#if catalog && tree && openParameter && !picking}
  <ParameterDialog
    parameter={openParameter}
    {catalog}
    variables={tree.variables}
    references={tree.references}
    items={tree.items}
    inEvent={inEvent(openParameter.path)}
    onset={setParameterValue}
    onpoint={setPoint}
    onopen={(inner) => (parameters = { owner: parameters.owner, paths: [...parameters.paths, inner.path] })}
    onpicking={(now) => (picking = now)}
    onclose={() => (parameters = { owner: parameters.owner, paths: parameters.paths.slice(0, -1) })}
  />
{/if}
