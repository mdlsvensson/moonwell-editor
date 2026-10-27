<script lang="ts">
/**
 * A button position as the game's command card (Sven, 2026-10-27): a button next to the X or Y box opens the card,
 * 4 buttons wide and 3 high, with the object's place on it; a click sets X and Y together, as one edit. Any other value
 * (one off the card, -11 to hide the button) is typed in the boxes.
 */
import LayoutGrid from "@lucide/svelte/icons/layout-grid";
import type { ButtonPair } from "./fields.ts";
import Popover from "./Popover.svelte";
import { t } from "./text.ts";

type Props = {
  pair: ButtonPair;
  /** Sets X and Y; answers with why it was refused, or null. */
  onpick: (x: number, y: number) => Promise<string | null>;
};

let { pair, onpick }: Props = $props();

let open = $state(false);
let error = $state("");
let button: HTMLButtonElement | undefined = $state();

const columns = [0, 1, 2, 3];
const rows = [0, 1, 2];
const at = $derived({ x: Number(pair.x.value), y: Number(pair.y.value) });

async function pick(x: number, y: number): Promise<void> {
  error = (await onpick(x, y)) ?? "";
  if (!error) open = false;
}
</script>

<button
  type="button"
  class="shrink-0 rounded p-1 text-faint hover:bg-hover hover:text-fg {open ? 'text-accent-hi' : ''}"
  title={t("commandCard.pickTitle")}
  aria-label={t("commandCard.pickLabel", { field: pair.x.label.replace(/\s*\(X\)$/, "") })}
  aria-haspopup="dialog"
  aria-expanded={open}
  bind:this={button}
  onclick={() => (open = !open)}
>
  <LayoutGrid size={14} />
</button>
{#if open && button}
  <Popover anchor={button} onclose={() => (open = false)}>
    <div class="p-1">
      <p class="m-0 mb-2 text-xs text-muted">{t("commandCard.heading")}</p>
      <div class="grid grid-cols-4 gap-1" role="grid" aria-label={t("commandCard.label")}>
        {#each rows as y (y)}
          {#each columns as x (x)}
            {@const here = at.x === x && at.y === y}
            <button
              type="button"
              class="flex size-10 items-center justify-center rounded border font-mono text-[11px] {here
                ? 'border-accent bg-accent-lo text-accent-hi'
                : 'border-line bg-bg text-faint hover:border-muted hover:text-fg'}"
              title={t("commandCard.cell", { x, y })}
              aria-pressed={here}
              onclick={() => pick(x, y)}
            >
              {x},{y}
            </button>
          {/each}
        {/each}
      </div>
      {#if error}
        <p class="m-0 mt-1 max-w-44 text-[11.5px] text-warn" role="alert">{error}</p>
      {/if}
    </div>
  </Popover>
{/if}
