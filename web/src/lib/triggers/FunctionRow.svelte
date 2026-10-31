<script lang="ts">
/**
 * One function of a trigger: its category's icon and its sentence, each parameter a link that opens the parameter. A
 * function that holds others (an If, a loop) shows its blocks under it, one deeper, each with its own + button.
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import Plus from "@lucide/svelte/icons/plus";
import { iconUrl } from "../api.ts";
import { t } from "../text.ts";
import type { BlockView, Catalog, FunctionView, ParameterView } from "./api.ts";
import FunctionRow from "./FunctionRow.svelte";

type Props = {
  function_: FunctionView;
  catalog: Catalog;
  mode: string;
  readOnly: boolean;
  selected: string | null;
  depth: number;
  onselect: (function_: FunctionView) => void;
  onparameter: (parameter: ParameterView) => void;
  onchange: (function_: FunctionView) => void;
  onmenu: (function_: FunctionView, x: number, y: number) => void;
  onadd: (parent: FunctionView, block: BlockView) => void;
  onmenublock: (parent: FunctionView, block: BlockView, x: number, y: number) => void;
};

let {
  function_,
  catalog,
  mode,
  readOnly,
  selected,
  depth,
  onselect,
  onparameter,
  onchange,
  onmenu,
  onadd,
  onmenublock,
}: Props = $props();

// Blocks start open, as World Editor shows them; the user closes one here.
let closed = $state<Record<number, boolean>>({});

const icon = $derived(
  catalog.categories.find((each) => each.key === function_.category)?.icon ?? "",
);
const indent = $derived(`${depth * 18 + 6}px`);
</script>

<div
  class="group flex items-start gap-1.5 rounded py-[3px] pr-2 {selected === function_.path
    ? 'bg-accent-lo'
    : 'hover:bg-hover'}"
  style:padding-left={indent}
  role="treeitem"
  aria-selected={selected === function_.path}
  tabindex="-1"
  data-function={function_.path}
  onclick={() => onselect(function_)}
  ondblclick={() => !readOnly && onchange(function_)}
  onkeydown={(event) => event.key === "Enter" && !readOnly && onchange(function_)}
  oncontextmenu={(event) => {
    event.preventDefault();
    onselect(function_);
    onmenu(function_, event.clientX, event.clientY);
  }}
>
  {#if icon}
    <img src={iconUrl(icon, mode)} alt="" class="mt-[1px] size-4 shrink-0" loading="lazy" />
  {/if}
  <span class="min-w-0 {function_.enabled ? 'text-body' : 'text-faint line-through'}">
    {#if function_.prefix}<span class="text-muted">{function_.prefix} - </span>{/if}
    {#each function_.sentence as part, index (index)}{#if part.parameter}{@const parameter = part.parameter}<button
          type="button"
          class="rounded-sm underline decoration-dotted underline-offset-2 hover:bg-accent-lo disabled:no-underline {parameter.kind ===
          'none'
            ? 'text-warn'
            : 'text-accent-hi'}"
          disabled={readOnly}
          onclick={(event) => {
            event.stopPropagation();
            onparameter(parameter);
          }}
          ondblclick={(event) => event.stopPropagation()}>{part.text}</button
        >{:else}{part.text}{/if}{/each}
  </span>
</div>

{#each function_.blocks as block (block.branch)}
  <div
    class="group flex items-center gap-1 py-[2px] pr-2 text-muted hover:bg-hover"
    style:padding-left={`${(depth + 1) * 18 + 6}px`}
    role="presentation"
    oncontextmenu={(event) => {
      event.preventDefault();
      onmenublock(function_, block, event.clientX, event.clientY);
    }}
  >
    <button
      type="button"
      class="flex items-center gap-1"
      aria-expanded={!closed[block.branch]}
      onclick={() => (closed[block.branch] = !closed[block.branch])}
    >
      {#if closed[block.branch]}<ChevronRight size={14} />{:else}<ChevronDown size={14} />{/if}
      {block.title}
    </button>
    {#if !readOnly}
      <button
        type="button"
        class="invisible rounded p-0.5 group-hover:visible hover:bg-accent-lo hover:text-accent-hi"
        title={block.kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction")}
        aria-label={block.kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction")}
        onclick={() => onadd(function_, block)}
      >
        <Plus size={13} />
      </button>
    {/if}
  </div>
  {#if !closed[block.branch]}
    {#each block.functions as child (child.path)}
      <FunctionRow
        function_={child}
        {catalog}
        {mode}
        {readOnly}
        {selected}
        depth={depth + 2}
        {onselect}
        {onparameter}
        {onchange}
        {onmenu}
        {onadd}
        {onmenublock}
      />
    {/each}
  {/if}
{/each}
