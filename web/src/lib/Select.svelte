<script lang="ts" module>
/** One choice of a `Select`: its value, its label and the group it is under. */
export type SelectOption = { value: string; label: string; group?: string };
</script>

<script lang="ts">
/**
 * A dropdown in the page's colours (Sven, 2026-10-27), on Bits UI's select (Sven, 2026-10-28): a button as wide as its
 * widest choice (the values are in a fixed-width font, so that is its length in characters) that opens the list under
 * it, or above it when there is no room below. The list takes the arrows, Home, End, Enter, Esc and typed letters;
 * choices can sit under group headings.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import { Select } from "bits-ui";

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
  /** True when there is nothing to pick: the button shows `missing` and does not open. */
  disabled?: boolean;
};

let {
  options,
  value,
  onpick,
  label,
  missing = value,
  tone = "",
  compact = false,
  field,
  disabled = false,
}: Props = $props();

const chosen = $derived(options.find((option) => option.value === value));
// The widest label, in characters (and one spare, so it is never cut); the padding, border, gaps and chevron take 32
// pixels.
const width = $derived(
  `calc(${Math.max(4, missing.length, ...options.map((option) => option.label.length)) + 1}ch + 32px)`,
);
</script>

<Select.Root
  type="single"
  {value}
  onValueChange={(picked) => picked !== value && onpick(picked)}
  items={options}
  {disabled}
>
  <Select.Trigger
    class="flex min-w-0 cursor-pointer items-center gap-1 rounded border py-[3px] pr-1 pl-1.5 text-left font-mono text-[12.5px] focus:border-accent focus:outline-none disabled:cursor-default disabled:text-faint {tone} {compact
      ? 'w-full'
      : 'max-w-full'}"
    style={compact ? undefined : `width: ${width}`}
    data-field={field}
    aria-label={label}
  >
    <span class="min-w-0 flex-1 truncate" title={chosen?.label ?? missing}>{chosen?.label ?? missing}</span>
    <ChevronDown size={12} class="shrink-0 text-faint" />
  </Select.Trigger>
  <Select.Portal>
    <Select.Content
      side="bottom"
      align="start"
      sideOffset={4}
      collisionPadding={8}
      data-popup
      class="z-40 w-max max-w-[360px] min-w-[200px] rounded-lg border border-line bg-surface p-1.5 text-[13px] text-body shadow-[0_10px_40px_rgba(0,0,0,.5)] outline-none"
    >
      <Select.Viewport class="max-h-[306px]">
        {#each options as option, index (index)}
          {#if option.group && option.group !== options[index - 1]?.group}
            <div class="px-2 pt-1.5 pb-0.5 text-[11px] tracking-[.06em] text-faint uppercase" role="presentation">
              {option.group}
            </div>
          {/if}
          <Select.Item
            value={option.value}
            label={option.label}
            class="flex cursor-pointer items-center gap-2 rounded px-2 py-1 outline-none data-highlighted:bg-hover data-highlighted:text-fg data-selected:text-accent-hi {option.group
              ? 'pl-3'
              : ''}"
          >
            <span class="truncate" title={option.label}>{option.label}</span>
          </Select.Item>
        {/each}
      </Select.Viewport>
    </Select.Content>
  </Select.Portal>
</Select.Root>
