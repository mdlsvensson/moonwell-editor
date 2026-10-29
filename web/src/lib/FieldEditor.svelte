<script lang="ts">
/**
 * The editor of one field at one level, by its type (see `editorFor`): a number or text box (Enter or leaving it
 * sends the value, Esc puts the value back), True or False (a click turns it), a select, the flags' checkboxes, or an object list's chips
 * and picker. A value the app refuses keeps what was typed, with the reason in `error`. The table's cell editor starts
 * it with what was typed into the cell (`initial`), and sends or drops the typed value itself (`commit`, `cancel`).
 */
import ChevronDown from "@lucide/svelte/icons/chevron-down";
import ChevronUp from "@lucide/svelte/icons/chevron-up";
import { onDestroy, untrack } from "svelte";
import type { ChoiceList, FieldView, Kind } from "./api.ts";
import { calculate, isArithmetic } from "./calc.ts";
import FlagsEditor from "./FlagsEditor.svelte";
import {
  assetText,
  emptyText,
  entryOf,
  type Lookup,
  listFor,
  namesOf,
  realText,
  shownValue,
} from "./fields.ts";
import IdsEditor from "./IdsEditor.svelte";
import Select, { type SelectOption } from "./Select.svelte";
import { t } from "./text.ts";
import { typing } from "./typing.ts";
import { checkValue, editorFor, isExtendedTooltip } from "./values.ts";

type Props = {
  field: FieldView;
  choices: Record<string, ChoiceList> | null;
  lookup: Lookup;
  /** True in a level grid's cell: a narrower box. */
  compact?: boolean;
  /** Sends a value; answers with why it was refused, or null when it was taken. */
  onset: (field: FieldView, value: string) => Promise<string | null>;
  onopen: (kind: Kind, id: string) => void;
  /** Why the last value sent or typed was refused; "" (or undefined) when it was not. */
  error?: string;
  /** Text the box starts with as typed (a number or text box only); null or undefined starts from the value. */
  initial?: string | null;
  /** True when the field is the one selected: its boxes show their border, not only on hover. */
  selected?: boolean;
  /** True when the field ids are shown (Ctrl D): an object list's chips show their objects' ids. */
  showIds?: boolean;
};

let {
  field,
  choices,
  lookup,
  compact = false,
  onset,
  onopen,
  error = $bindable(),
  initial = null,
  selected = false,
  showIds = false,
}: Props = $props();

// What the user typed and has not sent yet (or that was refused); null shows the field's value.
let draft = $state<string | null>(null);
// The typed value on its way, and the last one the app refused: neither is sent again when the box is left.
let sending: string | null = null;
let refused: string | null = null;
// The answer to the typed value on its way: whether the app took it.
let pending: Promise<boolean> | null = null;

// A value changed elsewhere (↺, undo, redo) replaces what was typed and its error. Only this field's value counts:
// every answer brings new field objects, and an edit of another field must not wipe what is typed or refused here.
// The first run starts the box with `initial` instead.
const value = $derived(field.value);
let started = false;
$effect(() => {
  value;
  untrack(() => {
    if (!started) {
      started = true;
      if (initial !== null) {
        setDraft(initial);
        return;
      }
    }
    if (sending === null) cancel();
  });
});

// While the box holds a typed value, a save (or opening another map, reloading, closing) sends it first. It is
// registered as the user types, not in an effect, so a click right after typing already finds it.
let stopTyping: (() => void) | null = null;

function setDraft(text: string | null): void {
  draft = text;
  if (text !== null && !stopTyping) stopTyping = typing(() => commit());
  if (text === null && stopTyping) {
    stopTyping();
    stopTyping = null;
  }
}

onDestroy(() => stopTyping?.());

const nameOf = namesOf((listOf, id) => lookup(listOf, id));
const editor = $derived(editorFor(field, choices));
const list = $derived(listFor(field, choices));
const shown = $derived(shownValue(field, choices, nameOf));
// An extended tooltip gets a box of several lines, as does any text that has more than one (Sven, 2026-10-27).
const multiline = $derived(isExtendedTooltip(field) || field.value.includes("\n"));
// A list's choices for the dropdown.
const options = $derived.by((): SelectOption[] =>
  (list?.entries ?? []).map((entry) => ({
    value: entry.value,
    label: entry.label,
    group: entry.group ?? undefined,
  })),
);
// A number box's arrows send the value once they stop.
let stepping: ReturnType<typeof setTimeout> | undefined;
let numberBox: HTMLInputElement | undefined = $state();
// A real shows with three decimals, and a file by the names of the objects that use it while the box is not being
// edited (its path while it is), as World Editor shows them (Sven, 2026-10-27).
let focused = $state(false);
const named = $derived(assetText(field));
const boxValue = $derived(
  field.type === "float" ? realText(field.value) : !focused && named ? named : field.value,
);
const title = $derived(
  field.changed
    ? t("field.standard", { value: shownValue({ ...field, value: field.base }, choices, nameOf) })
    : undefined,
);
// Values start at the left of their box, and a box in the details panel is as wide as its value (between 8 characters
// and the column), as World Editor shows them (Sven, 2026-10-27); a level grid's cell fills its column. The box has a
// background and border of its own, so it shows as a box on the row (Sven, 2026-10-27). Every box (a number, a text,
// True or False, a dropdown) shows only its value, as the tooltip editor's texts do: a dashed border on hover, the
// accent's while it has the cursor, and its placeholder when it is empty (Sven, 2026-10-28); a dropdown keeps its
// arrow (Sven, 2026-10-29). The selected field's boxes show a solid border, as they do with the cursor in them (Sven,
// 2026-10-29).
const quiet = $derived(
  `${error || field.warning ? "border-warn bg-warn-lo" : selected ? "border-muted bg-transparent" : "border-transparent bg-transparent hover:border-dashed hover:border-muted"} ${
    field.changed ? "text-changed" : "text-fg"
  }`,
);
// True and False, both kept in the box so it is as wide for either (Sven, 2026-10-29).
const toggleWords = $derived([
  shownValue({ ...field, value: "1" }, choices, nameOf),
  shownValue({ ...field, value: "0" }, choices, nameOf),
]);
const boxClass = $derived(
  `min-w-0 rounded border py-[3px] font-mono text-[12.5px] outline-none placeholder:text-faint focus:border-solid focus:border-accent ${
    compact ? "w-full" : "field-sizing-content min-w-[8ch] max-w-full"
  } ${quiet}`,
);
// Every box shows something when it is empty: World Editor's empty value (0, 0.000, None), else "(empty)".
const placeholder = $derived(emptyText(field, choices) || t("field.empty"));
// A number box is as wide as its value or placeholder, with room for its arrows.
const numberWidth = $derived(
  Math.max(3, (draft ?? boxValue).length, emptyText(field, choices).length),
);

async function send(value: string): Promise<boolean> {
  const problem = await onset(field, value);
  error = problem ?? "";
  return problem === null;
}

/**
 * Sends what was typed, when it differs from the value; a value refused (here or by the app) stays in the box. True
 * when nothing typed is left to send (it was taken, matched the value, or nothing was typed); false when it was
 * refused, or more was typed while it was on its way. Sending the value already on its way waits for its answer instead of sending it again.
 */
export async function commit(): Promise<boolean> {
  if (draft === null) return true;
  if (draft === refused) return false;
  if (draft === sending && pending) return pending;
  let typed = draft;
  // A number box works out arithmetic typed into it (Sven, 2026-10-29): `10*2+5`, or `*2` on the value it holds.
  if (editor === "number" && isArithmetic(typed)) {
    const result = calculate(typed, Number(field.value) || 0);
    if (result === null) {
      error = t("field.notArithmetic", { text: typed });
      return false;
    }
    typed = String(field.type === "int" ? Math.round(result) : Math.round(result * 1e6) / 1e6);
    setDraft(typed);
  }
  const checked = checkValue(field, typed);
  if (!checked.ok) {
    error = checked.problem;
    return false;
  }
  // A real typed as World Editor shows it (0.150) is the value it already holds (0.15).
  if (
    checked.value === field.value ||
    (field.type === "float" &&
      field.value.trim() !== "" &&
      Number(checked.value) === Number(field.value))
  ) {
    setDraft(null);
    error = "";
    return true;
  }
  sending = typed;
  pending = send(checked.value);
  const taken = await pending;
  pending = null;
  sending = null;
  if (!taken) refused = typed;
  else if (draft === typed) setDraft(null);
  // Typed on while it was on its way: that is still to send, so the box is not done.
  return taken && draft === null;
}

/** Drops what was typed (and its error): the box shows the value again. */
export function cancel(): void {
  setDraft(null);
  refused = null;
  error = "";
}

function onkeydown(event: KeyboardEvent): void {
  if (editor === "number" && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
    event.preventDefault();
    step(event.key === "ArrowUp" ? 1 : -1);
  } else if (event.key === "Enter" && !(multiline && event.shiftKey)) {
    event.preventDefault();
    commit();
  } else if (event.key === "Escape" && (draft !== null || error)) {
    event.preventDefault();
    event.stopPropagation();
    cancel();
  }
}

// The number box's arrows (Sven, 2026-10-29): a step of 1 up or down, sent once they stop, as the browser's own did.
function step(by: number): void {
  if (!numberBox) return;
  const now = numberBox.value === "" ? 0 : Number(numberBox.value);
  if (!Number.isFinite(now)) return;
  const next = String(Math.round((now + by) * 1e6) / 1e6);
  numberBox.value = next;
  setDraft(next);
  refused = null;
  clearTimeout(stepping);
  stepping = setTimeout(commit, 400);
}

function oninput(event: Event): void {
  setDraft((event.currentTarget as HTMLInputElement | HTMLTextAreaElement).value);
  refused = null;
}
</script>

<span class="block min-w-0 {compact ? '' : 'max-w-full'}" {title}>
  {#if editor === "toggle"}
    <!-- The word alone, with no checkbox and no box; a click turns it (Sven, 2026-10-29). -->
    <button
      type="button"
      class="inline-grid cursor-pointer rounded border border-transparent px-1.5 py-[3px] text-left font-mono text-[12.5px] outline-none focus-visible:border-accent {error ||
      field.warning
        ? 'text-warn'
        : field.changed
          ? 'text-changed'
          : 'text-fg'}"
      aria-pressed={field.value === "1"}
      aria-label={field.label}
      data-field="{field.id}:{field.level}"
      onclick={() => send(field.value === "1" ? "0" : "1")}
    >
      {#each toggleWords as word (word)}
        <span class="invisible col-start-1 row-start-1" aria-hidden="true">{word}</span>
      {/each}
      <span class="col-start-1 row-start-1">{shown}</span>
    </button>
  {:else if editor === "select" && list}
    <Select
      {options}
      value={entryOf(list, field.value)?.value ?? field.value}
      missing={field.value === "" ? emptyText(field, choices) : field.value}
      label={field.label}
      tone={quiet}
      {compact}
      snug
      field="{field.id}:{field.level}"
      onpick={(value) => send(value)}
    />
  {:else if editor === "flags" && list}
    <FlagsEditor {field} {list} {shown} {compact} tone={quiet} onset={send} />
  {:else if editor === "ids"}
    <IdsEditor {field} {lookup} {compact} {showIds} onset={send} {onopen} />
  {:else if editor === "number"}
    <span class="group/number relative inline-flex max-w-full align-top {compact ? 'w-full' : ''}">
    <!-- A text box, so arithmetic can be typed (Sven, 2026-10-29); its arrows and the arrow keys step it. -->
    <input
      inputmode="decimal"
      spellcheck="false"
      class="{boxClass} pr-4 pl-1.5"
      style:width="calc({numberWidth}ch + 30px)"
      bind:this={numberBox}
      value={draft ?? boxValue}
      {placeholder}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      aria-invalid={!!error}
      {oninput}
      {onkeydown}
      onblur={() => {
        clearTimeout(stepping);
        commit();
      }}
    />
    <!-- The box's arrows, shown while it is hovered or has the cursor; a click keeps the cursor where it was. -->
    <span
      class="absolute inset-y-px right-px flex w-3.5 flex-col opacity-0 group-hover/number:opacity-100 group-focus-within/number:opacity-100"
    >
      {#each [{ by: 1, Icon: ChevronUp, name: t("field.stepUp") }, { by: -1, Icon: ChevronDown, name: t("field.stepDown") }] as arrow (arrow.by)}
        <button
          type="button"
          tabindex="-1"
          class="flex flex-1 cursor-pointer items-center justify-center text-faint hover:text-fg"
          aria-label={arrow.name}
          onmousedown={(event) => event.preventDefault()}
          onclick={() => step(arrow.by)}
        >
          <arrow.Icon size={11} strokeWidth={2.5} />
        </button>
      {/each}
    </span>
    </span>
  {:else if multiline}
    <textarea
      class="{boxClass} block min-h-[4.5em] w-full resize-none px-1.5"
      rows="3"
      maxlength={field.maxLength ?? undefined}
      value={draft ?? field.value}
      {placeholder}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      title={t("field.multilineTitle")}
      {oninput}
      {onkeydown}
      onblur={commit}
    ></textarea>
  {:else}
    <input
      class="{boxClass} px-1.5"
      value={draft ?? boxValue}
      {placeholder}
      size={compact
        ? Math.max(4, Math.min(24, Math.max((draft ?? boxValue).length, placeholder.length) + 1))
        : undefined}
      spellcheck={editor === "text"}
      maxlength={editor === "text" ? (field.maxLength ?? undefined) : undefined}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      aria-invalid={!!error}
      title={named ? field.value : undefined}
      onfocus={() => (focused = true)}
      {oninput}
      {onkeydown}
      onblur={() => {
        focused = false;
        commit();
      }}
    />
  {/if}
</span>
