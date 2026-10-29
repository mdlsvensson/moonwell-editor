<script lang="ts">
/**
 * A small dialog over the page: a title, its text and its buttons, on Bits UI's dialog (Sven, 2026-10-28). Esc or a
 * click on the veil cancels; the first input, else the first button, gets the focus when it opens, Tab stays inside
 * it, and the focus goes back where it was when it closes. It stays open until the page takes it away: a cancel the
 * page does not act on (the settings' Esc while a hotkey is being pressed) leaves it there, where Bits UI had closed
 * it on its own and the page, still thinking it open, could not open it again (Sven, 2026-10-29). Its shadow is a
 * small one (Sven, 2026-10-29).
 */
import { Dialog } from "bits-ui";
import type { Component, Snippet } from "svelte";

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
  /** An icon before the title (the alpha notice's warning). */
  icon?: Component<{ size?: number; class?: string }>;
};

let {
  title,
  oncancel,
  children,
  actions,
  wide = false,
  note,
  large = false,
  icon: Icon,
}: Props = $props();

let box: HTMLDivElement | null = $state(null);
let open = $state(true);

function onOpenChange(next: boolean): void {
  if (next) return;
  open = true;
  oncancel();
}

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

<Dialog.Root bind:open {onOpenChange}>
  <Dialog.Portal>
    <Dialog.Overlay class="fixed inset-0 z-30 bg-black/55" />
    <Dialog.Content
      bind:ref={box}
      onOpenAutoFocus={focusFirst}
      class="fixed left-1/2 z-30 -translate-x-1/2 {large
        ? 'top-[60px] max-h-[calc(100%-90px)] w-[760px] overflow-auto'
        : wide
          ? 'top-[60px] max-h-[calc(100%-90px)] w-[620px] overflow-auto'
          : 'top-[120px] w-[460px]'} max-w-[calc(100%-32px)] rounded-[10px] border border-line bg-surface p-4 shadow-[0_6px_18px_rgba(0,0,0,.35)] outline-none"
    >
      <Dialog.Title
        class="m-0 mb-2 flex gap-2 font-heading text-base font-bold text-fg {Icon ? 'items-center' : 'items-baseline'}"
      >
        {#if Icon}<Icon size={18} class="shrink-0 text-changed" />{/if}
        {title}
        {#if note}<span class="font-mono text-[12.5px] font-normal text-muted">{note}</span>{/if}
      </Dialog.Title>
      <div class="text-[13.5px] text-body">{@render children()}</div>
      <div class="mt-4 flex justify-end gap-2">{@render actions()}</div>
    </Dialog.Content>
  </Dialog.Portal>
</Dialog.Root>
