<script module lang="ts">
/** One entry of a context menu; a `separator` draws a line instead. */
export type MenuItem =
  | { label: string; shortcut?: string; disabled?: boolean; danger?: boolean; run: () => void }
  | { separator: true };
</script>

<script lang="ts">
/**
 * The menu a right click opens (Sven, 2026-10-27), at the pointer and kept inside the window. The arrow keys move
 * between entries, Enter runs one; a click outside it, Esc, scrolling or a blur closes it.
 */
type Props = {
  x: number;
  y: number;
  items: MenuItem[];
  onclose: () => void;
};

let { x, y, items, onclose }: Props = $props();

let menu: HTMLDivElement | undefined = $state();
let top = $state(0);
let left = $state(0);

$effect(() => {
  const height = menu?.offsetHeight ?? 0;
  const width = menu?.offsetWidth ?? 0;
  top = Math.max(4, Math.min(y, window.innerHeight - height - 4));
  left = Math.max(4, Math.min(x, window.innerWidth - width - 4));
  menu?.querySelector<HTMLButtonElement>("button:not(:disabled)")?.focus();

  const outside = (event: Event) => {
    if (!(event.target instanceof Node) || !menu?.contains(event.target)) onclose();
  };
  window.addEventListener("pointerdown", outside, true);
  window.addEventListener("scroll", outside, true);
  window.addEventListener("blur", onclose);
  return () => {
    window.removeEventListener("pointerdown", outside, true);
    window.removeEventListener("scroll", outside, true);
    window.removeEventListener("blur", onclose);
  };
});

function run(item: MenuItem): void {
  if ("separator" in item || item.disabled) return;
  onclose();
  item.run();
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "Escape") {
    event.preventDefault();
    event.stopPropagation();
    onclose();
  } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    const buttons = [...(menu?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? [])];
    const at = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next = (at + (event.key === "ArrowDown" ? 1 : buttons.length - 1)) % buttons.length;
    buttons[next]?.focus();
  }
}
</script>

<div
  class="fixed z-30 min-w-[210px] rounded-lg border border-line bg-surface p-1 text-[13px] text-body shadow-[0_10px_40px_rgba(0,0,0,.5)]"
  style:top="{top}px"
  style:left="{left}px"
  role="menu"
  tabindex="-1"
  bind:this={menu}
  {onkeydown}
  oncontextmenu={(event) => event.preventDefault()}
>
  {#each items as item, index (index)}
    {#if "separator" in item}
      <div class="my-1 h-px bg-line" role="separator"></div>
    {:else}
      <button
        type="button"
        role="menuitem"
        class="flex w-full items-center gap-6 rounded px-2.5 py-1 text-left focus:outline-none disabled:opacity-40 {item.danger
          ? 'text-warn'
          : ''} enabled:hover:bg-hover enabled:focus:bg-hover"
        disabled={item.disabled}
        onclick={() => run(item)}
      >
        {item.label}
        {#if item.shortcut}
          <span class="ml-auto font-mono text-[11.5px] text-faint">{item.shortcut}</span>
        {/if}
      </button>
    {/if}
  {/each}
</div>
