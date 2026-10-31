<script lang="ts">
/**
 * The trigger editor (Sven, 2026-10-31): the map's categories, triggers, comments and variables at the left, and what
 * is selected at the right. The app keeps the triggers and works out their sentences; this view asks again after each
 * edit. Parameters that name a thing on the map are picked in the map view.
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
  setHeader,
  setOptions,
  setParameter,
  setVariable,
  type TreeItem,
  type TreeKind,
  type TriggerOptions,
  type TriggerTree,
  type TriggerView,
  triggerCatalog,
  trigger as triggerOf,
  triggerTree,
  type VariableInput,
} from "./triggers/api.ts";
import FunctionDialog from "./triggers/FunctionDialog.svelte";
import FunctionRow from "./triggers/FunctionRow.svelte";
import { findParameter, treeRows } from "./triggers/lists.ts";
import ParameterDialog from "./triggers/ParameterDialog.svelte";
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
let view = $state.raw<TriggerView | null>(null);
let selectedFunction = $state<string | null>(null);
let filter = $state("");
let toggled = $state<Record<number, boolean>>({});
let menu = $state<{ x: number; y: number; items: MenuItem[] } | null>(null);
// The kind of function copied: the app keeps the copy.
let clipboard = $state<FunctionKind | null>(null);
let adding = $state<{ title: string; kind: FunctionKind; place: FunctionPlace } | null>(null);
let changing = $state<FunctionView | null>(null);
// The parameters open, each inside the one before it.
let parameters = $state<string[]>([]);
let picking = $state(false);
let deleting = $state<TreeItem | null>(null);

const readOnly = $derived(tree?.readOnly ?? true);
const item = $derived(tree?.items.find((each) => each.id === selected) ?? null);
const variable = $derived(
  item?.kind === "variable" ? tree?.variables.find((each) => each.id === item.id) : null,
);
const variableInput = $derived<VariableInput | null>(
  variable
    ? {
        name: variable.name,
        type: variable.type,
        isArray: variable.isArray,
        size: variable.size,
        initial: variable.initial,
      }
    : null,
);
const rows = $derived(tree ? treeRows(tree.items, isOpen, filter) : []);
const openParameter = $derived.by<ParameterView | null>(() => {
  const path = parameters.at(-1);
  return path && view ? findParameter(view, path) : null;
});

function isOpen(each: TreeItem): boolean {
  return toggled[each.id] ?? each.expanded;
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Asks for the tree and what is selected again: after an edit, an undo, a save. */
export async function refresh(): Promise<void> {
  try {
    tree = await queued(triggerTree);
    if (selected !== null && !tree.items.some((each) => each.id === selected)) selected = null;
    await showSelected();
    problem = "";
  } catch (error) {
    problem = message(error);
  }
}

async function showSelected(): Promise<void> {
  const shown = selected === null ? undefined : tree?.items.find((each) => each.id === selected);
  const id = selected;
  if (id !== null && shown && ["trigger", "custom", "script", "comment"].includes(shown.kind)) {
    view = await queued(() => triggerOf(id));
  } else {
    view = null;
  }
}

async function select(id: number): Promise<void> {
  selected = id;
  selectedFunction = null;
  try {
    await showSelected();
  } catch (error) {
    onnotice(message(error), true);
  }
}

// Makes an edit, then shows the triggers as the app has them. True when it worked.
async function edit(change: () => Promise<unknown>): Promise<boolean> {
  try {
    await queued(change);
    await refresh();
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
  if (await edit(async () => (made = await addItem(kind, parent)))) {
    toggled[parent] = true;
    await refresh();
    await select(made);
  }
}

async function addNewVariable(): Promise<void> {
  const parent = targetCategory();
  const taken = new Set(tree?.variables.map((each) => each.name.toLowerCase()));
  let number = 1;
  while (taken.has(`variable_${String(number).padStart(3, "0")}`)) number++;
  const name = `Variable_${String(number).padStart(3, "0")}`;
  let made = -1;
  const input = { name, type: "integer", isArray: false, size: 1, initial: "" };
  if (await edit(async () => (made = await addVariable(input, parent)))) {
    toggled[parent] = true;
    await refresh();
    await select(made);
  }
}

function confirmDelete(): void {
  const each = deleting;
  deleting = null;
  if (each) edit(() => deleteItem(each.id));
}

function move(each: TreeItem, by: number): void {
  const siblings =
    tree?.items.filter((other) => other.parent === each.parent && other.kind !== "root") ?? [];
  const to = siblings.findIndex((other) => other.id === each.id) + by;
  if (to >= 0 && to < siblings.length) {
    edit(() => moveItem(each.id, each.parent, to));
  }
}

function itemMenu(each: TreeItem, x: number, y: number): void {
  select(each.id);
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
            { label: t("triggers.moveUp"), disabled: readOnly, run: () => move(each, -1) },
            { label: t("triggers.moveDown"), disabled: readOnly, run: () => move(each, 1) },
            { separator: true },
            {
              label: t("triggers.delete"),
              danger: true,
              disabled: readOnly,
              run: () => (deleting = each),
            },
          ],
  };
}

// A place after everything in a list.
const atEnd = 2147483647;

function addTo(kind: FunctionKind, place: FunctionPlace): void {
  const title =
    kind === "event"
      ? t("triggers.addEvent")
      : kind === "condition"
        ? t("triggers.addCondition")
        : t("triggers.addAction");
  adding = { title, kind, place };
}

function addToTrigger(kind: FunctionKind): void {
  addTo(kind, { parent: null, branch: 0, index: atEnd });
}

function blockPlace(parent: FunctionView, block: BlockView): FunctionPlace {
  return { parent: parent.path, branch: block.branch, index: atEnd };
}

async function pickedFunction(name: string): Promise<void> {
  const asked = adding;
  const changed = changing;
  const id = selected;
  adding = null;
  changing = null;
  if (id === null) return;
  if (asked) await edit(() => addFunction(id, asked.place, asked.kind, name));
  else if (changed) await edit(() => changeFunction(id, changed.path, name));
}

// The functions next to a function in its list: a trigger's of its kind, or a block's.
function siblingsOf(function_: FunctionView): FunctionView[] {
  if (!view) return [];
  const direct = [view.events, view.conditions, view.actions];
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
  return search(direct) ?? [];
}

function functionMenu(function_: FunctionView, x: number, y: number): void {
  const id = selected;
  if (id === null) return;
  const siblings = siblingsOf(function_);
  const at = siblings.findIndex((each) => each.path === function_.path);
  menu = {
    x,
    y,
    items: [
      {
        label: t("triggers.changeFunction"),
        disabled: readOnly,
        run: () => (changing = function_),
      },
      { separator: true },
      {
        label: t("triggers.copy"),
        run: () => copy(id, function_),
      },
      {
        label: t("triggers.paste"),
        disabled: readOnly || clipboard !== function_.kind,
        run: () => paste(function_, at + 1),
      },
      { separator: true },
      {
        label: function_.enabled ? t("triggers.disable") : t("triggers.enable"),
        disabled: readOnly,
        run: () => edit(() => enableFunction(id, function_.path, !function_.enabled)),
      },
      {
        label: t("triggers.moveUp"),
        disabled: readOnly || at <= 0,
        run: () => edit(() => moveFunction(id, function_.path, at - 1)),
      },
      {
        label: t("triggers.moveDown"),
        disabled: readOnly || at < 0 || at >= siblings.length - 1,
        run: () => edit(() => moveFunction(id, function_.path, at + 1)),
      },
      { separator: true },
      {
        label: t("triggers.delete"),
        danger: true,
        disabled: readOnly,
        run: () => edit(() => deleteFunction(id, function_.path)),
      },
    ],
  };
}

async function copy(id: number, function_: FunctionView): Promise<void> {
  try {
    await queued(() => copyFunction(id, function_.path));
    clipboard = function_.kind;
  } catch (error) {
    onnotice(message(error), true);
  }
}

function paste(near: FunctionView, index: number): void {
  const id = selected;
  if (id === null) return;
  const parts = near.path.split("/");
  const inBlock = parts.length > 1;
  const parent = inBlock ? parts.slice(0, -1).join("/") : null;
  const branch = near.branch ?? 0;
  edit(() => pasteFunction(id, { parent, branch, index }));
}

function pasteInto(kind: FunctionKind, place: FunctionPlace): void {
  const id = selected;
  if (id === null || clipboard !== kind) return;
  edit(() => pasteFunction(id, place));
}

function blockMenu(parent: FunctionView, block: BlockView, x: number, y: number): void {
  menu = {
    x,
    y,
    items: [
      {
        label: block.kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction"),
        disabled: readOnly,
        run: () => addTo(block.kind, blockPlace(parent, block)),
      },
      {
        label: t("triggers.paste"),
        disabled: readOnly || clipboard !== block.kind,
        run: () => pasteInto(block.kind, blockPlace(parent, block)),
      },
    ],
  };
}

async function setParameterValue(path: string, kind: ParameterKind, value: string): Promise<void> {
  const id = selected;
  if (id !== null) {
    await queued(() => setParameter(id, path, kind, value));
    await refresh();
  }
}

async function setPoint(path: string, x: number, y: number): Promise<void> {
  const id = selected;
  if (id === null) return;
  await queued(async () => {
    await setParameter(id, path, "function", "Location");
    await setParameter(id, `${path}/p0`, "value", x.toFixed(1));
    await setParameter(id, `${path}/p1`, "value", y.toFixed(1));
  });
  await refresh();
}

function options(id: number, change: TriggerOptions): void {
  edit(() => setOptions(id, change));
}

function rename(id: number, name: string): void {
  if (name !== tree?.items.find((each) => each.id === id)?.name) edit(() => renameItem(id, name));
}

function inEvent(path: string): boolean {
  const first = Number(path.split("/")[0]);
  return view?.events.some((each) => Number(each.path) === first) ?? false;
}

onMount(() => {
  untrack(async () => {
    try {
      catalog = await queued(triggerCatalog);
      await refresh();
      const root = tree?.items.find((each) => each.kind === "root");
      if (root && selected === null) await select(root.id);
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

{#snippet section(title: string, kind: FunctionKind, functions: FunctionView[])}
  <section class="mt-3">
    <div class="group flex items-center gap-1 border-b border-line pb-1">
      <h3 class="m-0 font-heading text-[13.5px] font-bold text-fg">{title}</h3>
      {#if !readOnly}
        <button
          type="button"
          class="rounded p-0.5 text-muted hover:bg-hover hover:text-fg"
          title={kind === "event" ? t("triggers.addEvent") : kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction")}
          aria-label={kind === "event" ? t("triggers.addEvent") : kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction")}
          onclick={() => addToTrigger(kind)}
        >
          <Plus size={14} />
        </button>
      {/if}
    </div>
    <div class="mt-1" role="tree" aria-label={title}>
      {#if catalog}
        {#each functions as function_ (function_.path)}
          <FunctionRow
            {function_}
            {catalog}
            {mode}
            {readOnly}
            selected={selectedFunction}
            depth={0}
            onselect={(each) => (selectedFunction = each.path)}
            onparameter={(parameter) => (parameters = [parameter.path])}
            onchange={(each) => (changing = each)}
            onmenu={functionMenu}
            onadd={(parent, block) => addTo(block.kind, blockPlace(parent, block))}
            onmenublock={blockMenu}
          />
        {:else}
          <p class="m-0 px-1.5 py-1 text-faint">{t("triggers.empty")}</p>
        {/each}
      {/if}
    </div>
  </section>
{/snippet}

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
          class="flex h-[26px] items-center gap-1 pr-2 {selected === row.item.id ? 'bg-accent-lo text-fg' : 'text-body hover:bg-hover'}"
          style:padding-left={`${row.depth * 16 + 4}px`}
          role="treeitem"
          aria-selected={selected === row.item.id}
          aria-expanded={row.children ? isOpen(row.item) : undefined}
          tabindex="-1"
          onclick={() => select(row.item.id)}
          onkeydown={(event) => event.key === "Enter" && select(row.item.id)}
          oncontextmenu={(event) => {
            event.preventDefault();
            itemMenu(row.item, event.clientX, event.clientY);
          }}
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
          <span class="truncate {row.item.enabled ? '' : 'text-faint'}">{row.item.kind === "root" ? t("triggers.mapHeader") : row.item.name}</span>
        </div>
      {/each}
    </div>
  </aside>

  <div class="flex min-h-0 flex-col overflow-auto bg-bg p-4">
    {#if problem}
      <p class="m-0 text-warn">{problem}</p>
    {:else if !tree || !catalog}
      <p class="m-0 text-muted">{t("triggers.loading")}</p>
    {:else}
      {#if tree.problem}
        <p class="m-0 mb-3 text-warn">{tree.problem}</p>
      {/if}
      {#if item?.kind === "root"}
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
      {:else if item?.kind === "category"}
        {@const id = item.id}
        <label class="max-w-[460px]">
          <span class="mb-1 block text-muted">{t("triggers.name")}</span>
          <input class={box} value={item.name} disabled={readOnly} use:typedBox={(name) => rename(id, name)} />
        </label>
      {:else if item?.kind === "variable" && variableInput && variable}
        {@const id = variable.id}
        <VariableForm {catalog} value={variableInput} {readOnly} onsave={(input) => edit(() => setVariable(id, input))} />
      {:else if view}
        {#key view.id}
          {@const id = view.id}
          <div class="flex items-center gap-4">
            <input class="{box} max-w-[460px] font-heading text-[15px] font-bold" aria-label={t("triggers.name")} value={view.name} disabled={readOnly} use:typedBox={(name) => rename(id, name)} />
            {#if view.kind !== "comment"}
              <label class="flex items-center gap-1.5 whitespace-nowrap text-body">
                <input type="checkbox" checked={view.enabled} disabled={readOnly} onchange={(event) => options(id, { enabled: event.currentTarget.checked })} />
                {t("triggers.enabled")}
              </label>
            {/if}
            {#if view.kind === "trigger" || view.kind === "custom"}
              <label class="flex items-center gap-1.5 whitespace-nowrap text-body">
                <input type="checkbox" checked={view.initiallyOn} disabled={readOnly} onchange={(event) => options(id, { initiallyOn: event.currentTarget.checked })} />
                {t("triggers.initiallyOn")}
              </label>
            {/if}
            {#if view.kind === "custom"}
              <label class="flex items-center gap-1.5 whitespace-nowrap text-body">
                <input type="checkbox" checked={view.runOnInit} disabled={readOnly} onchange={(event) => options(id, { runOnInit: event.currentTarget.checked })} />
                {t("triggers.runOnInit")}
              </label>
            {/if}
          </div>
          <label class="mt-3">
            <span class="mb-1 block text-muted">{t("triggers.comment")}</span>
            <textarea class="{box} {view.kind === 'comment' ? 'min-h-[320px]' : 'h-[58px]'} resize-none" value={view.description} disabled={readOnly} use:typedBox={(description) => options(id, { description })}></textarea>
          </label>
          {#if view.kind === "script" || view.kind === "custom"}
            <label class="mt-3 flex min-h-0 flex-1 flex-col">
              <span class="mb-1 block text-muted">{t("triggers.code")}</span>
              <textarea class={code} spellcheck="false" value={view.code ?? ""} disabled={readOnly} use:typedBox={(text) => edit(() => setCode(id, text))}></textarea>
            </label>
          {:else if view.kind === "trigger"}
            {@render section(t("triggers.events"), "event", view.events)}
            {@render section(t("triggers.conditions"), "condition", view.conditions)}
            {@render section(t("triggers.actions"), "action", view.actions)}
          {/if}
        {/key}
      {:else}
        <p class="m-0 text-muted">{t("triggers.nothingSelected")}</p>
      {/if}
    {/if}
  </div>
</div>

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
  <FunctionDialog {catalog} kind={changing.kind} title={t("triggers.function.change")} current={changing.name} onpick={pickedFunction} oncancel={() => (changing = null)} />
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
    onopen={(inner) => (parameters = [...parameters, inner.path])}
    onpicking={(now) => (picking = now)}
    onclose={() => (parameters = parameters.slice(0, -1))}
  />
{/if}
