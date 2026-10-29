<script lang="ts">
/**
 * The tooltip editor (Sven, 2026-10-27): the object's tooltip as the game shows it, its texts editable in place. Each
 * text names its field above its box when hovered or edited. The toolbar colours the selected text (the colours the
 * game's own tooltips use, no colour, and up to eight colours of the user's own, each picked with the picker) and, for
 * an ability, puts in the placeholders that show its values (`<AHhb,Dur1>`),
 * which show as chips named for what they stand for. The icon, hotkey and costs show as the game shows them but are
 * edited in their fields. An object with levels is edited a level at a time; Save sends every changed text as one
 * edit.
 */
import ChevronLeft from "@lucide/svelte/icons/chevron-left";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import Eraser from "@lucide/svelte/icons/eraser";
import Plus from "@lucide/svelte/icons/plus";
import { type FieldView, iconUrl, type ObjectView } from "./api.ts";
import ColorPicker from "./ColorPicker.svelte";
import Dialog from "./Dialog.svelte";
import { uiIcons } from "./gameIcons.ts";
import { graphics } from "./graphics.svelte.ts";
import Popover from "./Popover.svelte";
import RichText from "./RichText.svelte";
import { tokenOf, tokenParts } from "./richText.ts";
import { t, tCount } from "./text.ts";
import { abilityTextValues, formatNumber, textKey, tooltipOf } from "./tooltip.ts";

type Props = {
  view: ObjectView;
  /** Changes several fields as one edit; answers with why it was refused, or null. */
  onfields: (edits: { field: FieldView; value: string }[]) => Promise<string | null>;
  onclose: () => void;
};

let { view, onfields, onclose }: Props = $props();

/** A text of the tooltip: its field, and whether it is one line (a title). */
type Part = { id: string; single: boolean };

// The texts of each kind's tooltip, in the boxes the game shows them in: the tooltip, an ability's turned-off
// tooltip, and a hero ability's learn tooltip.
const boxes: Record<string, { key: string; parts: Part[] }[]> = {
  unit: [
    {
      key: "main",
      parts: [
        { id: "utip", single: true },
        { id: "utub", single: false },
      ],
    },
  ],
  item: [
    {
      key: "main",
      parts: [
        { id: "utip", single: true },
        { id: "utub", single: false },
      ],
    },
  ],
  ability: [
    {
      key: "main",
      parts: [
        { id: "atp1", single: true },
        { id: "aub1", single: false },
      ],
    },
    {
      key: "off",
      parts: [
        { id: "aut1", single: true },
        { id: "auu1", single: false },
      ],
    },
    {
      key: "learn",
      parts: [
        { id: "aret", single: true },
        { id: "arut", single: false },
      ],
    },
  ],
  upgrade: [
    {
      key: "main",
      parts: [
        { id: "gtp1", single: true },
        { id: "gub1", single: false },
      ],
    },
  ],
  buff: [
    {
      key: "main",
      parts: [
        { id: "ftip", single: true },
        { id: "fube", single: false },
      ],
    },
  ],
};

const boxNames: Record<string, string> = {
  off: t("tooltipEditor.box.off"),
  learn: t("tooltipEditor.box.learn"),
};

let level = $state(1);
const shownLevel = $derived(Math.min(Math.max(1, level), Math.max(1, view.levels)));
const tip = $derived(tooltipOf(view, shownLevel));
const values = $derived(view.kind === "ability" ? abilityTextValues(view) : {});

/** A field at a level: the per-level field's level, else the field without levels. */
function fieldAt(id: string): FieldView | undefined {
  return (
    view.fields.find((field) => field.id === id && field.level === shownLevel) ??
    view.fields.find((field) => field.id === id && field.level === 0)
  );
}

// The boxes this object has: a box whose fields it lacks is left out, as are a learn tooltip of an ability that is
// not a hero's and a turned-off tooltip of an ability that has none (it is written in its fields).
const shownBoxes = $derived(
  (boxes[view.kind] ?? [])
    .filter((box) => box.key !== "learn" || fieldAt("aher")?.value !== "0")
    .filter(
      (box) =>
        box.key !== "off" ||
        box.parts.some((part) => {
          const field = fieldAt(part.id);
          return field !== undefined && (field.value !== "" || field.base !== "");
        }),
    )
    .map((box) => ({
      key: box.key,
      parts: box.parts.flatMap((part) => {
        const field = fieldAt(part.id);
        return field ? [{ ...part, field }] : [];
      }),
    }))
    .filter((box) => box.parts.length > 0),
);

// The texts as edited, by `field:level`, until they are saved.
let drafts = $state<Record<string, string>>({});
let editors = $state<Record<string, RichText | undefined>>({});
let active = $state<string | null>(null);
// The text the mouse is over, and the one with the cursor: the first is named above its box, else the second.
let hovered = $state<string | null>(null);
let focused = $state<string | null>(null);
const named = $derived.by(() => {
  const key = hovered ?? focused;
  for (const box of shownBoxes) {
    const part = box.parts.find((each) => keyOf(each.field) === key);
    if (part) return { box: box.key, field: part.field };
  }
  return null;
});
let error = $state("");
let saving = $state(false);

const keyOf = (field: FieldView) => `${field.id}:${field.level}`;

// The colours the game's own tooltips use, most used first (counted in the game's ability and item texts, Sven,
// 2026-10-29), so they are kept at hand.
const presets = [
  { css: "#ffcc00", name: t("tooltipEditor.colour.gold") },
  { css: "#c3dbff", name: t("tooltipEditor.colour.autocast") },
  { css: "#aeaeae", name: t("tooltipEditor.colour.common") },
  { css: "#40da00", name: t("tooltipEditor.colour.uncommon") },
  { css: "#6295e3", name: t("tooltipEditor.colour.rare") },
  { css: "#7c2abb", name: t("tooltipEditor.colour.epic") },
  { css: "#f28302", name: t("tooltipEditor.colour.legendary") },
  { css: "#ffff00", name: t("tooltipEditor.colour.quest") },
  { css: "#87ceeb", name: t("tooltipEditor.colour.consumable") },
];

// The user's own colours, after the divider: + picks a new one (until there are `maxOwn`), and a click on one colours
// the selection and opens the picker to change it (Sven, 2026-10-29). The picker is the editor's own: WebView2's could
// not pick a colour. They are remembered in the window's storage.
const maxOwn = 8;
const ownKey = "moonwell-editor.tooltip-colours";
let own = $state<string[]>(savedOwn());
// The picker, open under + (index null, a new colour) or under one of the user's colours.
let picking = $state<{ index: number | null; anchor: HTMLElement } | null>(null);

function savedOwn(): string[] {
  try {
    const saved = JSON.parse(localStorage.getItem(ownKey) ?? "[]") as unknown;
    return Array.isArray(saved)
      ? saved
          .filter(
            (each): each is string => typeof each === "string" && /^#[0-9a-f]{6}$/i.test(each),
          )
          .slice(0, maxOwn)
      : [];
  } catch {
    return [];
  }
}

function setOwn(next: string[]): void {
  own = next;
  try {
    localStorage.setItem(ownKey, JSON.stringify(next));
  } catch {
    // Not remembered: the next start has none.
  }
}

// The ability's fields a placeholder can name, by the game's key, with their names.
const insertable = $derived.by(() => {
  if (view.kind !== "ability") return [];
  const seen = new Map<string, string>();
  for (const field of view.fields) {
    const key = textKey(field);
    if (key && field.level > 0 && !seen.has(key)) seen.set(key, field.label);
  }
  return [...seen].map(([key, name]) => ({ key, name }));
});
let inserting = $state(false);
let insertButton: HTMLButtonElement | undefined = $state();
let percent = $state(false);

const keyNames: Record<string, string> = {
  Dur: t("tooltipEditor.key.dur"),
  HeroDur: t("tooltipEditor.key.heroDur"),
  Cool: t("tooltipEditor.key.cool"),
  Cost: t("tooltipEditor.key.cost"),
  Rng: t("tooltipEditor.key.rng"),
  Area: t("tooltipEditor.key.area"),
};

/** What a placeholder chip says: the value it stands for, named, with its level and value when it is this object's. */
function describe(token: string): { label: string; title: string } {
  const parts = tokenParts(token);
  if (!parts) return { label: token, title: token };
  const own = parts.id === view.id || parts.id === view.baseId;
  const named =
    insertable.find((each) => each.key.toLowerCase() === parts.key.toLowerCase())?.name ??
    keyNames[parts.key] ??
    parts.key;
  const value = own ? values[`${parts.key}${parts.level}`] : undefined;
  const label = [
    own ? "" : `${parts.id}: `,
    named,
    parts.level > 0 ? ` ${parts.level}` : "",
    value !== undefined ? ` = ${formatNumber(value, parts.percent)}` : parts.percent ? " ×100" : "",
  ].join("");
  return { label, title: t("tooltipEditor.chipTitle", { token, name: named }) };
}

function insert(key: string): void {
  inserting = false;
  const editor = active ? editors[active] : undefined;
  editor?.insert(tokenOf({ id: view.id, key, level: shownLevel, percent }));
}

function colorSelection(css: string | null): void {
  const editor = active ? editors[active] : undefined;
  editor?.color(css);
}

async function save(): Promise<void> {
  const edits = Object.entries(drafts).flatMap(([key, value]) => {
    const [id, at] = key.split(":");
    const field = view.fields.find((each) => each.id === id && String(each.level) === at);
    return field && field.value !== value ? [{ field, value }] : [];
  });
  if (edits.length === 0) {
    onclose();
    return;
  }
  saving = true;
  const problem = await onfields(edits);
  saving = false;
  if (problem === null) onclose();
  else error = problem;
}

const changedCount = $derived(
  Object.entries(drafts).filter(([key, value]) => {
    const [id, at] = key.split(":");
    return view.fields.find((each) => each.id === id && String(each.level) === at)?.value !== value;
  }).length,
);
</script>

{#snippet text(part: Part & { field: FieldView }, look: string)}
  {@const key = keyOf(part.field)}
  <!-- Hovered or edited, a text names its field above its box, where it covers nothing (Sven, 2026-10-29). -->
  <div
    class="-mx-1 rounded px-1 outline-1 outline-transparent outline-dashed hover:outline-white/30 focus-within:outline-accent"
    role="group"
    onmouseenter={() => (hovered = key)}
    onmouseleave={() => (hovered = null)}
    onfocusin={() => (focused = key)}
    onfocusout={() => (focused = null)}
  >
    {#key key}
      <RichText
        bind:this={editors[key]}
        markup={drafts[key] ?? part.field.value}
        single={part.single}
        {describe}
        label={part.field.label}
        limit={part.field.maxLength}
        class={look}
        onfocus={() => (active = key)}
        onchange={(markup) => (drafts[key] = markup)}
      />
    {/key}
  </div>
{/snippet}

<Dialog title={t("tooltipEditor.title", { name: view.name })} note={view.id} oncancel={onclose} large>
  <!-- The toolbar: colours for the selected text, and an ability's values to put in. -->
  <div class="mb-2 flex flex-wrap items-center gap-1.5" role="toolbar" aria-label={t("tooltipEditor.toolbar")}>
    {#each presets as preset (preset.css)}
      <button
        type="button"
        class="size-5 rounded border border-line hover:border-fg"
        style:background-color={preset.css}
        title={t("tooltipEditor.presetTitle", { name: preset.name, colour: preset.css })}
        aria-label={t("tooltipEditor.colourTitle", { colour: preset.name })}
        onpointerdown={(event) => event.preventDefault()}
        onclick={() => colorSelection(preset.css)}
      ></button>
    {/each}
    <button
      type="button"
      class="flex size-5 items-center justify-center rounded border border-line text-muted hover:border-fg hover:text-fg"
      title={t("tooltipEditor.noColourTitle")}
      aria-label={t("tooltipEditor.noColourTitle")}
      onpointerdown={(event) => event.preventDefault()}
      onclick={() => colorSelection(null)}
    >
      <Eraser size={12} />
    </button>
    <span class="mx-1 h-4 w-px bg-line" aria-hidden="true"></span>
    <!-- The user's own colours, then + while there is room; the picker opens under the one clicked. -->
    {#each own as colour, index (index)}
      <button
        type="button"
        class="size-5 rounded border hover:border-fg {picking?.index === index ? 'border-fg' : 'border-line'}"
        style:background-color={colour}
        title={t("tooltipEditor.ownTitle", { colour })}
        aria-label={t("tooltipEditor.ownTitle", { colour })}
        aria-haspopup="dialog"
        onpointerdown={(event) => event.preventDefault()}
        onclick={(event) => {
          colorSelection(colour);
          picking = { index, anchor: event.currentTarget };
        }}
      ></button>
    {/each}
    {#if own.length < maxOwn}
      <button
        type="button"
        class="flex size-5 items-center justify-center rounded border border-dashed text-muted hover:border-accent hover:text-fg {picking?.index ===
        null
          ? 'border-accent'
          : 'border-line'}"
        title={t("tooltipEditor.addColour", { max: maxOwn })}
        aria-label={t("tooltipEditor.addColour", { max: maxOwn })}
        aria-haspopup="dialog"
        onpointerdown={(event) => event.preventDefault()}
        onclick={(event) => (picking = { index: null, anchor: event.currentTarget })}
      >
        <Plus size={12} />
      </button>
    {/if}
    {#if insertable.length > 0}
      <span class="mx-1 h-4 w-px bg-line" aria-hidden="true"></span>
      <button
        type="button"
        class="flex items-center gap-1 rounded border border-line bg-raised px-2 py-0.5 text-[12.5px] text-fg hover:border-accent"
        aria-haspopup="dialog"
        aria-expanded={inserting}
        bind:this={insertButton}
        onpointerdown={(event) => event.preventDefault()}
        onclick={() => (inserting = !inserting)}
      >
        <Plus size={13} />
        {t("tooltipEditor.insert")}
      </button>
    {/if}
    {#if view.levels > 1}
      <div class="ml-auto flex items-center gap-1 text-[12.5px] text-muted">
        <button
          type="button"
          class="rounded p-0.5 text-faint hover:bg-hover hover:text-fg disabled:opacity-40"
          aria-label={t("tooltip.levelBefore")}
          disabled={shownLevel <= 1}
          onclick={() => (level = shownLevel - 1)}
        >
          <ChevronLeft size={14} />
        </button>
        <span class="min-w-16 text-center">{t("tooltip.level", { level: shownLevel, levels: view.levels })}</span>
        <button
          type="button"
          class="rounded p-0.5 text-faint hover:bg-hover hover:text-fg disabled:opacity-40"
          aria-label={t("tooltip.levelAfter")}
          disabled={shownLevel >= view.levels}
          onclick={() => (level = shownLevel + 1)}
        >
          <ChevronRight size={14} />
        </button>
      </div>
    {/if}
  </div>

  <!-- Opened anew for each colour clicked. -->
  {#key picking}
    {#if picking}
      {@const at = picking.index}
      <Popover anchor={picking.anchor} onclose={() => (picking = null)}>
      <ColorPicker
        value={at === null ? (own.at(-1) ?? "#ffcc00") : (own[at] ?? "#ffffff")}
        action={at === null ? t("colourPicker.add") : t("colourPicker.change")}
        onpick={(css) => {
          setOwn(at === null ? [...own, css] : own.map((each, index) => (index === at ? css : each)));
          colorSelection(css);
          picking = null;
        }}
      />
    </Popover>
    {/if}
  {/key}

  {#if inserting && insertButton}
    <Popover anchor={insertButton} onclose={() => (inserting = false)}>
      <div class="flex flex-col" role="menu" aria-label={t("tooltipEditor.insert")}>
        <p class="m-0 px-2 pt-1 pb-1.5 text-[12px] text-faint">
          {t("tooltipEditor.insertNote", { level: shownLevel })}
        </p>
        {#each insertable as each (each.key)}
          <button
            type="button"
            role="menuitem"
            class="flex items-center gap-3 rounded px-2 py-1 text-left hover:bg-hover hover:text-fg"
            onpointerdown={(event) => event.preventDefault()}
            onclick={() => insert(each.key)}
          >
            <span class="flex-1">{keyNames[each.key] ?? each.name}</span>
            <span class="font-mono text-[11.5px] text-faint">{each.key}{shownLevel}</span>
            {#if values[`${each.key}${shownLevel}`] !== undefined}
              <span class="font-mono text-[11.5px] text-muted">{formatNumber(values[`${each.key}${shownLevel}`] ?? "")}</span>
            {/if}
          </button>
        {/each}
        <label class="mt-1 flex items-center gap-2 border-t border-line px-2 pt-1.5 pb-0.5 text-[12.5px]">
          <input type="checkbox" bind:checked={percent} />
          {t("tooltipEditor.percent")}
        </label>
      </div>
    </Popover>
  {/if}

  {#each shownBoxes as box (box.key)}
    <div class="mb-1 flex h-4 items-baseline gap-2 text-[12px] {box.key === 'main' ? '' : 'mt-3'}">
      {#if box.key !== "main"}
        <span class="tracking-[.06em] text-faint uppercase">{boxNames[box.key]}</span>
      {/if}
      {#if named?.box === box.key}
        <span class="ml-auto truncate text-muted">
          {named.field.label} · <span class="font-mono">{named.field.id}</span>{#if named.field.level > 0}
            · {t("tooltipEditor.levelTag", { level: named.field.level })}{/if}
          {#if named.field.maxLength}
            {@const length = (drafts[keyOf(named.field)] ?? named.field.value).length}
            · <span class="font-mono {length >= named.field.maxLength ? 'text-warn' : ''}"
              >{t("tooltipEditor.length", { length, max: named.field.maxLength })}</span
            >
          {/if}
        </span>
      {/if}
    </div>
    <div class="rounded-md border border-line bg-black/85 px-3 py-2.5 text-[13px] leading-snug text-white shadow-lg">
      {#if box.key === "main" && tip}
        <div class="flex items-start gap-2">
          {#if tip.icon}
            <img
              src={iconUrl(tip.icon, graphics.mode)}
              alt=""
              class="size-8 shrink-0 rounded-sm border border-white/20"
              title={t("tooltipEditor.notHere")}
            />
          {/if}
          <div class="min-w-0 flex-1">
            {#each box.parts.filter((part) => part.single) as part (part.id)}
              <div class="flex items-baseline gap-1 font-semibold">
                <!-- As wide as the title, so the hotkey follows it. -->
                <div class="min-w-[3ch]">{@render text(part, "")}</div>
                {#if tip.hotkey}
                  <span class="shrink-0" title={t("tooltipEditor.notHere")}
                    >(<span style:color="#ffcc00">{tip.hotkey.charAt(0).toUpperCase()}</span>)</span
                  >
                {/if}
              </div>
            {/each}
            {#if tip.costs.length > 0}
              <!-- Shown as the game shows them; they are edited in their fields. -->
              <div class="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[12px] opacity-80" title={t("tooltipEditor.notHere")}>
                {#each tip.costs as cost (cost.label)}
                  <span class="inline-flex items-center gap-1">
                    {#if cost.icon}
                      <img src={iconUrl(uiIcons[cost.icon], graphics.mode)} alt={cost.label} class="size-4" />
                    {:else}
                      <span class="text-[#a0a0a0]">{cost.label}</span>
                    {/if}
                    {cost.value}
                  </span>
                {/each}
              </div>
            {/if}
          </div>
        </div>
        {#each box.parts.filter((part) => !part.single) as part (part.id)}
          <div class="mt-2">{@render text(part, "")}</div>
        {/each}
      {:else}
        {#each box.parts as part (part.id)}
          <div class={part.single ? "font-semibold" : "mt-2"}>{@render text(part, "")}</div>
        {/each}
      {/if}
    </div>
  {/each}

  {#if error}
    <p class="mt-3 mb-0 text-[12.5px] text-warn" role="alert">{error}</p>
  {/if}

  {#snippet actions()}
    <span class="mr-auto self-center text-[12.5px] text-muted">
      {changedCount === 0 ? t("tooltipEditor.unchanged") : tCount("tooltipEditor.changed", changedCount)}
    </span>
    <button
      type="button"
      class="rounded-[5px] border border-line bg-raised px-3 py-1 text-fg hover:border-accent"
      onclick={onclose}
    >
      {t("dialog.cancel")}
    </button>
    <button
      type="button"
      class="rounded-[5px] bg-accent px-3 py-1 font-bold text-bg hover:bg-accent-hi disabled:opacity-50"
      data-primary
      disabled={saving}
      onclick={save}
    >
      {t("tooltipEditor.save")}
    </button>
  {/snippet}
</Dialog>
