<script lang="ts">
/**
 * The tooltip editor (Sven, 2026-10-27): the object's tooltip as the game shows it, its texts editable in place. Each
 * text names its field at the bottom when hovered or edited. The toolbar colours the selected text (the colours the
 * game's own tooltips use, no colour, and up to eight colours of the user's own, each picked with the picker and
 * removed from its context menu) and puts in the placeholders that show the object's values (`<AHhb,Dur1>`,
 * `<h000,realHP>`), which show as chips named for what they stand for. A `/` in a text lists the values it can show to
 * put in: the object's own, and those of the abilities a unit's or an item's lists name (Sven, 2026-10-29). A value
 * that is not whole goes in with one decimal (`,.`) unless the user picks ×100 (`,%`), since the game rounds a plain
 * one down; a buff's tooltip gets no values and a warning for a comma, which the game cuts its extended tooltip at
 * (game facts, probes of 2026-10-29). The icon, hotkey and costs show as the game shows them but are edited in their
 * fields. An object with levels is edited a level at a time; Save sends every changed text as one edit.
 */
import ChevronLeft from "@lucide/svelte/icons/chevron-left";
import ChevronRight from "@lucide/svelte/icons/chevron-right";
import Eraser from "@lucide/svelte/icons/eraser";
import Plus from "@lucide/svelte/icons/plus";
import { type FieldView, iconUrl, type ObjectView, object } from "./api.ts";
import ColorPicker from "./ColorPicker.svelte";
import ContextMenu from "./ContextMenu.svelte";
import Dialog from "./Dialog.svelte";
import { uiIcons } from "./gameIcons.ts";
import { graphics } from "./graphics.svelte.ts";
import Popover from "./Popover.svelte";
import RichText from "./RichText.svelte";
import { keyAt, tokenOf, tokenParts } from "./richText.ts";
import { formsFor, listedAbilities, type SlashSource, sourceOf, suggestions } from "./slash.ts";
import { t, tCount } from "./text.ts";
import { type NumberForm, placeholderNumber, textValues, tooltipOf } from "./tooltip.ts";

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
const values = $derived(textValues(view));

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
// The text the mouse is over, and the one with the cursor: the first is named at the bottom, else the second.
let hovered = $state<string | null>(null);
let focused = $state<string | null>(null);
const named = $derived.by(() => {
  const key = hovered ?? focused;
  for (const box of shownBoxes) {
    const part = box.parts.find((each) => keyOf(each.field) === key);
    if (part) return { field: part.field };
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
// The context menu of one of the user's colours, which removes it (Sven, 2026-10-29).
let colourMenu = $state<{ x: number; y: number; index: number } | null>(null);
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

const keyNames: Record<string, string> = {
  Dur: t("tooltipEditor.key.dur"),
  HeroDur: t("tooltipEditor.key.heroDur"),
  Cool: t("tooltipEditor.key.cool"),
  Cost: t("tooltipEditor.key.cost"),
  Rng: t("tooltipEditor.key.rng"),
  Area: t("tooltipEditor.key.area"),
  realHP: t("tooltipEditor.key.realHP"),
  regenHP: t("tooltipEditor.key.regenHP"),
  mindmg1: t("tooltipEditor.key.mindmg"),
  maxdmg1: t("tooltipEditor.key.maxdmg"),
  uses: t("tooltipEditor.key.uses"),
};

// The object's own values a placeholder can name: an ability's, a unit's or an item's. A buff's tooltip gets none, as
// the game cuts it at the comma every placeholder holds.
const ownSource = $derived(
  view.kind === "ability" || view.kind === "unit" || view.kind === "item"
    ? sourceOf(view, "", keyNames)
    : null,
);
const insertable = $derived(ownSource?.keys ?? []);
let inserting = $state(false);
let insertButton: HTMLButtonElement | undefined = $state();
// How an inserted value shows: "auto" as it is when whole, else with one decimal.
let form = $state<NumberForm | "auto">("auto");

/** The value of a key at the level shown, and the form it goes in with. */
function insertedAt(key: { key: string; whole?: boolean }): {
  value: string | undefined;
  form: NumberForm;
} {
  const value = values[key.whole ? key.key : `${key.key}${shownLevel}`];
  return { value, form: form === "auto" ? (formsFor(value)[0] ?? "") : form };
}

// The abilities a unit's or an item's lists name, whose values its tooltip can show too; fetched when the editor
// opens, and null until they are here.
let listed = $state.raw<SlashSource[] | null>(null);
$effect(() => {
  const ids = listedAbilities(view).slice(0, 24);
  let stale = false;
  Promise.all(ids.map((id) => object("ability", id).catch(() => null))).then((found) => {
    if (stale) return;
    listed = found.flatMap((each) =>
      each ? [sourceOf(each, each.name || each.id, keyNames)] : [],
    );
  });
  return () => {
    stale = true;
  };
});

// What the `/` lists: the object's own values, then the listed abilities'; nothing for a buff.
const sources = $derived(
  view.kind === "buff"
    ? []
    : [...(ownSource && ownSource.keys.length > 0 ? [ownSource] : []), ...(listed ?? [])],
);

function suggest(query: string) {
  return suggestions(sources, query, shownLevel);
}

/**
 * What a placeholder chip says: the value it stands for, named, with its level and value when it is this object's or
 * a listed ability's.
 */
function describe(token: string): { label: string; title: string } {
  const parts = tokenParts(token);
  if (!parts) return { label: token, title: token };
  const own = parts.id === view.id || parts.id === view.baseId;
  const source = own ? undefined : listed?.find((each) => each.id === parts.id);
  // A key without a level (`mindmg1`) reads as a key and a level; it is found by the two together.
  const whole = keyAt(parts).toLowerCase();
  const found = (own ? insertable : (source?.keys ?? [])).find(
    (each) => each.key.toLowerCase() === (each.whole ? whole : parts.key.toLowerCase()),
  );
  const named = found?.name ?? keyNames[parts.key] ?? keyNames[keyAt(parts)] ?? parts.key;
  const value = own ? values[keyAt(parts)] : source?.values[keyAt(parts)];
  const label = [
    own ? "" : `${source?.name ?? parts.id}: `,
    named,
    parts.level > 0 && !found?.whole ? ` ${parts.level}` : "",
    value !== undefined
      ? ` = ${placeholderNumber(value, parts.form)}`
      : parts.form === "%"
        ? " ×100"
        : "",
  ].join("");
  return { label, title: t("tooltipEditor.chipTitle", { token, name: named }) };
}

function insert(key: { key: string; whole?: boolean }): void {
  inserting = false;
  const editor = active ? editors[active] : undefined;
  const level = key.whole ? 0 : shownLevel;
  editor?.insert(tokenOf({ id: view.id, key: key.key, level, form: insertedAt(key).form }));
}

const forms: { form: NumberForm | "auto"; label: string; title: string }[] = [
  { form: "auto", label: t("tooltipEditor.form.auto"), title: t("tooltipEditor.form.autoTitle") },
  {
    form: ".",
    label: t("tooltipEditor.form.decimal"),
    title: t("tooltipEditor.form.decimalTitle"),
  },
  {
    form: "%",
    label: t("tooltipEditor.form.percent"),
    title: t("tooltipEditor.form.percentTitle"),
  },
];

// A buff's extended tooltip with a comma, which the game shows only up to it: a custom buff's always, its own text or
// one it inherits (probe, 2026-10-29: a custom Flame Strike showed "This unit is in a Flame Strike"). The game's own
// buff shows its standard text whole, so a standard buff warns only once the map changes it (not measured).
const buffComma = $derived(
  view.kind === "buff" &&
    view.fields.some((field) => {
      const text = drafts[keyOf(field)] ?? field.value;
      return (
        field.id === "fube" && text.includes(",") && (view.baseId !== null || text !== field.base)
      );
    }),
);

// How many times the commas were swapped: the boxes are made again with the swapped text.
let swaps = $state(0);

// Swaps the extended tooltip's commas for U+201A, which the game draws as a comma and does not cut the text at
// (probe, 2026-10-29; Sven, 2026-10-29).
function swapCommas(): void {
  for (const field of view.fields) {
    if (field.id !== "fube") continue;
    const key = keyOf(field);
    drafts[key] = (drafts[key] ?? field.value).replaceAll(",", "‚");
  }
  swaps++;
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
  <!-- Hovered or edited, a text names its field at the bottom, where it covers nothing (Sven, 2026-10-29). -->
  <div
    class="-mx-1 rounded px-1 outline-1 outline-transparent outline-dashed hover:outline-white/30 focus-within:outline-accent"
    role="group"
    onmouseenter={() => (hovered = key)}
    onmouseleave={() => (hovered = null)}
    onfocusin={() => (focused = key)}
    onfocusout={() => (focused = null)}
  >
    <!-- Made again once the listed abilities are here, so the chips that name them say what they are. -->
    {#key `${key}:${listed !== null}:${swaps}`}
      <RichText
        bind:this={editors[key]}
        markup={drafts[key] ?? part.field.value}
        single={part.single}
        {describe}
        label={part.field.label}
        limit={part.field.maxLength}
        suggest={sources.length > 0 ? suggest : undefined}
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
        oncontextmenu={(event) => {
          event.preventDefault();
          picking = null;
          colourMenu = { x: event.clientX, y: event.clientY, index };
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

  {#if colourMenu}
    {@const at = colourMenu.index}
    <ContextMenu
      x={colourMenu.x}
      y={colourMenu.y}
      items={[
        {
          label: t("tooltipEditor.removeColour"),
          danger: true,
          run: () => setOwn(own.filter((_, index) => index !== at)),
        },
      ]}
      onclose={() => (colourMenu = null)}
    />
  {/if}

  {#if inserting && insertButton}
    <Popover anchor={insertButton} onclose={() => (inserting = false)}>
      <div class="flex flex-col" role="menu" aria-label={t("tooltipEditor.insert")}>
        <p class="m-0 px-2 pt-1 pb-1.5 text-[12px] text-faint">
          {view.kind === "ability"
            ? t("tooltipEditor.insertNote", { level: shownLevel })
            : t("tooltipEditor.insertNoteWhole")}
        </p>
        {#each insertable as each (each.key)}
          {@const at = insertedAt(each)}
          <button
            type="button"
            role="menuitem"
            class="flex items-center gap-3 rounded px-2 py-1 text-left hover:bg-hover hover:text-fg"
            onpointerdown={(event) => event.preventDefault()}
            onclick={() => insert(each)}
          >
            <span class="flex-1">{keyNames[each.key] ?? each.name}</span>
            <span class="font-mono text-[11.5px] text-faint"
              >{each.key}{each.whole ? "" : shownLevel}{at.form ? `,${at.form}` : ""}</span
            >
            {#if at.value !== undefined}
              <span class="font-mono text-[11.5px] text-muted">{placeholderNumber(at.value, at.form)}</span>
            {/if}
          </button>
        {/each}
        <div
          class="mt-1 flex items-center gap-1 border-t border-line px-2 pt-1.5 pb-0.5 text-[12.5px]"
          role="radiogroup"
          aria-label={t("tooltipEditor.form.label")}
        >
          <span class="mr-1 text-muted">{t("tooltipEditor.form.label")}</span>
          {#each forms as each (each.form)}
            <button
              type="button"
              role="radio"
              aria-checked={form === each.form}
              title={each.title}
              class="rounded border px-2 py-0.5 {form === each.form
                ? 'border-accent text-fg'
                : 'border-line text-muted hover:text-fg'}"
              onpointerdown={(event) => event.preventDefault()}
              onclick={() => (form = each.form)}
            >
              {each.label}
            </button>
          {/each}
        </div>
      </div>
    </Popover>
  {/if}

  {#each shownBoxes as box (box.key)}
    {#if box.key !== "main"}
      <div class="mt-3 mb-1 text-[12px] tracking-[.06em] text-faint uppercase">{boxNames[box.key]}</div>
    {/if}
    <div class="rounded-md border border-line bg-black/85 px-3 py-2.5 font-tooltip text-[13px] leading-snug text-white shadow-lg">
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

  {#if buffComma}
    <p class="mt-3 mb-0 text-[12.5px] text-warn">
      {t("tooltipEditor.buffComma")}
      <button
        type="button"
        class="ml-1 rounded-[5px] border border-line px-2 py-0.5 text-body hover:border-muted"
        title={t("tooltipEditor.swapCommasTitle")}
        onclick={swapCommas}
      >
        {t("tooltipEditor.swapCommas")}
      </button>
    </p>
  {/if}

  {#if error}
    <p class="mt-3 mb-0 text-[12.5px] text-warn" role="alert">{error}</p>
  {/if}

  {#snippet actions()}
    <!-- The text hovered or edited is named here, over the count of changed texts, so the colours sit right above the
         tooltip (Sven, 2026-10-29). -->
    <div class="mr-auto flex min-w-0 flex-col self-center text-[12.5px] text-muted">
      <span class="h-[1.45em] truncate">
        {#if named}
          {named.field.label} · <span class="font-mono">{named.field.id}</span>{#if named.field.level > 0}
            · {t("tooltipEditor.levelTag", { level: named.field.level })}{/if}
          {#if named.field.maxLength}
            {@const length = (drafts[keyOf(named.field)] ?? named.field.value).length}
            · <span class="font-mono {length >= named.field.maxLength ? 'text-warn' : ''}"
              >{t("tooltipEditor.length", { length, max: named.field.maxLength })}</span
            >
          {/if}
        {/if}
      </span>
      <span class="text-faint">
        {changedCount === 0 ? t("tooltipEditor.unchanged") : tCount("tooltipEditor.changed", changedCount)}
      </span>
    </div>
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
