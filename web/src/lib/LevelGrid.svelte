<script lang="ts">
/**
 * Per-level fields as a grid: one row per field, its levels (a doodad's variations) across. When the object can be
 * edited, each cell is the level's editor, with ↺ when the level is changed.
 */
import type { ChoiceList, FieldView, Kind } from "./api.ts";
import FieldEditor from "./FieldEditor.svelte";
import { isChanged, type Lookup, type Row, shownValue } from "./fields.ts";

type Props = {
  rows: Row[];
  showIds: boolean;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  /** False for an object that cannot be edited: the values are only shown. */
  editable: boolean;
  onset: (field: FieldView, value: string) => Promise<string | null>;
  onreset: (field: FieldView) => Promise<string | null>;
  onopen: (kind: Kind, id: string) => void;
};

let { rows, showIds, choices, lookup, editable, onset, onreset, onopen }: Props = $props();

// Why a cell's last value was refused, by `field:level`.
let errors = $state<Record<string, string>>({});

const nameOf = (listOf: string, id: string) => lookup(listOf, id)?.name;
const columns = $derived(
  Array.from(
    { length: Math.max(0, ...rows.map((row) => row.levels?.length ?? 0)) },
    (_, index) => index + 1,
  ),
);

function cellTitle(field: FieldView, shown: string): string {
  const lines = [`Level ${field.level}: ${shown}`];
  if (field.changed) lines.push(`Standard: ${standardOf(field)}`);
  if (field.warning) lines.push(field.warning);
  return lines.join("\n");
}

function standardOf(field: FieldView): string {
  return shownValue({ ...field, value: field.base }, choices, nameOf);
}

async function reset(field: FieldView): Promise<void> {
  errors[`${field.id}:${field.level}`] = (await onreset(field)) ?? "";
}
</script>

<div class="grid grid-cols-[22px_1fr] border-t border-line">
  <span></span>
  <div class="overflow-x-auto py-1 pr-2 pb-2">
    <table class="border-collapse">
      <thead>
        <tr>
          <th class="border border-line px-2 py-0.5"></th>
          {#each columns as level (level)}
            <th class="border border-line px-2 py-0.5 text-right font-mono text-[12.5px] font-normal text-muted">
              {level}
            </th>
          {/each}
        </tr>
      </thead>
      <tbody>
        {#each rows as row (row.id)}
          <tr>
            <th class="border border-line px-2 py-0.5 text-left font-normal whitespace-nowrap">
              <span class="inline-flex items-center gap-1.5">
                <span class="h-3.5 w-1 rounded-sm {isChanged(row) ? 'bg-changed' : ''}"></span>
                {row.label}
                {#if showIds}<span class="font-mono text-[12.5px] text-faint">{row.id}</span>{/if}
              </span>
            </th>
            {#each row.levels ?? [] as field (field.level)}
              {@const shown = shownValue(field, choices, nameOf)}
              {@const key = `${field.id}:${field.level}`}
              {#if editable}
                <td
                  class="group border p-0 align-top {field.warning || errors[key]
                    ? 'border-warn'
                    : 'border-line'}"
                  title={field.warning ?? undefined}
                >
                  <span class="flex items-center">
                    <span class="min-w-0 flex-1">
                      <FieldEditor
                        {field}
                        {choices}
                        {lookup}
                        compact
                        {onset}
                        {onopen}
                        bind:error={errors[key]}
                      />
                    </span>
                    {#if field.changed}
                      <button
                        type="button"
                        class="px-1 text-xs text-changed opacity-40 group-hover:opacity-100 hover:text-fg focus:opacity-100"
                        title="Level {field.level} back to the standard value: {standardOf(field) ||
                          '(empty)'}"
                        aria-label="Reset {row.label} level {field.level}"
                        onclick={() => reset(field)}
                      >
                        ↺
                      </button>
                    {/if}
                  </span>
                  {#if errors[key]}
                    <span class="block max-w-56 px-1.5 pb-0.5 text-[11px] whitespace-normal text-warn" role="alert">
                      {errors[key]}
                    </span>
                  {/if}
                </td>
              {:else}
                <td
                  class="max-w-56 truncate border px-2 py-0.5 font-mono text-[12.5px] {field.type ===
                    'int' || field.type === 'float'
                    ? 'text-right'
                    : 'text-left'} {field.changed ? 'text-changed' : 'text-fg'} {field.warning
                    ? 'border-warn bg-warn-lo'
                    : 'border-line'}"
                  title={cellTitle(field, shown)}
                >
                  {shown}
                </td>
              {/if}
            {/each}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</div>
