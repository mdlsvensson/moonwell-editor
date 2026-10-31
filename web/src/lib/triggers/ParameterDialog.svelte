<script lang="ts">
/**
 * A parameter: World Editor's four ways to fill it (preset, variable, function, value) and, for a thing on the map, a
 * pick on the map. Each choice is sent at once, and undo takes it back. A function's own parameters and an array's
 * index are links here too: they open on top of this one, and Close goes back.
 */
import Crosshair from "@lucide/svelte/icons/crosshair";
import { type Hit, search } from "../api.ts";
import Dialog from "../Dialog.svelte";
import { pickOnMap } from "../map/pick.ts";
import { t } from "../text.ts";
import type {
  Catalog,
  MapReference,
  ParameterKind,
  ParameterView,
  TreeItem,
  Variable,
} from "./api.ts";
import {
  callsFor,
  functionTitle,
  objectKindOf,
  pickKindOf,
  presetsOf,
  valueOffered,
  variablesFor,
} from "./lists.ts";

type Props = {
  parameter: ParameterView;
  catalog: Catalog;
  variables: Variable[];
  references: MapReference[];
  items: TreeItem[];
  /** True for a parameter of an event's function, where the game allows fewer functions. */
  inEvent: boolean;
  onset: (path: string, kind: ParameterKind, value: string) => Promise<void>;
  /** Sets a point: the `Location` function with these coordinates. */
  onpoint: (path: string, x: number, y: number) => Promise<void>;
  /** Opens a parameter inside this one. */
  onopen: (parameter: ParameterView) => void;
  /** The map is about to show for a pick, or has gone again: the dialog hides meanwhile. */
  onpicking: (picking: boolean) => void;
  onclose: () => void;
};

let {
  parameter,
  catalog,
  variables,
  references,
  items,
  inEvent,
  onset,
  onpoint,
  onopen,
  onpicking,
  onclose,
}: Props = $props();

const type = $derived(parameter.type);
const typeTitle = $derived(catalog.types.find((each) => each.key === type)?.title ?? type);
const presets = $derived(presetsOf(catalog, type));
const calls = $derived(callsFor(catalog, type, inEvent));
const named = $derived.by(() => {
  const list = variablesFor(variables, type).map((each) => ({ value: each.name, text: each.name }));
  for (const reference of references) {
    if (reference.type === type) list.push({ value: reference.ref, text: reference.text });
  }
  if (type === "trigger") {
    for (const item of items) {
      if (item.kind === "trigger" || item.kind === "custom") {
        list.push({ value: item.ref, text: `${item.name} <gen>` });
      }
    }
  }
  return list;
});
const typedValue = $derived(valueOffered(catalog, type));
const objectKind = $derived(objectKindOf(type));
const pickKind = $derived(pickKindOf(type));

type Way = "preset" | "variable" | "function" | "value";
const ways = $derived.by(() => {
  const list: { way: Way; label: string }[] = [];
  if (presets.length > 0) list.push({ way: "preset", label: t("triggers.parameter.preset") });
  if (named.length > 0 || type === "AnyGlobal") {
    list.push({ way: "variable", label: t("triggers.parameter.variable") });
  }
  if (calls.length > 0) list.push({ way: "function", label: t("triggers.parameter.function") });
  if (typedValue) list.push({ way: "value", label: t("triggers.parameter.value") });
  return list;
});

let chosen = $state<Way | null>(null);
const way = $derived<Way | null>(
  chosen ?? (parameter.kind !== "none" ? parameter.kind : (ways[0]?.way ?? null)),
);

let filter = $state("");
let text = $state("");
let lastPath = "";
let problem = $state("");

// Another parameter opened here starts on its own value.
$effect(() => {
  if (parameter.path !== lastPath) {
    lastPath = parameter.path;
    chosen = null;
    filter = "";
    text = parameter.kind === "value" && !objectKind ? parameter.typed : "";
    hits = [];
  }
});

function has(what: string): boolean {
  return what.toLowerCase().includes(filter.trim().toLowerCase());
}

const shownPresets = $derived(presets.filter((each) => has(each.text)));
const shownNames = $derived(named.filter((each) => has(each.text)));
const shownCalls = $derived(
  calls
    .map((each) => ({ name: each.name, title: functionTitle(catalog, each) }))
    .filter((each) => has(each.title))
    .toSorted((one, other) => one.title.localeCompare(other.title)),
);

async function set(kind: ParameterKind, value: string): Promise<void> {
  problem = "";
  try {
    await onset(parameter.path, kind, value);
  } catch (error) {
    problem = error instanceof Error ? error.message : String(error);
  }
}

// A box left as it opened changes nothing, as its text may be a string from the strings file.
function sendValue(): void {
  if (parameter.kind !== "value" || text !== parameter.typed) set("value", text);
}

async function pick(): Promise<void> {
  if (!pickKind) return;
  onpicking(true);
  const picked = await pickOnMap(pickKind);
  onpicking(false);
  if (!picked) return;
  if (pickKind === "point") await onpoint(parameter.path, picked.x, picked.y);
  else if (picked.ref) await set("variable", picked.ref);
}

// The objects of the parameter's kind that a typed text finds.
let hits = $state.raw<Hit[]>([]);
let asked = 0;
let timer: ReturnType<typeof setTimeout> | undefined;

function findObjects(): void {
  clearTimeout(timer);
  timer = setTimeout(async () => {
    const mine = ++asked;
    const wanted = text.trim();
    const found = wanted === "" ? [] : await search(wanted, 200);
    if (mine === asked) {
      hits = found.filter((hit) => hit.field === null && hit.kind === objectKind).slice(0, 40);
    }
  }, 60);
}
</script>

<Dialog title={parameter.name || typeTitle} note={typeTitle} oncancel={onclose} wide>
  <div class="flex flex-col gap-2">
    <div class="flex items-center gap-1" role="group" aria-label={typeTitle}>
      {#each ways as each (each.way)}
        <button
          type="button"
          class="rounded-[5px] px-2 py-1 {way === each.way
            ? 'bg-accent-lo text-accent-hi'
            : 'text-muted hover:bg-hover hover:text-fg'}"
          aria-pressed={way === each.way}
          onclick={() => (chosen = each.way)}
        >
          {each.label}
        </button>
      {/each}
      {#if pickKind}
        <button
          type="button"
          class="ml-auto inline-flex items-center gap-1 rounded-[5px] px-2 py-1 text-muted hover:bg-hover hover:text-fg"
          onclick={pick}
        >
          <Crosshair size={14} />
          {t("triggers.parameter.pick")}
        </button>
      {/if}
    </div>

    <p class="m-0 min-h-[20px] text-fg">
      {#if parameter.function}
        {#each parameter.function.sentence as part, index (index)}{#if part.parameter}{@const inner = part.parameter}<button
              type="button"
              class="rounded-sm underline decoration-dotted underline-offset-2 hover:bg-accent-lo {inner.kind === 'none'
                ? 'text-warn'
                : 'text-accent-hi'}"
              onclick={() => onopen(inner)}>{part.text}</button
            >{:else}{part.text}{/if}{/each}
      {:else if parameter.index}
        {parameter.value}[<button
          type="button"
          class="rounded-sm text-accent-hi underline decoration-dotted underline-offset-2 hover:bg-accent-lo"
          title={t("triggers.parameter.index")}
          onclick={() => parameter.index && onopen(parameter.index)}>{parameter.index.text}</button
        >]
      {:else if parameter.kind === "none"}
        <span class="text-warn">{t("triggers.parameter.none")}</span>
      {:else}
        {parameter.text}
      {/if}
    </p>

    {#if way === "value"}
      <input
        class="w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
        placeholder={objectKind ? t("triggers.parameter.search") : ""}
        aria-label={t("triggers.parameter.value")}
        bind:value={text}
        oninput={() => objectKind && findObjects()}
        onkeydown={(event) => {
          if (event.key === "Enter" && !objectKind) {
            event.preventDefault();
            sendValue();
          }
        }}
        onchange={() => !objectKind && sendValue()}
      />
      {#if objectKind}
        <div class="h-[260px] overflow-auto rounded-[5px] border border-line">
          {#each hits as hit (hit.id)}
            <button
              type="button"
              class="flex w-full items-baseline gap-2 px-2 py-[3px] text-left text-body hover:bg-hover"
              onclick={() => set("value", hit.id)}
            >
              {hit.name}<span class="font-mono text-[12px] text-faint">{hit.id}</span>
            </button>
          {/each}
        </div>
      {/if}
    {:else if way !== null}
      <input
        class="w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
        placeholder={t("triggers.parameter.filter")}
        aria-label={t("triggers.parameter.filter")}
        bind:value={filter}
      />
      <div class="h-[260px] overflow-auto rounded-[5px] border border-line" role="listbox" aria-label={typeTitle}>
        {#if way === "preset"}
          {#each shownPresets as each (each.key)}
            <button
              type="button"
              role="option"
              aria-selected={parameter.kind === "preset" && parameter.value === each.key}
              class="block w-full px-2 py-[3px] text-left {parameter.kind === 'preset' && parameter.value === each.key
                ? 'bg-accent-lo text-fg'
                : 'text-body hover:bg-hover'}"
              onclick={() => set("preset", each.key)}
            >
              {each.text}
            </button>
          {:else}
            <p class="p-2 text-muted">{t("triggers.parameter.noMatch")}</p>
          {/each}
        {:else if way === "variable"}
          {#each shownNames as each (each.value)}
            <button
              type="button"
              role="option"
              aria-selected={parameter.kind === "variable" && parameter.value === each.value}
              class="block w-full px-2 py-[3px] text-left {parameter.kind === 'variable' && parameter.value === each.value
                ? 'bg-accent-lo text-fg'
                : 'text-body hover:bg-hover'}"
              onclick={() => set("variable", each.value)}
            >
              {each.text}
            </button>
          {:else}
            <p class="p-2 text-muted">{t("triggers.parameter.noMatch")}</p>
          {/each}
        {:else}
          {#each shownCalls as each (each.name)}
            <button
              type="button"
              role="option"
              aria-selected={parameter.kind === "function" && parameter.value === each.name}
              class="block w-full px-2 py-[3px] text-left {parameter.kind === 'function' && parameter.value === each.name
                ? 'bg-accent-lo text-fg'
                : 'text-body hover:bg-hover'}"
              onclick={() => set("function", each.name)}
            >
              {each.title}
            </button>
          {:else}
            <p class="p-2 text-muted">{t("triggers.parameter.noMatch")}</p>
          {/each}
        {/if}
      </div>
    {/if}
    {#if problem}<p class="m-0 text-warn">{problem}</p>{/if}
  </div>
  {#snippet actions()}
    <button
      type="button"
      data-primary
      class="rounded-md border border-line bg-raised px-3 py-1.5 text-fg hover:bg-hover"
      onclick={onclose}
    >
      {t("triggers.close")}
    </button>
  {/snippet}
</Dialog>
