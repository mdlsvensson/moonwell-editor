<script lang="ts">
/** Per-level fields as a grid: one row per field, its levels (a doodad's variations) across. */
import type { ChoiceList, FieldView } from "./api.ts";
import { isChanged, type Lookup, type Row, shownValue } from "./fields.ts";

type Props = {
  rows: Row[];
  showIds: boolean;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
};

let { rows, showIds, choices, lookup }: Props = $props();

const nameOf = (listOf: string, id: string) => lookup(listOf, id)?.name;
const columns = $derived(
  Array.from(
    { length: Math.max(0, ...rows.map((row) => row.levels?.length ?? 0)) },
    (_, index) => index + 1,
  ),
);

function cellTitle(field: FieldView, shown: string): string {
  const lines = [`Level ${field.level}: ${shown}`];
  if (field.changed)
    lines.push(`Standard: ${shownValue({ ...field, value: field.base }, choices, nameOf)}`);
  if (field.warning) lines.push(field.warning);
  return lines.join("\n");
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
            {/each}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</div>
