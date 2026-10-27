<script lang="ts">
/**
 * The window's title bar (Sven, 2026-10-27): thin, as Windows draws a maximized window's, whether the window is
 * maximized or not. The bar is a drag region, which the window treats as its caption (dragging, snapping, a double
 * click to maximize, the system menu on a right click). At the left the logo, then back and forward, which walk the
 * visited objects as a browser's do (Alt ← and Alt →; held down or right-clicked, they list the history); then the
 * map: its name, Folder or Archive, Expansion (Frozen Throne) or RoC, and its path. At the right minimize, maximize or
 * restore, and close, all with the same hover.
 */
import ArrowLeft from "@lucide/svelte/icons/arrow-left";
import ArrowRight from "@lucide/svelte/icons/arrow-right";
import Copy from "@lucide/svelte/icons/copy";
import Minus from "@lucide/svelte/icons/minus";
import Square from "@lucide/svelte/icons/square";
import X from "@lucide/svelte/icons/x";
import { onMount } from "svelte";
import {
  closeClicked,
  type MapInfo,
  maximizeWindow,
  minimizeWindow,
  onWindowState,
  windowState,
} from "./api.ts";
import logo from "./logo.svg";
import Popover from "./Popover.svelte";
import { t } from "./text.ts";

/** One visited object, for the history list: its place in the history and what to call it. */
export type Visit = { at: number; name: string; id: string };

type Props = {
  map: MapInfo | null;
  /** The visits before the current one, newest first. */
  back: Visit[];
  /** The visits after the current one, nearest first. */
  forward: Visit[];
  /** Goes to a place in the history. */
  onjump: (at: number) => void;
};

let { map, back, forward, onjump }: Props = $props();

let maximized = $state(false);
// The history list open under the back or the forward button.
let listing = $state<"back" | "forward" | null>(null);
let backButton: HTMLButtonElement | undefined = $state();
let forwardButton: HTMLButtonElement | undefined = $state();
let holdTimer: ReturnType<typeof setTimeout> | undefined;
let held = false;

onMount(() => {
  windowState().then(
    (state) => (maximized = state.maximized),
    () => {},
  );
  return onWindowState((state) => (maximized = state.maximized));
});

// The map's name without the game's colour codes (|cffffcc00 ... |r).
const title = $derived(
  (map?.title ?? "")
    .replace(/\|c[0-9a-f]{8}/gi, "")
    .replace(/\|r/gi, "")
    .trim() ||
    (map?.name ?? ""),
);

function press(which: "back" | "forward"): void {
  held = false;
  clearTimeout(holdTimer);
  holdTimer = setTimeout(() => {
    held = true;
    listing = which;
  }, 450);
}

function release(which: "back" | "forward"): void {
  clearTimeout(holdTimer);
  if (held) return;
  const list = which === "back" ? back : forward;
  if (list[0]) onjump(list[0].at);
}

function jump(at: number): void {
  listing = null;
  onjump(at);
}

// Like Windows' own caption buttons, they take no keyboard focus; every one has the same hover.
const buttonClass =
  "flex items-center justify-center text-muted outline-none hover:bg-hover hover:text-fg disabled:text-faint disabled:opacity-50 disabled:hover:bg-transparent [app-region:no-drag]";
</script>

{#snippet arrow(which: "back" | "forward")}
  {@const list = which === "back" ? back : forward}
  <button
    type="button"
    class="{buttonClass} w-8"
    tabindex="-1"
    title={which === "back" ? t("titleBar.backTitle") : t("titleBar.forwardTitle")}
    aria-label={which === "back" ? t("titleBar.back") : t("titleBar.forward")}
    disabled={list.length === 0}
    onpointerdown={(event) => event.button === 0 && press(which)}
    onpointerup={(event) => event.button === 0 && release(which)}
    onpointerleave={() => clearTimeout(holdTimer)}
    oncontextmenu={(event) => {
      event.preventDefault();
      listing = which;
    }}
    bind:this={
      () => (which === "back" ? backButton : forwardButton),
      (element) => (which === "back" ? (backButton = element) : (forwardButton = element))
    }
  >
    {#if which === "back"}<ArrowLeft size={14} strokeWidth={1.75} />{:else}<ArrowRight
        size={14}
        strokeWidth={1.75}
      />{/if}
  </button>
{/snippet}

<div class="relative flex min-w-0 items-stretch bg-surface select-none [app-region:drag]">
  <img src={logo} alt="" class="mx-2 size-4 self-center" draggable="false" />
  {@render arrow("back")}
  {@render arrow("forward")}
  <!-- Every piece on one centre line (Sven, 2026-10-27): one line height, tags as boxes of a fixed height. -->
  <span class="flex min-w-0 flex-1 items-center gap-1.5 overflow-hidden px-2 text-xs leading-none">
    {#if map}
      <b class="shrink-0 font-semibold text-fg">{title}</b>
      <span class="flex h-4 shrink-0 items-center rounded-[3px] border border-line px-1 pt-px text-[10.5px] text-muted">
        {map.archive ? t("titleBar.archive") : t("titleBar.folder")}
      </span>
      {#if map.expansion !== null && map.expansion !== undefined}
        <span
          class="flex h-4 shrink-0 items-center rounded-[3px] border border-line px-1 pt-px text-[10.5px] text-muted"
          title={map.expansion ? t("titleBar.expansionTitle") : t("titleBar.rocTitle")}
        >
          {map.expansion ? t("titleBar.expansion") : t("titleBar.roc")}
        </span>
      {/if}
      <span class="min-w-0 truncate text-faint">{map.path}</span>
    {:else}
      <span class="text-muted">{t("app.name")}</span>
    {/if}
  </span>
  <button
    type="button"
    class="{buttonClass} w-[46px]"
    tabindex="-1"
    title={t("titleBar.minimize")}
    aria-label={t("titleBar.minimize")}
    onclick={minimizeWindow}
  >
    <Minus size={14} strokeWidth={1.5} />
  </button>
  <button
    type="button"
    class="{buttonClass} w-[46px]"
    tabindex="-1"
    title={maximized ? t("titleBar.restore") : t("titleBar.maximize")}
    aria-label={maximized ? t("titleBar.restore") : t("titleBar.maximize")}
    onclick={maximizeWindow}
  >
    {#if maximized}
      <Copy size={11} strokeWidth={1.5} class="-scale-x-100" />
    {:else}
      <Square size={11} strokeWidth={1.5} />
    {/if}
  </button>
  <button
    type="button"
    class="{buttonClass} w-[46px]"
    tabindex="-1"
    title={t("titleBar.close")}
    aria-label={t("titleBar.close")}
    onclick={closeClicked}
  >
    <X size={15} strokeWidth={1.5} />
  </button>
  <!-- The window's top edge, which resizes it while it is not maximized, over the buttons too: last, so its drag
       region is not cut out by the buttons'. -->
  {#if !maximized}
    <div class="absolute inset-x-0 top-0 z-10 h-1 [app-region:drag]" aria-hidden="true"></div>
  {/if}
</div>

{#if listing}
  {@const anchor = listing === "back" ? backButton : forwardButton}
  {@const list = listing === "back" ? back : forward}
  {#if anchor}
    <Popover {anchor} onclose={() => (listing = null)}>
      <div class="flex flex-col" role="menu" aria-label={t("titleBar.history")}>
        {#each list as visit (visit.at)}
          <button
            type="button"
            role="menuitem"
            class="flex items-center gap-3 rounded px-2 py-1 text-left hover:bg-hover"
            onclick={() => jump(visit.at)}
          >
            <span class="min-w-0 flex-1 truncate text-fg">{visit.name}</span>
            <span class="font-mono text-[12px] text-faint">{visit.id}</span>
          </button>
        {/each}
      </div>
    </Popover>
  {/if}
{/if}
