<script lang="ts">
/**
 * The window's title bar (Sven, 2026-10-27): thin, as Windows draws a maximized window's, whether the window is
 * maximized or not. The bar is a drag region, which the window treats as its caption (dragging, snapping, a double
 * click to maximize, the system menu on a right click). Back and forward at the left walk the visited objects, as a
 * browser's do (Alt ← and Alt →); the buttons at the right minimize, maximize or restore, and close.
 */
import ArrowLeft from "@lucide/svelte/icons/arrow-left";
import ArrowRight from "@lucide/svelte/icons/arrow-right";
import Copy from "@lucide/svelte/icons/copy";
import Minus from "@lucide/svelte/icons/minus";
import Square from "@lucide/svelte/icons/square";
import X from "@lucide/svelte/icons/x";
import { onMount } from "svelte";
import { closeClicked, maximizeWindow, minimizeWindow, onWindowState, windowState } from "./api.ts";

type Props = {
  title: string;
  canBack: boolean;
  canForward: boolean;
  onback: () => void;
  onforward: () => void;
};

let { title, canBack, canForward, onback, onforward }: Props = $props();

let maximized = $state(false);

onMount(() => {
  windowState().then(
    (state) => (maximized = state.maximized),
    () => {},
  );
  return onWindowState((state) => (maximized = state.maximized));
});

// Like Windows' own caption buttons, they take no keyboard focus.
const buttonClass =
  "flex w-[46px] items-center justify-center text-muted outline-none hover:bg-hover hover:text-fg [app-region:no-drag]";
</script>

<div class="flex min-w-0 items-stretch bg-surface select-none [app-region:drag]">
  <button
    type="button"
    class="flex w-8 items-center justify-center text-muted outline-none hover:bg-hover hover:text-fg disabled:text-faint disabled:opacity-50 disabled:hover:bg-transparent [app-region:no-drag]"
    tabindex="-1"
    title="Back (Alt ←)"
    aria-label="Back"
    disabled={!canBack}
    onclick={onback}
  >
    <ArrowLeft size={14} strokeWidth={1.75} />
  </button>
  <button
    type="button"
    class="flex w-8 items-center justify-center text-muted outline-none hover:bg-hover hover:text-fg disabled:text-faint disabled:opacity-50 disabled:hover:bg-transparent [app-region:no-drag]"
    tabindex="-1"
    title="Forward (Alt →)"
    aria-label="Forward"
    disabled={!canForward}
    onclick={onforward}
  >
    <ArrowRight size={14} strokeWidth={1.75} />
  </button>
  <span class="min-w-0 flex-1 truncate px-2 text-xs leading-[23px] text-muted">{title}</span>
  <button type="button" class={buttonClass} tabindex="-1" title="Minimize" aria-label="Minimize" onclick={minimizeWindow}>
    <Minus size={14} strokeWidth={1.5} />
  </button>
  <button
    type="button"
    class={buttonClass}
    tabindex="-1"
    title={maximized ? "Restore" : "Maximize"}
    aria-label={maximized ? "Restore" : "Maximize"}
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
    class="flex w-[46px] items-center justify-center text-muted outline-none hover:bg-[#c42b1c] hover:text-white [app-region:no-drag]"
    tabindex="-1"
    title="Close"
    aria-label="Close"
    onclick={closeClicked}
  >
    <X size={15} strokeWidth={1.5} />
  </button>
</div>
