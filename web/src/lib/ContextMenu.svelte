<script module lang="ts">
/** One entry of a context menu; a `separator` draws a line instead. */
export type MenuItem =
  | { label: string; shortcut?: string; disabled?: boolean; danger?: boolean; run: () => void }
  | { separator: true };
</script>

<script lang="ts">
/**
 * The menu a right click opens (Sven, 2026-10-27), at the pointer and kept inside the window, on Bits UI's dropdown
 * menu (Sven, 2026-10-28). The arrow keys and typed letters move between entries, Enter runs one; a click outside it
 * or Esc closes it.
 */
import { DropdownMenu } from "bits-ui";

type Props = {
  x: number;
  y: number;
  items: MenuItem[];
  onclose: () => void;
};

let { x, y, items, onclose }: Props = $props();

// The point the menu opens at, as the box of nothing there.
const point = $derived({ getBoundingClientRect: () => new DOMRect(x, y, 0, 0) });
</script>

<DropdownMenu.Root open onOpenChange={(open) => !open && onclose()}>
  <DropdownMenu.Portal>
    <DropdownMenu.Content
      customAnchor={point}
      side="bottom"
      align="start"
      collisionPadding={4}
      onCloseAutoFocus={(event) => event.preventDefault()}
      data-popup
      class="z-50 min-w-[210px] rounded-lg border border-line bg-surface p-1 text-[13px] text-body shadow-[0_4px_12px_rgba(0,0,0,.3)] outline-none"
      oncontextmenu={(event) => event.preventDefault()}
    >
      {#each items as item, index (index)}
        {#if "separator" in item}
          <DropdownMenu.Separator class="my-1 h-px bg-line" />
        {:else}
          <DropdownMenu.Item
            disabled={item.disabled}
            textValue={item.label}
            onSelect={item.run}
            class="flex w-full cursor-default items-center gap-6 rounded px-2.5 py-1 text-left outline-none data-disabled:opacity-40 data-highlighted:bg-hover {item.danger
              ? 'text-warn'
              : ''}"
          >
            {item.label}
            {#if item.shortcut}
              <span class="ml-auto font-mono text-[11.5px] text-faint">{item.shortcut}</span>
            {/if}
          </DropdownMenu.Item>
        {/if}
      {/each}
    </DropdownMenu.Content>
  </DropdownMenu.Portal>
</DropdownMenu.Root>
