<script lang="ts">
/**
 * One card of a category's grid (Sven, 2026-10-31). A trigger is the outer card: its events, conditions and actions are
 * cards inside it and are never loose. Comments, variables and custom scripts are cards too, and a category inside this
 * one is a small card that opens with a double click. The grid moves a card by its title bar, marked `data-grip`.
 */
import Braces from "@lucide/svelte/icons/braces";
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import FileCode from "@lucide/svelte/icons/file-code";
import Folder from "@lucide/svelte/icons/folder";
import MessageSquare from "@lucide/svelte/icons/message-square";
import Play from "@lucide/svelte/icons/play";
import Plus from "@lucide/svelte/icons/plus";
import Power from "@lucide/svelte/icons/power";
import Workflow from "@lucide/svelte/icons/workflow";
import { t, tCount } from "../text.ts";
import type { CardView, Catalog, FunctionKind, FunctionView } from "./api.ts";
import { type CardActions, cardTone } from "./cards.ts";
import FunctionCard from "./FunctionCard.svelte";
import { typedBox } from "./typedBox.ts";

type Props = {
  card: CardView;
  catalog: Catalog;
  mode: string;
  readOnly: boolean;
  selected: boolean;
  folded: boolean;
  /** True when the trigger's comment box shows though it is empty (the context menu's Comment). */
  commenting: boolean;
  dropKey: string | null;
  actions: CardActions;
  onfold: (folded: boolean) => void;
};

let {
  card,
  catalog,
  mode,
  readOnly,
  selected,
  folded,
  commenting,
  dropKey,
  actions,
  onfold,
}: Props = $props();

const trigger = $derived(card.trigger);
const icons = {
  root: FileCode,
  category: Folder,
  trigger: Workflow,
  custom: FileCode,
  script: FileCode,
  comment: MessageSquare,
  variable: Braces,
};
const Icon = $derived(icons[card.kind]);
const box =
  "w-full resize-none rounded-[5px] border border-line bg-raised px-1.5 py-1 text-fg outline-none focus:border-accent disabled:opacity-60";
const variableText = $derived.by(() => {
  const each = card.variable;
  if (!each) return "";
  const type = catalog.types.find((other) => other.key === each.type)?.title ?? each.type;
  const size = each.isArray ? ` [${each.size}]` : "";
  return each.initial ? `${type}${size} · ${each.initial}` : `${type}${size}`;
});

function lines(text: string): number {
  return Math.min(16, Math.max(3, text.split("\n").length));
}
</script>

{#snippet section(title: string, kind: FunctionKind, functions: FunctionView[], addLabel: string)}
  <section
    class="px-[7px] pt-[5px]"
    aria-label={title}
    ondragover={(event) => actions.overSection(event, card.id, kind)}
    ondrop={actions.dropFunction}
  >
    <div
      class="flex items-center px-0.5 pb-[3px] text-[11px] text-muted"
      class:text-fg={dropKey === `${card.id}:section:${kind}`}
    >
      {title}
      {#if !readOnly}
        <button
          type="button"
          class="ml-auto rounded-[4px] bg-raised p-px text-fg hover:bg-accent hover:text-bg"
          title={addLabel}
          aria-label={addLabel}
          onclick={() => actions.add(card.id, kind)}
        >
          <Plus size={11} />
        </button>
      {/if}
    </div>
    {#if functions.length > 0}
      <div class="flex flex-col gap-1">
        {#each functions as function_ (function_.path)}
          <FunctionCard owner={card.id} {function_} {catalog} {mode} {readOnly} {dropKey} {actions} />
        {/each}
      </div>
    {/if}
  </section>
{/snippet}

<div
  class="rounded-[8px] border border-black bg-surface text-xs text-text"
  class:outline-2={selected}
  class:outline-offset-1={selected}
  class:outline-[#f0a020]={selected}
  class:pb-[7px]={!folded && card.kind === "trigger"}
  role="group"
  aria-label={card.name}
  data-card-body
  oncontextmenu={(event) => {
    event.preventDefault();
    actions.cardMenu(card, event.clientX, event.clientY);
  }}
>
  <div
    class="flex items-center gap-1 rounded-t-[7px] px-1.5 py-[5px] font-medium text-white {cardTone(card.kind)} {trigger &&
    !trigger.enabled
      ? 'opacity-60'
      : ''}"
    class:rounded-b-[7px]={folded}
    class:cursor-grab={!readOnly}
    data-grip
    role="presentation"
    onpointerdown={() => actions.select(card.id)}
    ondblclick={(event) => {
      if (!(event.target as HTMLElement).closest("button")) actions.open(card.id);
    }}
  >
    {#if card.kind !== "category"}
      <button
        type="button"
        class="shrink-0 text-white/70 hover:text-white"
        aria-label={folded ? t("triggers.unfold") : t("triggers.fold")}
        title={folded ? t("triggers.unfold") : t("triggers.fold")}
        aria-expanded={!folded}
        onclick={() => onfold(!folded)}
      >
        {#if folded}<ChevronRight size={13} />{:else}<ChevronDown size={13} />{/if}
      </button>
    {/if}
    <Icon size={13} class="shrink-0" />
    <span class="truncate">{card.name}</span>
    {#if trigger && (card.kind === "trigger" || card.kind === "custom" || card.kind === "script")}
      <button
        type="button"
        class="ml-auto shrink-0 rounded-[4px] p-0.5 hover:bg-white/15 {trigger.enabled ? 'text-white' : 'text-white/40'}"
        title={t("triggers.enabled")}
        aria-label={t("triggers.enabled")}
        aria-pressed={trigger.enabled}
        disabled={readOnly}
        onclick={() => actions.options(card.id, { enabled: !trigger.enabled })}
      >
        <Power size={12} />
      </button>
    {/if}
    {#if trigger && (card.kind === "trigger" || card.kind === "custom")}
      <button
        type="button"
        class="shrink-0 rounded-[4px] p-0.5 hover:bg-white/15 {trigger.initiallyOn ? 'text-white' : 'text-white/40'}"
        title={t("triggers.initiallyOn")}
        aria-label={t("triggers.initiallyOn")}
        aria-pressed={trigger.initiallyOn}
        disabled={readOnly}
        onclick={() => actions.options(card.id, { initiallyOn: !trigger.initiallyOn })}
      >
        <Play size={12} />
      </button>
    {/if}
  </div>

  {#if !folded}
    {#if card.kind === "category"}
      <p class="m-0 px-2 py-1.5 text-muted">
        {tCount("triggers.count", card.count)} · {t("triggers.openCategory")}
      </p>
    {:else if card.kind === "variable"}
      <button
        type="button"
        class="block w-full px-2 py-1.5 text-left text-fg hover:bg-hover disabled:hover:bg-transparent"
        disabled={readOnly}
        onclick={() => actions.variable(card)}
      >
        {variableText}
      </button>
    {:else if trigger}
      {@const id = card.id}
      {#if card.kind === "comment"}
        <div class="p-1.5">
          <textarea
            class={box}
            rows={lines(trigger.description)}
            aria-label={t("triggers.comment")}
            value={trigger.description}
            disabled={readOnly}
            use:typedBox={(description) => actions.options(id, { description })}
          ></textarea>
        </div>
      {:else if trigger.description || commenting}
        <div class="px-[7px] pt-[5px]">
          <textarea
            class={box}
            rows={Math.min(5, trigger.description.split("\n").length)}
            aria-label={t("triggers.comment")}
            placeholder={t("triggers.comment")}
            value={trigger.description}
            disabled={readOnly}
            use:typedBox={(description) => actions.options(id, { description })}
          ></textarea>
        </div>
      {/if}
      {#if card.kind === "script" || card.kind === "custom"}
        <div class="p-1.5">
          <textarea
            class="{box} font-mono text-[11px] leading-[1.45]"
            rows={lines(trigger.code ?? "")}
            spellcheck="false"
            aria-label={t("triggers.code")}
            value={trigger.code ?? ""}
            disabled={readOnly}
            use:typedBox={(text) => actions.code(id, text)}
          ></textarea>
        </div>
      {:else if card.kind === "trigger"}
        {@render section(t("triggers.events"), "event", trigger.events, t("triggers.addEvent"))}
        {@render section(t("triggers.conditions"), "condition", trigger.conditions, t("triggers.addCondition"))}
        {@render section(t("triggers.actions"), "action", trigger.actions, t("triggers.addAction"))}
      {/if}
    {/if}
  {/if}
</div>
