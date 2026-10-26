<script lang="ts">
/**
 * The "Open map…" button: it opens a small menu of the two ways a map is stored, a folder or an archive (see
 * `openMenu.ts`). Picking one closes the menu and hands the entry on.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import FolderOpen from "@lucide/svelte/icons/folder-open";
import { type OpenEntry, openEntries } from "./openMenu.ts";
import Popover from "./Popover.svelte";

type Props = {
  /** The button's text. */
  label: string;
  disabled: boolean;
  /** The button's classes (the header's and the empty page's differ). */
  buttonClass: string;
  onpick: (entry: OpenEntry) => void;
};

let { label, disabled, buttonClass, onpick }: Props = $props();

let open = $state(false);
let button: HTMLButtonElement | undefined = $state();
let menu: HTMLDivElement | undefined = $state();

// The first entry takes the focus, so the menu works from the keyboard (Esc closes it).
$effect(() => {
  if (open) menu?.querySelector("button")?.focus();
});

function pick(entry: OpenEntry): void {
  open = false;
  onpick(entry);
}
</script>

<button
  type="button"
  class={buttonClass}
  {disabled}
  aria-haspopup="menu"
  aria-expanded={open}
  bind:this={button}
  onclick={() => (open = !open)}
>
  <FolderOpen size={16} />
  {label}
  <ChevronDown size={14} class="text-muted" />
</button>
{#if open && button}
  <Popover anchor={button} onclose={() => (open = false)}>
    <div class="flex flex-col" role="menu" aria-label="Open map" bind:this={menu}>
      {#each openEntries as entry (entry.label)}
        <button
          type="button"
          role="menuitem"
          class="rounded px-2 py-1 text-left hover:bg-hover focus:bg-hover focus:outline-none"
          onclick={() => pick(entry)}
        >
          {entry.label}
        </button>
      {/each}
    </div>
  </Popover>
{/if}
