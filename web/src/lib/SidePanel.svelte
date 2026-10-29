<script lang="ts">
/**
 * The right panel (Sven, 2026-10-27): the object's model turning (with a team colour to pick), its tooltip as the
 * game shows it, Uses, every object this object's fields name, and Used by, every object whose fields name this one.
 * A click opens the object; the fields that name it show as its tooltip. Each section opens and closes, and stays so
 * from object to object.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import PencilLine from "@lucide/svelte/icons/pencil-line";
import { fileUrl, type ObjectView, type Reference, usedBy } from "./api.ts";
import { type Lookup, uses } from "./fields.ts";
import { graphics } from "./graphics.svelte.ts";
import ModelPreview from "./ModelPreview.svelte";
import { prefs } from "./prefs.svelte.ts";
import TooltipPreview from "./TooltipPreview.svelte";
import { t } from "./text.ts";
import { kindNames, kindText } from "./tree.ts";

type Props = {
  view: ObjectView;
  lookup: Lookup;
  onopen: (kind: Reference["kind"], id: string) => void;
  /** Opens the tooltip editor; absent when the object's tooltip cannot be edited. */
  onedittooltip?: () => void;
};

let { view, lookup, onopen, onedittooltip }: Props = $props();

const used = $derived(uses(view, lookup));

// The field whose model the panel shows: the object's own model (a unit's, item's, destructable's or doodad's file),
// else a buff's or an ability's first art, else the first model field with a value.
const modelFields = [
  "umdl",
  "ifil",
  "bfil",
  "dfil",
  "ftat",
  "fsat",
  "atat",
  "acat",
  "asat",
  "aeat",
  "amat",
];
const model = $derived.by(() => {
  const named = view.fields.filter((field) => field.asset === "model" && field.value.trim() !== "");
  const first = (value: string) => value.split(",")[0]?.trim() ?? "";
  for (const id of modelFields) {
    const field = named.find((each) => each.id === id);
    if (field) return first(field.value);
  }
  return named[0] ? first(named[0].value) : "";
});

// The team colour the model is shown in, kept from object to object; it starts as the settings' default and follows it
// when that changes (Sven, 2026-10-29).
let teamColor = $state(prefs.teamColor);
$effect(() => {
  teamColor = prefs.teamColor;
});
let width = $state(270);
let level = $state(1);

// The sections the user closed; Uses and Used by start closed (Sven, 2026-10-29).
let closed = $state<Record<string, boolean>>({ uses: true, usedBy: true });

// Used by is asked for again with each new view of the object (another object, or the same one after an edit).
let users = $state.raw<Reference[] | null>(null);
let problem = $state("");
$effect(() => {
  const asked = view;
  users = null;
  problem = "";
  usedBy(asked.kind, asked.id).then(
    (found) => {
      if (asked === view) users = found;
    },
    (error) => {
      if (asked === view) problem = error instanceof Error ? error.message : String(error);
    },
  );
});
</script>

{#snippet list(items: { kind: Reference["kind"]; id: string; name: string; fields: string[] }[])}
  {#each items as item (`${item.kind}:${item.id}`)}
    <button
      type="button"
      class="mb-1 flex w-full items-center gap-2 rounded-md border border-line bg-raised px-2 py-1 text-left hover:border-accent"
      title="{kindNames[item.kind]} · {item.fields.join(', ')}"
      onclick={() => onopen(item.kind, item.id)}
    >
      <span class="truncate {item.name ? kindText[item.kind] : 'text-muted'}">{item.name || t("side.unknown")}</span>
      <span class="ml-auto shrink-0 font-mono text-[12.5px] text-faint">{item.id}</span>
    </button>
  {/each}
{/snippet}

{#snippet heading(key: string, title: string, count: number | null, top: string)}
  <h3 class="{top} mb-2">
    <button
      type="button"
      class="flex w-full items-center gap-1.5 text-left text-[13px] tracking-[.06em] text-muted uppercase hover:text-fg"
      aria-expanded={!closed[key]}
      onclick={() => (closed[key] = !closed[key])}
    >
      <span class="flex w-3 shrink-0 items-center text-faint">
        {#if closed[key]}<ChevronRight size={12} />{:else}<ChevronDown size={12} />{/if}
      </span>
      {title}
      {#if count !== null}
        <!-- At the right end, as the tree's counts are (Sven, 2026-10-29). -->
        <span class="ml-auto font-mono text-[11px] tracking-normal text-faint">{count}</span>
      {/if}
    </button>
  </h3>
{/snippet}

<div bind:clientWidth={width}>
  {@render heading("model", t("side.model"), null, "mt-1")}
  {#if !closed.model}
    {#if model}
      <ModelPreview
        path={model}
        fileUrl={(path) => fileUrl(path, graphics.mode)}
        size={Math.max(160, width)}
        bind:teamColor
        picker
        animations
      />
    {:else}
      <p class="text-[12.5px] text-faint">{t("side.noModel")}</p>
    {/if}
  {/if}
</div>

<div class="relative">
  {@render heading("tooltip", t("side.tooltip"), null, "mt-5")}
  {#if onedittooltip}
    <!-- As tall as the heading's text (Sven, 2026-10-29). -->
    <button
      type="button"
      class="absolute top-0 right-0 flex items-center gap-1 rounded px-1 text-[13px] leading-[inherit] text-muted hover:bg-hover hover:text-fg"
      title={t("side.editTooltip")}
      onclick={onedittooltip}
    >
      <PencilLine size={13} />
      {t("side.edit")}
    </button>
  {/if}
</div>
{#if !closed.tooltip}
  <TooltipPreview {view} bind:level />
{/if}

{@render heading("uses", t("side.uses"), used.length, "mt-5")}
{#if closed.uses}
  <!-- Closed. -->
{:else if used.length === 0}
  <p class="text-[12.5px] text-faint">{t("side.usesNone")}</p>
{:else}
  {@render list(used)}
{/if}

{@render heading("usedBy", t("side.usedBy"), users?.length ?? null, "mt-5")}
{#if closed.usedBy}
  <!-- Closed. -->
{:else if problem}
  <p class="text-[12.5px] text-warn">{problem}</p>
{:else if users === null}
  <p class="text-[12.5px] text-faint">{t("side.looking")}</p>
{:else if users.length === 0}
  <p class="text-[12.5px] text-faint">{t("side.usedByNone")}</p>
{:else}
  {@render list(users)}
{/if}

