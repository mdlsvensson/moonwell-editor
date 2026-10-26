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
import { type Lookup, shownValue } from "./fields.ts";
import IdsEditor from "./IdsEditor.svelte";
import { typing } from "./typing.ts";
import { checkValue, editorFor } from "./values.ts";

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

const nameOf = (listOf: string, id: string) => lookup(listOf, id)?.name;
const editor = $derived(editorFor(field, choices));
const list = $derived(field.choice ? choices?.[field.choice] : undefined);
const shown = $derived(shownValue(field, choices, nameOf));
const multiline = $derived(field.value.includes("\n"));
const title = $derived(
  field.changed
    ? `Standard: ${shownValue({ ...field, value: field.base }, choices, nameOf)}`
    : undefined,
);
const boxClass = $derived(
  `w-full min-w-0 rounded border px-1.5 py-[3px] font-mono text-[12.5px] outline-none hover:border-line focus:border-accent ${
    error || field.warning ? "border-warn bg-warn-lo" : "border-transparent bg-bg"
  } ${field.changed ? "text-changed" : "text-fg"} ${editor === "number" ? "text-right" : ""}`,
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
  if (checked.value === field.value) {
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

// A toggle or a select the app refused shows the field's value again.
async function choose(element: HTMLInputElement | HTMLSelectElement, value: string): Promise<void> {
  if (await send(value)) return;
  if (element instanceof HTMLInputElement) element.checked = field.value === "1";
  else element.value = field.value;
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

<span class="block min-w-0" {title}>
  {#if editor === "toggle"}
    <label class="inline-flex cursor-pointer items-center gap-2 px-1.5 py-[3px] font-mono text-[12.5px] {field.changed
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
    <select
      class="{boxClass} cursor-pointer"
      value={list.entries.find((entry) => entry.value.toLowerCase() === field.value.toLowerCase())
        ?.value ?? field.value}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      onchange={(event) => choose(event.currentTarget, event.currentTarget.value)}
    >
      {#if !list.entries.some((entry) => entry.value.toLowerCase() === field.value.toLowerCase())}
        <option value={field.value}>{field.value === "" ? "(none)" : field.value}</option>
      {/if}
      {#each list.entries as entry (entry.value)}
        <option value={entry.value}>{entry.label}</option>
      {/each}
    </select>
  {:else if editor === "flags" && list}
    <FlagsEditor {field} {list} {shown} {compact} onset={send} />
  {:else if editor === "ids"}
    <IdsEditor {field} {lookup} {compact} onset={send} {onopen} />
  {:else if multiline}
    <textarea
      class="{boxClass} block resize-y"
      rows="3"
      value={draft ?? field.value}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      title="Enter sends it; Shift Enter starts a new line"
      {oninput}
      {onkeydown}
      onblur={commit}
    ></textarea>
  {:else}
    <input
      class={boxClass}
      value={draft ?? field.value}
      size={compact ? Math.max(4, Math.min(24, (draft ?? field.value).length + 1)) : undefined}
      spellcheck={editor === "text"}
      inputmode={editor === "number" ? "decimal" : undefined}
      data-field="{field.id}:{field.level}"
      aria-label={field.label}
      aria-invalid={!!error}
      {oninput}
      {onkeydown}
      onblur={commit}
    />
  {/if}
</span>
