<script lang="ts">
/**
 * A small dialog over the page: a title, its text and its buttons. Esc or a click on the veil cancels; the first
 * input, else the first button, gets the focus when it opens, and Tab stays inside it.
 */
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

let box: HTMLDivElement | undefined = $state();

$effect(() => {
  const first =
    box?.querySelector<HTMLElement>("input, select, textarea") ??
    box?.querySelector<HTMLElement>("[data-primary]") ??
    box?.querySelector<HTMLElement>("button");
  first?.focus();
  if (first instanceof HTMLInputElement) first.select();
});

function focusable(): HTMLElement[] {
  return [
    ...(box?.querySelectorAll<HTMLElement>("button:not(:disabled), input, select, textarea") ?? []),
  ];
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "Escape") {
    event.preventDefault();
    event.stopPropagation();
    oncancel();
  } else if (event.key === "Tab") {
    const items = focusable();
    const first = items[0];
    const last = items[items.length - 1];
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
}
</script>

<div
  class="fixed inset-0 z-30 flex items-start justify-center bg-black/55 {wide || large ? 'pt-[60px]' : 'pt-[120px]'}"
  role="presentation"
  onclick={(event) => {
    if (event.target === event.currentTarget) oncancel();
  }}
>
  <div
    class="{large
      ? 'max-h-[calc(100%-90px)] w-[760px] overflow-auto'
      : wide
        ? 'max-h-[calc(100%-90px)] w-[620px] overflow-auto'
        : 'w-[460px]'} max-w-[calc(100%-32px)] rounded-[10px] border border-line bg-surface p-4 shadow-[0_20px_60px_rgba(0,0,0,.5)]"
    role="dialog"
    aria-modal="true"
    aria-label={title}
    tabindex="-1"
    bind:this={box}
    {onkeydown}
  >
    <h2 class="m-0 mb-2 flex items-baseline gap-2 font-heading text-base font-bold text-fg">
      {title}
      {#if note}<span class="font-mono text-[12.5px] font-normal text-muted">{note}</span>{/if}
    </h2>
    <div class="text-[13.5px] text-body">{@render children()}</div>
    <div class="mt-4 flex justify-end gap-2">{@render actions()}</div>
  </div>
</div>
