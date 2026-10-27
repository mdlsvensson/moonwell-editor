<script lang="ts">
/**
 * A small panel under the element that opened it (the flags' checkboxes, the object picker). It is placed on the
 * page, not in its scrolling box, so a level grid's sideways scroll cannot cut it off. A click outside it, Esc, or
 * scrolling the page under it closes it.
 */
import type { Snippet } from "svelte";

type Props = {
  anchor: HTMLElement;
  onclose: () => void;
  children: Snippet;
  /** A large panel (the icon and model pickers): as big as its content, up to the window. */
  large?: boolean;
};

let { anchor, onclose, children, large = false }: Props = $props();

let panel: HTMLDivElement | undefined = $state();
let top = $state(0);
let left = $state(0);

// Placed under the anchor, or above it when there is no room below, and kept inside the window; placed again
// whenever its size changes (a picker's results arrive after it opens).
function place(): void {
  const rect = anchor.getBoundingClientRect();
  const height = panel?.offsetHeight ?? 0;
  const width = panel?.offsetWidth ?? 0;
  const below = rect.bottom + 4;
  top = below + height > window.innerHeight - 8 ? Math.max(8, rect.top - 4 - height) : below;
  left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8));
}

$effect(() => {
  place();
  const resized = new ResizeObserver(place);
  if (panel) resized.observe(panel);

  const outside = (target: EventTarget | null) =>
    !(target instanceof Node) || (!panel?.contains(target) && !anchor.contains(target));
  const onpointerdown = (event: PointerEvent) => {
    if (outside(event.target)) onclose();
  };
  const onscroll = (event: Event) => {
    if (outside(event.target)) onclose();
  };
  window.addEventListener("pointerdown", onpointerdown, true);
  window.addEventListener("scroll", onscroll, true);
  return () => {
    resized.disconnect();
    window.removeEventListener("pointerdown", onpointerdown, true);
    window.removeEventListener("scroll", onscroll, true);
  };
});

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "Escape") {
    event.preventDefault();
    event.stopPropagation();
    onclose();
    anchor.focus();
  }
}
</script>

<div
  class="fixed z-20 w-max min-w-[200px] overflow-auto rounded-lg border border-line bg-surface p-1.5 text-[13px] text-body shadow-[0_10px_40px_rgba(0,0,0,.5)] {large
    ? 'max-h-[calc(100vh-16px)] max-w-[calc(100vw-16px)]'
    : 'max-h-[320px] max-w-[360px]'}"
  style:top="{top}px"
  style:left="{left}px"
  role="dialog"
  tabindex="-1"
  bind:this={panel}
  {onkeydown}
>
  {@render children()}
</div>
