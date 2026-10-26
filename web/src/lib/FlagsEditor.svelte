<script lang="ts">
/**
 * A flags field (any values of a list): a button with the labels of the values it holds, which opens a list of
 * checkboxes. Each tick is sent at once, as the data spells the list (`air,enemies`, or bits as a number).
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import type { ChoiceList, FieldView } from "./api.ts";
import Popover from "./Popover.svelte";
import { formatFlags, parseFlags } from "./values.ts";

type Props = {
  field: FieldView;
  list: ChoiceList;
  /** The text shown for the value (the labels). */
  shown: string;
  compact: boolean;
  /** Sends a value; it ends once the app has answered (well or not). */
  onset: (value: string) => Promise<unknown>;
};

let { field, list, shown, compact, onset }: Props = $props();

let open = $state(false);
let button: HTMLButtonElement | undefined = $state();

// The value sent last, until every tick sent has been answered: a second tick before the first is answered builds on
// the first, not on the value shown before it.
let sent = $state<string | null>(null);
let waiting = 0;

const ticked = $derived(parseFlags(sent ?? field.value, list));
// The list's entries, then any value the field holds that the list does not know (so it can be taken out).
const options = $derived([
  ...list.entries,
  ...ticked
    .filter((value) => !list.entries.some((entry) => entry.value === value))
    .map((value) => ({ value, label: list.store === "int" ? `Bit ${value}` : value })),
]);

async function toggle(value: string, on: boolean): Promise<void> {
  const next = on ? [...ticked, value] : ticked.filter((each) => each !== value);
  sent = formatFlags(next, list);
  waiting++;
  await onset(sent);
  waiting--;
  if (waiting === 0) sent = null;
}
</script>

<button
  type="button"
  class="flex w-full min-w-0 items-center gap-1 rounded border px-1.5 py-[3px] text-left font-mono text-[12.5px] hover:border-line focus:border-accent focus:outline-none {field.warning
    ? 'border-warn bg-warn-lo'
    : 'border-transparent bg-bg'} {field.changed ? 'text-changed' : 'text-fg'} {compact
    ? 'max-w-56'
    : ''}"
  title={shown}
  data-field="{field.id}:{field.level}"
  aria-haspopup="dialog"
  aria-expanded={open}
  bind:this={button}
  onclick={() => (open = !open)}
>
  <span class="min-w-0 flex-1 truncate">{shown === "" ? " " : shown}</span>
  <ChevronDown size={12} class="shrink-0 text-faint" />
</button>
{#if open && button}
  <Popover anchor={button} onclose={() => (open = false)}>
    <div class="flex flex-col" role="group" aria-label={field.label}>
      {#each options as option (option.value)}
        <label class="flex cursor-pointer items-center gap-2 rounded px-2 py-1 hover:bg-hover">
          <input
            type="checkbox"
            class="accent-accent"
            checked={ticked.includes(option.value)}
            onchange={(event) => toggle(option.value, event.currentTarget.checked)}
          />
          <span class="flex-1">{option.label}</span>
          <span class="font-mono text-[11.5px] text-faint">{option.value}</span>
        </label>
      {/each}
    </div>
  </Popover>
{/if}
