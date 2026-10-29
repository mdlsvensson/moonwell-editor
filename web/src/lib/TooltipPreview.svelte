<script lang="ts">
/**
 * The object's tooltip as the game shows it (Sven, 2026-10-27): the icon and the basic tooltip with its hotkey in
 * gold, a line of costs, then the extended tooltip in the game's colours. An object with levels gets a level stepper;
 * a placeholder the preview cannot fill (an ability's Data) shows dimmed, as written. A hero ability's learn tooltip
 * comes after, in a box of its own.
 */
import ChevronLeft from "@lucide/svelte/icons/chevron-left";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import { iconUrl, type ObjectView } from "./api.ts";
import { uiIcons } from "./gameIcons.ts";
import { graphics } from "./graphics.svelte.ts";
import { t } from "./text.ts";
import { type Cost, tooltipLines, tooltipOf } from "./tooltip.ts";

type Props = {
  view: ObjectView;
  /** The level shown, from 1, for an object with levels. */
  level?: number;
};

let { view, level = $bindable(1) }: Props = $props();

const shown = $derived(Math.min(Math.max(1, level), Math.max(1, view.levels)));
const tip = $derived(tooltipOf(view, shown));
const empty = $derived(!tip || (tip.title === "" && tip.text === ""));

// The icon that failed to load, so a missing one shows the empty square instead.
let broken = $state("");
</script>

{#snippet lines(text: string, extra: string)}
  <div class="{extra} break-words">
    {#each tooltipLines(text) as line, i (i)}
      <div class="min-h-[1.35em]">
        {#each line as run, j (j)}<span
            class="whitespace-pre-wrap {run.placeholder ? 'opacity-45' : ''}"
            style:color={run.color ?? "#ffffff"}>{run.text}</span
          >{/each}
      </div>
    {/each}
  </div>
{/snippet}

{#snippet costLine(costs: Cost[])}
  {#if costs.length > 0}
    <div class="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[12px]">
      {#each costs as cost (cost.label)}
        <!-- With the game's icon for gold, lumber, food and mana, as its tooltips show them (Sven, 2026-10-27). -->
        <span class="inline-flex items-center gap-1" title={cost.label}>
          {#if cost.icon}
            <img src={iconUrl(uiIcons[cost.icon], graphics.mode)} alt={cost.label} class="size-4" draggable="false" />
          {:else}
            <span class="text-[#a0a0a0]">{cost.label}</span>
          {/if}
          <span class="text-white">{cost.value}</span>
        </span>
      {/each}
    </div>
  {/if}
{/snippet}

{#if view.levels > 1}
  <div class="mb-2 flex items-center gap-1 text-[12.5px] text-muted">
    <button
      type="button"
      class="rounded p-0.5 text-faint hover:bg-hover hover:text-fg disabled:opacity-40"
      aria-label={t("tooltip.levelBefore")}
      disabled={shown <= 1}
      onclick={() => (level = shown - 1)}
    >
      <ChevronLeft size={14} />
    </button>
    <span class="min-w-16 text-center">{t("tooltip.level", { level: shown, levels: view.levels })}</span>
    <button
      type="button"
      class="rounded p-0.5 text-faint hover:bg-hover hover:text-fg disabled:opacity-40"
      aria-label={t("tooltip.levelAfter")}
      disabled={shown >= view.levels}
      onclick={() => (level = shown + 1)}
    >
      <ChevronRight size={14} />
    </button>
  </div>
{/if}

<div class="rounded-md border border-line bg-black/85 px-2.5 py-2 font-tooltip text-[13px] leading-snug text-white shadow-lg">
  {#if empty || !tip}
    <p class="m-0 text-[12.5px] text-white/40">{t("tooltip.none")}</p>
  {:else}
    <div class="flex items-start gap-2">
      {#if tip.icon && broken !== tip.icon}
        <img
          src={iconUrl(tip.icon, graphics.mode)}
          alt=""
          class="size-8 shrink-0 rounded-sm border border-white/20"
          onerror={() => (broken = tip.icon)}
        />
      {:else}
        <div class="size-8 shrink-0 rounded-sm border border-white/20 bg-white/5"></div>
      {/if}
      <div class="min-w-0 flex-1">
        {@render lines(tip.title, "font-semibold")}
        {@render costLine(tip.costs)}
      </div>
    </div>
    {#if tip.text}
      {@render lines(tip.text, "mt-2")}
    {/if}
  {/if}
</div>

{#if tip?.learn}
  <div class="mt-2 rounded-md border border-line bg-black/85 px-2.5 py-2 text-[13px] leading-snug text-white shadow-lg">
    {@render lines(tip.learn.title, "font-semibold")}
    {#if tip.learn.text}
      {@render lines(tip.learn.text, "mt-2")}
    {/if}
  </div>
{/if}
