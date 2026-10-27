<script lang="ts" module>
/** One choice of a `Select`: its value, its label, a game icon's URL to show before it, and the group it is under. */
export type SelectOption = { value: string; label: string; icon?: string | null; group?: string };
</script>

<script lang="ts">
/**
 * A dropdown in the page's colours (Sven, 2026-10-27), in place of the browser's select, which can show no icons: a
 * button as wide as its widest choice (the values are in a fixed-width font, so that is its length in characters) that
 * opens the list under it. The list takes the arrows, Home, End, Enter, Esc and a typed letter; choices can have a game
 * icon and sit under group headings.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import { tick } from "svelte";
import Popover from "./Popover.svelte";

type Props = {
  options: SelectOption[];
  value: string;
  onpick: (value: string) => void;
  /** The name for screen readers. */
  label: string;
  /** What the button shows for a value none of the options has. */
  missing?: string;
  /** Classes for the button's border, background and text colour. */
  tone?: string;
  /** True in a level grid's cell: as wide as the cell allows. */
  compact?: boolean;
  /** The `data-field` the details panel finds the editor by. */
  field?: string;
};

let { options, value, onpick, label, missing = value, tone = "", compact = false, field }: Props = $props();

let open = $state(false);
let button: HTMLButtonElement | undefined = $state();
let list: HTMLDivElement | undefined = $state();
let active = $state(0);

const chosen = $derived(options.find((option) => option.value === value));
const hasIcons = $derived(options.some((option) => option.icon));
// The widest label, in characters (and one spare, so it is never cut); the padding, border, gaps and chevron take 32
// pixels, and an icon with its gap 20 more.
const width = $derived(
  `calc(${Math.max(4, missing.length, ...options.map((option) => option.label.length)) + 1}ch + ${hasIcons ? 52 : 32}px)`,
);

async function show(): Promise<void> {
  active = Math.max(0, options.indexOf(chosen as SelectOption));
  open = true;
  await tick();
  list?.focus();
  scrollToActive();
}

function pick(option: SelectOption | undefined): void {
  open = false;
  button?.focus();
  if (option && option.value !== value) onpick(option.value);
}

function scrollToActive(): void {
  list?.querySelector(`[data-index="${active}"]`)?.scrollIntoView({ block: "nearest" });
}

function move(to: number): void {
  active = Math.max(0, Math.min(options.length - 1, to));
  scrollToActive();
}

function onbuttonkey(event: KeyboardEvent): void {
  if (["ArrowDown", "ArrowUp", "Enter", " "].includes(event.key)) {
    event.preventDefault();
    show();
  }
}

function onlistkey(event: KeyboardEvent): void {
  if (event.key === "ArrowDown") move(active + 1);
  else if (event.key === "ArrowUp") move(active - 1);
  else if (event.key === "Home") move(0);
  else if (event.key === "End") move(options.length - 1);
  else if (event.key === "PageDown") move(active + 8);
  else if (event.key === "PageUp") move(active - 8);
  else if (event.key === "Enter" || event.key === " ") pick(options[active]);
  else if (event.key === "Tab") open = false;
  else if (event.key.length === 1 && /\S/.test(event.key)) {
    // A letter goes to the next choice that starts with it, as a select does.
    const letter = event.key.toLowerCase();
    const order = [...options.slice(active + 1), ...options.slice(0, active + 1)];
    const next = order.find((option) => option.label.toLowerCase().startsWith(letter));
    if (next) move(options.indexOf(next));
  } else return;
  event.preventDefault();
}
</script>

<button
  type="button"
  class="flex min-w-0 cursor-pointer items-center gap-1 rounded border py-[3px] pr-1 pl-1.5 text-left font-mono text-[12.5px] focus:border-accent focus:outline-none {tone} {compact
    ? 'w-full'
    : 'max-w-full'}"
  style:width={compact ? undefined : width}
  data-field={field}
  aria-label={label}
  aria-haspopup="listbox"
  aria-expanded={open}
  bind:this={button}
  onclick={() => (open ? (open = false) : show())}
  onkeydown={onbuttonkey}
>
  {#if chosen?.icon}
    <img src={chosen.icon} alt="" class="size-4 shrink-0 rounded-[2px]" draggable="false" />
  {/if}
  <span class="min-w-0 flex-1 truncate" title={chosen?.label ?? missing}>{chosen?.label ?? missing}</span>
  <ChevronDown size={12} class="shrink-0 text-faint" />
</button>
{#if open && button}
  <Popover anchor={button} onclose={() => (open = false)}>
    <div
      class="flex flex-col outline-none"
      role="listbox"
      tabindex="-1"
      aria-label={label}
      aria-activedescendant="select-option-{active}"
      bind:this={list}
      onkeydown={onlistkey}
    >
      {#each options as option, index (index)}
        {#if option.group && option.group !== options[index - 1]?.group}
          <div class="px-2 pt-1.5 pb-0.5 text-[11px] tracking-[.06em] text-faint uppercase">{option.group}</div>
        {/if}
        <div
          id="select-option-{index}"
          data-index={index}
          role="option"
          tabindex="-1"
          aria-selected={option.value === value}
          class="flex cursor-pointer items-center gap-2 rounded px-2 py-1 {index === active
            ? 'bg-hover text-fg'
            : ''} {option.value === value ? 'text-accent-hi' : ''} {option.group ? 'pl-3' : ''}"
          onpointermove={() => (active = index)}
          onclick={() => pick(option)}
          onkeydown={() => {}}
        >
          {#if hasIcons}
            {#if option.icon}
              <img src={option.icon} alt="" class="size-4 shrink-0 rounded-[2px]" draggable="false" />
            {:else}
              <span class="size-4 shrink-0"></span>
            {/if}
          {/if}
          <span class="truncate" title={option.label}>{option.label}</span>
        </div>
      {/each}
    </div>
  </Popover>
{/if}
