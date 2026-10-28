<script lang="ts">
/**
 * One tooltip text in the tooltip editor, edited as it looks (Sven, 2026-10-27), on TipTap (Sven, 2026-10-28):
 * coloured text in its colour, line breaks as lines, and each `<id,Key>` placeholder as a chip that names what it
 * stands for. Enter breaks the line (not in a one-line text), Ctrl Z and Ctrl Y undo and redo in the text, and every
 * change is sent on as markup. Copying gives the game's markup and a paste is read as markup (Agent's call), so
 * colours and placeholders survive both. The editor's toolbar colours the selection and puts placeholders in at the
 * cursor through `color` and `insert`.
 */
import { Editor, Extension, Mark, Node } from "@tiptap/core";
import Document from "@tiptap/extension-document";
import HardBreak from "@tiptap/extension-hard-break";
import Paragraph from "@tiptap/extension-paragraph";
import Text from "@tiptap/extension-text";
import { UndoRedo } from "@tiptap/extensions/undo-redo";
import { onMount, untrack } from "svelte";
import {
  colorMark,
  cssColor,
  type DocNode,
  docOf,
  inlineOf,
  markupColor,
  markupOf,
  newlineOf,
  segmentsOf,
  segmentsOfDoc,
  tokenNode,
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
let editor: Editor | null = null;
// Whether the text has had the cursor: the toolbar puts a placeholder at its end until it has.
let touched = false;
// The text's own way of breaking lines, as it opened, kept when it is written back.
const newline = untrack(() => newlineOf(markup));

// The text is one paragraph; its lines are hard breaks.
const OneParagraph = Document.extend({ content: "paragraph" });

// A colour, as the markup's `aarrggbb`.
const Color = Mark.create({
  name: colorMark,
  addAttributes: () => ({ color: { default: null } }),
  parseHTML: () => [
    { tag: "span[data-color]", getAttrs: (element) => ({ color: element.dataset.color }) },
  ],
  renderHTML: ({ mark }) => [
    "span",
    { "data-color": mark.attrs.color, style: `color: ${cssColor(String(mark.attrs.color))}` },
    0,
  ],
});

// A placeholder, as a chip that cannot be typed in; its colour is the colour mark around it.
const Token = Node.create({
  name: tokenNode,
  group: "inline",
  inline: true,
  atom: true,
  addAttributes: () => ({ token: { default: "" } }),
  parseHTML: () => [
    { tag: "span[data-token]", getAttrs: (element) => ({ token: element.dataset.token }) },
  ],
  renderHTML: ({ node }) => {
    const { label, title } = describe(String(node.attrs.token));
    return [
      "span",
      {
        "data-token": node.attrs.token,
        title,
        contenteditable: "false",
        class:
          "mx-px inline-block cursor-default rounded border border-current/40 bg-white/10 px-1 align-baseline text-[0.85em] leading-tight",
      },
      label,
    ];
  },
  renderText: ({ node }) => String(node.attrs.token),
});

// Enter breaks the line, or does nothing in a one-line text.
const Keys = Extension.create({
  name: "tooltipKeys",
  priority: 1000,
  addKeyboardShortcuts() {
    const brk = () => single || this.editor.commands.setHardBreak();
    return { Enter: brk, "Shift-Enter": brk, "Mod-Enter": brk };
  },
});

onMount(() => {
  if (!root) return;
  editor = new Editor({
    element: root,
    extensions: [OneParagraph, Paragraph, Text, HardBreak, UndoRedo, Color, Token, Keys],
    content: docOf(segmentsOf(markup)),
    editorProps: {
      attributes: {
        class: `min-h-[1.4em] break-words whitespace-pre-wrap outline-none ${extra}`,
        role: "textbox",
        "aria-multiline": String(!single),
        "aria-label": label,
        spellcheck: "false",
      },
      // Copied text is the game's markup.
      clipboardTextSerializer: (slice) =>
        markupOf(segmentsOfDoc(slice.content.toJSON() ?? []), newline),
      // A paste is read as the game's markup; a one-line text takes its lines as one.
      handlePaste: (_, event) => {
        const text = event.clipboardData?.getData("text/plain") ?? "";
        editor?.commands.insertContent(
          inlineOf(segmentsOf(single ? text.replace(/\r?\n/g, " ") : text)),
        );
        return true;
      },
    },
    onUpdate: ({ editor: changed }) =>
      onchange(markupOf(segmentsOfDoc([changed.getJSON() as DocNode]), newline)),
    onFocus: () => {
      touched = true;
      onfocus?.();
    },
  });
  return () => editor?.destroy();
});

/** Colours the selected text (`#rrggbb`), or gives it no colour (null). */
export function color(css: string | null): void {
  if (!editor || !touched || editor.state.selection.empty) return;
  const value = css ? markupColor(css) : null;
  const chain = editor.chain().focus();
  (value ? chain.setMark(colorMark, { color: value }) : chain.unsetMark(colorMark)).run();
}

/** Puts a placeholder in at the cursor (over the selection), or at the end when the text never had the cursor. */
export function insert(token: string): void {
  editor
    ?.chain()
    .focus(touched ? null : "end")
    .insertContent({ type: tokenNode, attrs: { token } })
    .run();
}
</script>

<div bind:this={root} class="contents"></div>
