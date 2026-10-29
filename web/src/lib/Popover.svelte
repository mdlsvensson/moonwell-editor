<script lang="ts">
/**
 * A small panel under the element that opened it (the flags' checkboxes, the object picker), on Bits UI's popover
 * (Sven, 2026-10-28). It is placed on the page, not in its scrolling box, so a level grid's sideways scroll cannot cut
 * it off; it flips above the element when there is no room below, follows it when the page under it scrolls, and
 * hides while the element is scrolled out of view. A click outside it or Esc closes it.
 */
import { Popover } from "bits-ui";
import type { Snippet } from "svelte";

type Props = {
  anchor: HTMLElement;
  onclose: () => void;
  children: Snippet;
  /** A large panel (the icon and model pickers): as big as its content, up to the window. */
  large?: boolean;
};

let { anchor, onclose, children, large = false }: Props = $props();
</script>

<Popover.Root open onOpenChange={(open) => !open && onclose()}>
  <Popover.Portal>
    <Popover.Content
      customAnchor={anchor}
      side="bottom"
      align="start"
      sideOffset={4}
      collisionPadding={8}
      hideWhenDetached
      trapFocus={false}
      onOpenAutoFocus={(event) => event.preventDefault()}
      onCloseAutoFocus={(event) => event.preventDefault()}
      onEscapeKeydown={() => anchor.focus()}
      onInteractOutside={(event) => {
        // The element that opened it closes it itself (a second click on a dropdown's button).
        if (event.target instanceof Node && anchor.contains(event.target)) event.preventDefault();
      }}
      data-popup
      class="z-40 w-max min-w-[200px] overflow-auto rounded-lg border border-line bg-surface p-1.5 text-[13px] text-body shadow-[0_4px_12px_rgba(0,0,0,.3)] outline-none {large
        ? 'max-h-[calc(100vh-16px)] max-w-[calc(100vw-16px)]'
        : 'max-h-[320px] max-w-[360px]'}"
    >
      {@render children()}
    </Popover.Content>
  </Popover.Portal>
</Popover.Root>
