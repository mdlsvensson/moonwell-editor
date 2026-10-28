<script lang="ts">
/**
 * A small dialog over the page: a title, its text and its buttons, on Bits UI's dialog (Sven, 2026-10-28). Esc or a
 * click on the veil cancels; the first input, else the first button, gets the focus when it opens, Tab stays inside
 * it, and the focus goes back where it was when it closes.
 */
import { Dialog } from "bits-ui";
import type { Snippet } from "svelte";

type Props = {
  title: string;
  oncancel: () => void;
  children: Snippet;
  actions: Snippet;
  /** A wider dialog that scrolls when it is taller than the window (the settings). */
  wide?: boolean;
  /** A short text beside the title, in the ids' type (an object's id). */
  note?: string;
  /** A large dialog that scrolls when it is taller than the window (the tooltip editor). */
  large?: boolean;
};

let { title, oncancel, children, actions, wide = false, note, large = false }: Props = $props();

let box: HTMLDivElement | null = $state(null);

// The first input (its text selected), else the button marked primary, else the first button.
function focusFirst(event: Event): void {
  event.preventDefault();
  const first =
    box?.querySelector<HTMLElement>("input, select, textarea") ??
    box?.querySelector<HTMLElement>("[data-primary]") ??
    box?.querySelector<HTMLElement>("button");
  first?.focus();
  if (first instanceof HTMLInputElement) first.select();
}
</script>

<Dialog.Root open onOpenChange={(open) => !open && oncancel()}>
  <Dialog.Portal>
    <Dialog.Overlay class="fixed inset-0 z-30 bg-black/55" />
    <Dialog.Content
      bind:ref={box}
      onOpenAutoFocus={focusFirst}
      class="fixed left-1/2 z-30 -translate-x-1/2 {large
        ? 'top-[60px] max-h-[calc(100%-90px)] w-[760px] overflow-auto'
        : wide
          ? 'top-[60px] max-h-[calc(100%-90px)] w-[620px] overflow-auto'
          : 'top-[120px] w-[460px]'} max-w-[calc(100%-32px)] rounded-[10px] border border-line bg-surface p-4 shadow-[0_20px_60px_rgba(0,0,0,.5)] outline-none"
    >
      <Dialog.Title class="m-0 mb-2 flex items-baseline gap-2 font-heading text-base font-bold text-fg">
        {title}
        {#if note}<span class="font-mono text-[12.5px] font-normal text-muted">{note}</span>{/if}
      </Dialog.Title>
      <div class="text-[13.5px] text-body">{@render children()}</div>
      <div class="mt-4 flex justify-end gap-2">{@render actions()}</div>
    </Dialog.Content>
  </Dialog.Portal>
</Dialog.Root>
