<script lang="ts">
/**
 * One tooltip text in the tooltip editor, edited as it looks (Sven, 2026-10-27): coloured text in its colour, line
 * breaks as lines, and each `<id,Key>` placeholder as a chip that names what it stands for. Enter breaks the line
 * (not in a one-line text), a paste comes in as plain text, and every change is sent on as markup. The editor's
 * toolbar colours the selection and puts placeholders in at the cursor through `color` and `insert`.
 */
import { onMount, untrack } from "svelte";
import {
  cssColor,
  markupColor,
  markupOf,
  newlineOf,
  type Segment,
  segmentsOf,
} from "./richText.ts";

type Props = {
  /** The text as markup, as the field holds it when the editor opens. */
  markup: string;
  /** A one-line text (a basic tooltip): Enter does not break it. */
  single?: boolean;
  /** What a placeholder chip says, and its tooltip. */
  describe: (token: string) => { label: string; title: string };
  /** Every change, as markup. */
  onchange: (markup: string) => void;
  /** The text got the cursor, so the toolbar acts on it. */
  onfocus?: () => void;
  /** The name for screen readers. */
  label: string;
  class?: string;
};

let {
  markup,
  single = false,
  describe,
  onchange,
  onfocus,
  label,
  class: extra = "",
}: Props = $props();

let root: HTMLDivElement | undefined = $state();
// Where the cursor or selection was last in this text, for the toolbar, which takes the focus when clicked.
let saved: Range | null = null;
// The text's own way of breaking lines, as it opened, kept when it is written back.
const newline = untrack(() => newlineOf(markup));

onMount(() => {
  if (!root) return;
  render(root, segmentsOf(markup));
  const remember = () => {
    const selection = document.getSelection();
    if (root && selection?.rangeCount && root.contains(selection.anchorNode)) {
      saved = selection.getRangeAt(0).cloneRange();
    }
  };
  document.addEventListener("selectionchange", remember);
  return () => document.removeEventListener("selectionchange", remember);
});

function chip(token: string, color: string | null): HTMLElement {
  const { label, title } = describe(token);
  const element = document.createElement("span");
  element.contentEditable = "false";
  element.dataset.token = token;
  element.title = title;
  element.textContent = label;
  element.className =
    "mx-px inline-block cursor-default rounded border border-current/40 bg-white/10 px-1 align-baseline text-[0.85em] leading-tight";
  if (color) element.style.color = cssColor(color);
  return element;
}

/** Fills `into` with the pieces: coloured text in spans, breaks as <br>, placeholders as chips. */
function render(into: HTMLElement, segments: Segment[]): void {
  into.replaceChildren();
  for (const segment of segments) {
    if (segment.type === "break") {
      into.append(document.createElement("br"));
    } else if (segment.type === "token") {
      into.append(chip(segment.token, segment.color));
    } else if (segment.color) {
      const span = document.createElement("span");
      span.dataset.color = segment.color;
      span.style.color = cssColor(segment.color);
      span.textContent = segment.text;
      into.append(span);
    } else {
      into.append(document.createTextNode(segment.text));
    }
  }
  // A text that ends in a break needs one more for the browser to show the empty line.
  if (into.lastChild instanceof HTMLBRElement) into.append(document.createElement("br"));
}

/** The colour an element gives the text in it: its own marking, its style, a <font>'s; undefined when none. */
function colorOf(element: HTMLElement): string | null | undefined {
  if (element.dataset.color === "none") return null;
  if (element.dataset.color) return element.dataset.color;
  if (element.style.color) return markupColor(element.style.color) ?? undefined;
  const font = element.getAttribute("color");
  if (element.tagName === "FONT" && font) return markupColor(font) ?? undefined;
  return undefined;
}

/** The pieces the text holds now. */
function read(from: HTMLElement): Segment[] {
  const segments: Segment[] = [];
  const walk = (node: Node, color: string | null) => {
    if (node instanceof Text) {
      // The box keeps white space as typed, so the browser breaks a line in it with a newline as often as with <br>.
      const lines = node.data
        .replace(/\u00a0/g, " ")
        .replace(/\u200b/g, "")
        .split("\n");
      lines.forEach((text, index) => {
        if (index > 0) segments.push({ type: "break" });
        if (text === "") return;
        const last = segments[segments.length - 1];
        if (last?.type === "text" && last.color === color) last.text += text;
        else segments.push({ type: "text", text, color });
      });
      return;
    }
    if (!(node instanceof HTMLElement)) return;
    if (node.dataset.token) {
      segments.push({ type: "token", token: node.dataset.token, color });
      return;
    }
    if (node instanceof HTMLBRElement) {
      segments.push({ type: "break" });
      return;
    }
    // A block the browser made (a pasted paragraph) starts a new line.
    if ((node.tagName === "DIV" || node.tagName === "P") && segments.length > 0) {
      const last = segments[segments.length - 1];
      if (last?.type !== "break") segments.push({ type: "break" });
    }
    const own = colorOf(node);
    for (const child of node.childNodes) walk(child, own === undefined ? color : own);
  };
  for (const child of from.childNodes) walk(child, null);
  // The extra break that shows an empty last line is not part of the text.
  if (from.lastChild instanceof HTMLBRElement && segments[segments.length - 1]?.type === "break")
    segments.pop();
  return segments;
}

function changed(): void {
  if (root) onchange(markupOf(read(root), newline));
}

/** Puts the cursor back where it was in this text, for the toolbar; false when it never had it. */
function restore(): Range | null {
  if (!root || !saved) return null;
  root.focus();
  const selection = document.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(saved);
  return saved;
}

/**
 * Colours the selected text (`#rrggbb`), or gives it no colour (null): it goes into a span of its own, inside which
 * any colour it had is dropped.
 */
export function color(css: string | null): void {
  const range = restore();
  if (!range || range.collapsed) return;
  const content = range.extractContents();
  for (const element of [
    ...content.querySelectorAll<HTMLElement>("[data-color], font, span[style]"),
  ]) {
    if (element.dataset.token) continue;
    element.replaceWith(...element.childNodes);
  }
  const wrap = document.createElement("span");
  const value = css ? markupColor(css) : null;
  wrap.dataset.color = value ?? "none";
  if (value) wrap.style.color = cssColor(value);
  wrap.append(content);
  for (const token of wrap.querySelectorAll<HTMLElement>("[data-token]")) {
    token.style.color = value ? cssColor(value) : "";
  }
  range.insertNode(wrap);
  const selection = document.getSelection();
  selection?.removeAllRanges();
  const after = document.createRange();
  after.selectNodeContents(wrap);
  selection?.addRange(after);
  changed();
}

/** Puts a placeholder in at the cursor (over the selection). */
export function insert(token: string): void {
  const range = restore() ?? (root ? endOf(root) : null);
  if (!range || !root) return;
  range.deleteContents();
  const element = chip(token, null);
  range.insertNode(element);
  const after = document.createRange();
  after.setStartAfter(element);
  after.collapse(true);
  const selection = document.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(after);
  saved = after.cloneRange();
  changed();
}

function endOf(element: HTMLElement): Range {
  const range = document.createRange();
  range.selectNodeContents(element);
  range.collapse(false);
  return range;
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key !== "Enter") return;
  event.preventDefault();
  if (!single) document.execCommand("insertLineBreak");
}

function onpaste(event: ClipboardEvent): void {
  event.preventDefault();
  const text = event.clipboardData?.getData("text/plain") ?? "";
  const lines = single ? [text.replace(/\r?\n/g, " ")] : text.split(/\r?\n/);
  lines.forEach((line, index) => {
    if (index > 0) document.execCommand("insertLineBreak");
    if (line) document.execCommand("insertText", false, line);
  });
}
</script>

<div
  bind:this={root}
  class="min-h-[1.4em] break-words whitespace-pre-wrap outline-none {extra}"
  contenteditable="true"
  role="textbox"
  tabindex="0"
  aria-multiline={!single}
  aria-label={label}
  spellcheck="false"
  oninput={changed}
  {onkeydown}
  {onpaste}
  onfocus={() => onfocus?.()}
></div>
