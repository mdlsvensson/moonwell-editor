<script lang="ts">
/**
 * The editor of one field at one level, by its type (see `editorFor`): a number or text box (Enter or leaving it
 * sends the value, Esc puts the value back), a toggle, a select, the flags' checkboxes, or an object list's chips
 * and picker. A value the app refuses keeps what was typed, with the reason in `error`. The table's cell editor starts
 * it with what was typed into the cell (`initial`), and sends or drops the typed value itself (`commit`, `cancel`).
 */
import { onDestroy, untrack } from "svelte";
import type { ChoiceList, FieldView, Kind } from "./api.ts";
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
// background and border of its own, so it shows as a box on the row (Sven, 2026-10-27).
const tone = $derived(
  `${error || field.warning ? "border-warn bg-warn-lo" : `${compact ? "border-transparent" : "border-line"} bg-raised hover:border-muted`} ${
    field.changed ? "text-changed" : "text-fg"
  }`,
);
const boxClass = $derived(
  `min-w-0 rounded border py-[3px] font-mono text-[12.5px] outline-none focus:border-accent ${
    compact ? "w-full" : "field-sizing-content min-w-[8ch] max-w-full"
  } ${tone}`,
);
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
  const typed = draft;
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

// A toggle the app refused shows the field's value again.
async function choose(element: HTMLInputElement, value: string): Promise<void> {
  if (await send(value)) return;
  element.checked = field.value === "1";
}

/** Drops what was typed (and its error): the box shows the value again. */
export function cancel(): void {
  setDraft(null);
  refused = null;
  error = "";
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "Enter" && !(multiline && event.shiftKey)) {
    event.preventDefault();
    commit();
  } else if (event.key === "Escape" && (draft !== null || error)) {
    event.preventDefault();
    event.stopPropagation();
    cancel();
  }
}

function oninput(event: Event): void {
  setDraft((event.currentTarget as HTMLInputElement | HTMLTextAreaElement).value);
  refused = null;
}
</script>

<span class="block min-w-0 {compact ? '' : 'max-w-full'}" {title}>
  {#if editor === "toggle"}
    <!-- No line height of its own: in the mono font's line the text sat a pixel low against the box (Sven,
         2026-10-27). -->
    <label class="inline-flex cursor-pointer items-center gap-2 px-1.5 py-[3px] font-mono text-[12.5px] leading-none {field.changed
        ? 'text-changed'
        : 'text-fg'}">
      <input
        type="checkbox"
        class="accent-accent"
        checked={field.value === "1"}
        data-field="{field.id}:{field.level}"
        onchange={(event) => choose(event.currentTarget, event.currentTarget.checked ? "1" : "0")}
      />
      {shown}
    </label>
  {:else if editor === "select" && list}
    <Select
      {options}
      value={entryOf(list, field.value)?.value ?? field.value}
      missing={field.value === "" ? emptyText(field, choices) : field.value}
      label={field.label}
      {tone}
      {compact}
      field="{field.id}:{field.level}"
      onpick={(value) => send(value)}
    />
  {:else if editor === "flags" && list}
    <FlagsEditor {field} {list} {shown} {compact} onset={send} />
  {:else if editor === "ids"}
    <IdsEditor {field} {lookup} {compact} onset={send} {onopen} />
  {:else if editor === "number"}
    <input
      type="number"
      class="{boxClass} px-1.5"
      style:width="calc({numberWidth}ch + 30px)"
      step={field.type === "int" ? 1 : "any"}
      value={draft ?? boxValue}
      placeholder={emptyText(field, choices)}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      aria-invalid={!!error}
      {oninput}
      {onkeydown}
      onchange={() => {
        clearTimeout(stepping);
        stepping = setTimeout(commit, 400);
      }}
      onwheel={(event) => {
        // The wheel scrolls the panel: it never steps a box that has the cursor.
        if (document.activeElement === event.currentTarget) event.currentTarget.blur();
      }}
      onblur={() => {
        clearTimeout(stepping);
        commit();
      }}
    />
  {:else if multiline}
    <textarea
      class="{boxClass} block min-h-[4.5em] w-full resize-y px-1.5"
      rows="3"
      value={draft ?? field.value}
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
      placeholder={emptyText(field, choices)}
      size={compact
        ? Math.max(
            4,
            Math.min(24, Math.max((draft ?? boxValue).length, emptyText(field, choices).length) + 1),
          )
        : undefined}
      spellcheck={editor === "text"}
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
