<script lang="ts">
/**
 * The picker of an icon or a model field (Sven, 2026-10-27): a button beside the box opens a search of the game's
 * icons (as a grid of them) or models (as a list, with the one under the pointer turning in a preview). A click puts
 * the path into the field as World Editor writes it; any other path (an import) is typed in the box.
 */
import Box from "@lucide/svelte/icons/box";
import ImageIcon from "@lucide/svelte/icons/image";
import { type AssetList, fileUrl, iconUrl } from "./api.ts";
import { listOf, matching } from "./assets.ts";
import { graphics } from "./graphics.svelte.ts";
import ModelPreview from "./ModelPreview.svelte";
import Popover from "./Popover.svelte";

type Props = {
  kind: "icon" | "model";
  /** The field's value now. */
  value: string;
  label: string;
  /** Sends a value; answers with why it was refused, or null. */
  onpick: (path: string) => Promise<string | null>;
};

let { kind, value, label, onpick }: Props = $props();

let open = $state(false);
let button: HTMLButtonElement | undefined = $state();
let query = $state("");
let list = $state<AssetList | null>(null);
let problem = $state("");
let hovered = $state<string | null>(null);

const shown = $derived(list ? matching(list.paths, query, kind === "icon" ? 240 : 300) : []);
// The model the preview shows: the one under the pointer, else the field's.
const previewed = $derived(hovered ?? (value.trim() || null));

$effect(() => {
  if (!open) return;
  problem = "";
  listOf(kind, graphics.mode).then(
    (found) => (list = found),
    (error) => (problem = error instanceof Error ? error.message : String(error)),
  );
});

async function pick(path: string): Promise<void> {
  const refused = await onpick(path);
  if (refused) {
    problem = refused;
    return;
  }
  open = false;
}

function focusOnMount(element: HTMLInputElement): void {
  element.focus();
}
</script>

<button
  type="button"
  class="shrink-0 rounded p-1 text-faint hover:bg-hover hover:text-fg {open ? 'text-accent-hi' : ''}"
  title={kind === "icon" ? "Pick an icon" : "Pick a model"}
  aria-label="Pick the {label}"
  aria-haspopup="dialog"
  aria-expanded={open}
  bind:this={button}
  onclick={() => (open = !open)}
>
  {#if kind === "icon"}<ImageIcon size={14} />{:else}<Box size={14} />{/if}
</button>
{#if open && button}
  <Popover anchor={button} onclose={() => (open = false)} large>
    <div class="flex flex-col gap-2 {kind === 'icon' ? 'w-[430px]' : 'w-[640px]'}">
      <input
        class="w-full rounded-[5px] border border-line bg-bg px-2 py-1 text-fg outline-none focus:border-accent"
        placeholder={kind === "icon" ? "Search icons by name or path…" : "Search models by name or path…"}
        aria-label="Search"
        bind:value={query}
        use:focusOnMount
      />
      {#if problem}
        <p class="m-0 text-[12.5px] text-warn">{problem}</p>
      {:else if !list}
        <p class="m-0 text-[12.5px] text-muted">Reading the game's {kind === "icon" ? "icons" : "models"}…</p>
      {:else if !list.ready}
        <p class="m-0 text-[12.5px] text-warn">
          {list.error ?? "The game's files are still being opened; try again in a moment."}
        </p>
      {:else if shown.length === 0}
        <p class="m-0 text-[12.5px] text-muted">Nothing matches “{query}”.</p>
      {/if}

      {#if kind === "icon"}
        <div class="grid max-h-[300px] grid-cols-9 gap-1 overflow-auto pr-1">
          {#each shown as path (path)}
            <button
              type="button"
              class="rounded border p-0.5 hover:border-accent {path.toLowerCase() === value.trim().toLowerCase()
                ? 'border-accent bg-accent-lo'
                : 'border-transparent'}"
              title={path}
              onclick={() => pick(path)}
            >
              <img src={iconUrl(path, graphics.mode)} alt={path} class="block size-10" loading="lazy" draggable="false" />
            </button>
          {/each}
        </div>
      {:else}
        <div class="flex gap-2">
          <div class="flex max-h-[300px] min-w-0 flex-1 flex-col overflow-auto pr-1" role="listbox" aria-label="Models">
            {#each shown as path (path)}
              <button
                type="button"
                role="option"
                aria-selected={path.toLowerCase() === value.trim().toLowerCase()}
                class="shrink-0 truncate rounded px-1.5 py-0.5 text-left font-mono text-[12px] hover:bg-hover {path.toLowerCase() ===
                value.trim().toLowerCase()
                  ? 'text-accent-hi'
                  : 'text-body'}"
                title={path}
                onmouseenter={() => (hovered = path)}
                onmouseleave={() => (hovered = null)}
                onclick={() => pick(path)}
              >
                {path}
              </button>
            {/each}
          </div>
          <div class="shrink-0">
            {#if previewed}
              <ModelPreview path={previewed} fileUrl={(path) => fileUrl(path, graphics.mode)} size={240} />
              <p class="m-0 mt-1 w-[240px] truncate font-mono text-[11px] text-faint" title={previewed}>
                {previewed}
              </p>
            {:else}
              <div class="flex size-[240px] items-center justify-center rounded border border-dashed border-line text-[12px] text-faint">
                No model
              </div>
            {/if}
          </div>
        </div>
      {/if}
    </div>
  </Popover>
{/if}
