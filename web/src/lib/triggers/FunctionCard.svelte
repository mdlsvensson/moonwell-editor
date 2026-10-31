<script lang="ts">
/**
 * An event, condition or action: a small card with its category's icon and title, then World Editor's sentence with each
 * parameter a chip that opens it. A function that holds others (an If, a loop) has their cards in lanes under it. It is
 * dragged by its title bar, within a trigger or to another one (Sven, 2026-10-31).
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import Plus from "@lucide/svelte/icons/plus";
import { iconUrl } from "../api.ts";
import { t } from "../text.ts";
import type { Catalog, FunctionView } from "./api.ts";
import { type CardActions, showsSentence, toneOf } from "./cards.ts";
import FunctionCard from "./FunctionCard.svelte";

type Props = {
  owner: number;
  function_: FunctionView;
  catalog: Catalog;
  mode: string;
  readOnly: boolean;
  /** Where a drag would land: `owner:path:before`, `owner:path:after` or `owner:path:block0`. */
  dropKey: string | null;
  actions: CardActions;
};

let { owner, function_, catalog, mode, readOnly, dropKey, actions }: Props = $props();

// Lanes start open; the user closes one here.
let closed = $state<Record<number, boolean>>({});

const icon = $derived(
  catalog.categories.find((each) => each.key === function_.category)?.icon ?? "",
);
const here = $derived(`${owner}:${function_.path}`);
</script>

<div
  class="rounded-[7px] border border-black bg-bg"
  class:opacity-50={!function_.enabled}
  class:shadow-[0_-2px_0_var(--accent)]={dropKey === `${here}:before`}
  class:shadow-[0_2px_0_var(--accent)]={dropKey === `${here}:after`}
  role="group"
  aria-label={function_.title}
  data-function={function_.path}
  oncontextmenu={(event) => {
    event.preventDefault();
    event.stopPropagation();
    actions.functionMenu(owner, function_, event.clientX, event.clientY);
  }}
  ondragover={(event) => {
    event.stopPropagation();
    actions.overFunction(event, owner, function_);
  }}
  ondrop={(event) => {
    event.stopPropagation();
    actions.dropFunction(event);
  }}
>
  <div
    class="flex items-center gap-1.5 rounded-t-[6px] px-2 py-[3px] font-medium text-white {toneOf(function_)} {function_.enabled
      ? ''
      : 'line-through'}"
    class:rounded-b-[6px]={!showsSentence(function_) && function_.blocks.length === 0}
    class:cursor-grab={!readOnly}
    role="presentation"
    title={function_.prefix ? `${function_.prefix} - ${function_.title}` : function_.title}
    draggable={!readOnly}
    ondblclick={() => !readOnly && actions.change(owner, function_)}
    ondragstart={(event) => {
      event.stopPropagation();
      actions.dragFunction(owner, function_);
      if (event.dataTransfer) {
        event.dataTransfer.setData("text/plain", function_.title);
        event.dataTransfer.effectAllowed = "move";
      }
    }}
    ondragend={actions.dragEnd}
  >
    {#if icon}
      <img src={iconUrl(icon, mode)} alt="" class="size-[14px] shrink-0" draggable="false" loading="lazy" />
    {/if}
    <span class="truncate">{function_.title}</span>
  </div>

  {#if showsSentence(function_)}
    <div class="px-2 pt-[3px] pb-[5px] leading-5 text-fg">
      {#each function_.sentence as part, index (index)}{#if part.parameter}{@const parameter = part.parameter}<button
            type="button"
            class="mx-px max-w-full rounded-[4px] border bg-raised px-[5px] text-left text-[11.5px] leading-[16px] hover:border-accent disabled:hover:border-line {parameter.kind ===
            'none'
              ? 'border-warn text-warn'
              : 'border-line text-fg'}"
            disabled={readOnly}
            onclick={() => actions.parameter(owner, parameter)}>{part.text}</button
          >{:else}{part.text}{/if}{/each}
    </div>
  {/if}

  {#each function_.blocks as block (block.branch)}
    <div
      class="group flex items-center gap-1 px-2 py-[2px] text-[11px] text-muted"
      class:bg-accent-lo={dropKey === `${here}:block${block.branch}`}
      role="presentation"
      oncontextmenu={(event) => {
        event.preventDefault();
        event.stopPropagation();
        actions.blockMenu(owner, function_, block, event.clientX, event.clientY);
      }}
      ondragover={(event) => {
        event.stopPropagation();
        actions.overBlock(event, owner, function_, block);
      }}
      ondrop={(event) => {
        event.stopPropagation();
        actions.dropFunction(event);
      }}
    >
      <button
        type="button"
        class="flex items-center gap-0.5 hover:text-fg"
        aria-expanded={!closed[block.branch]}
        onclick={() => (closed[block.branch] = !closed[block.branch])}
      >
        {#if closed[block.branch]}<ChevronRight size={12} />{:else}<ChevronDown size={12} />{/if}
        {block.title}
      </button>
      {#if !readOnly}
        <button
          type="button"
          class="ml-auto rounded-[4px] bg-raised p-px text-fg hover:bg-accent hover:text-bg"
          title={block.kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction")}
          aria-label={block.kind === "condition" ? t("triggers.addCondition") : t("triggers.addAction")}
          onclick={() => actions.addToBlock(owner, function_, block)}
        >
          <Plus size={11} />
        </button>
      {/if}
    </div>
    {#if !closed[block.branch] && block.functions.length > 0}
      <div class="flex flex-col gap-1 px-1.5 pb-1.5">
        {#each block.functions as child (child.path)}
          <FunctionCard {owner} function_={child} {catalog} {mode} {readOnly} {dropKey} {actions} />
        {/each}
      </div>
    {/if}
  {/each}
</div>
