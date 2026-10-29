<script lang="ts">
/**
 * One tooltip text in the tooltip editor, edited as it looks (Sven, 2026-10-27), on TipTap (Sven, 2026-10-28):
 * coloured text in its colour, line breaks as lines, and each `<id,Key>` placeholder as a chip that names what it
 * stands for. Enter breaks the line (not in a one-line text), Ctrl Z and Ctrl Y undo and redo in the text, and every
 * change is sent on as markup. Copying gives the game's markup and a paste is read as markup (Agent's call), so
 * colours and placeholders survive both. The editor's toolbar colours the selection and puts placeholders in at the
 * cursor through `color` and `insert`. A text takes no more markup than its field's limit. A `/` that starts a word
 * opens a list of the values the text can show (Sven, 2026-10-29); the arrows move in it, Enter or Tab puts the one
 * chosen in, Esc closes it.
 */
import { Editor, Extension, Mark, Node } from "@tiptap/core";
import Document from "@tiptap/extension-document";
import HardBreak from "@tiptap/extension-hard-break";
import Paragraph from "@tiptap/extension-paragraph";
import Text from "@tiptap/extension-text";
import { UndoRedo } from "@tiptap/extensions/undo-redo";
import { Plugin } from "@tiptap/pm/state";
import type { EditorView } from "@tiptap/pm/view";
import { Popover } from "bits-ui";
import { onMount, tick, untrack } from "svelte";
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
import { chipChar, type Suggestion, slashQuery } from "./slash.ts";

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
  /** The most characters of markup the text takes, as World Editor's box does; null for no limit. */
  limit?: number | null;
  /** The values a `/` lists for the words typed after it; absent when there are none to list. */
  suggest?: (query: string) => Suggestion[];
  class?: string;
};

let {
  markup,
  single = false,
  describe,
  onchange,
  onfocus,
  label,
  limit = null,
  suggest,
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

// No change makes the markup longer than `limit` (Sven, 2026-10-29: World Editor's box stops there too); a text
// already longer can still be shortened.
const Limit = Extension.create({
  name: "tooltipLimit",
  addProseMirrorPlugins() {
    const length = (doc: { toJSON(): unknown }) =>
      markupOf(segmentsOfDoc([doc.toJSON() as DocNode]), newline).length;
    return [
      new Plugin({
        filterTransaction: (transaction, state) => {
          if (!limit || !transaction.docChanged) return true;
          const after = length(transaction.doc);
          return after <= limit || after <= length(state.doc);
        },
      }),
    ];
  },
});

// The `/` list: where the `/` and the words after it are, the values listed, the one chosen and the place under the
// `/`. A list closed with Esc stays closed for that `/`.
let slash = $state<{
  from: number;
  to: number;
  items: Suggestion[];
  index: number;
  rect: DOMRect;
} | null>(null);
let dismissed = -1;
let list: HTMLDivElement | undefined = $state();

function follow(view: EditorView): void {
  const { selection } = view.state;
  if (!suggest || !view.hasFocus() || !selection.empty) {
    slash = null;
    return;
  }
  const start = selection.$from;
  const query = slashQuery(start.parent.textBetween(0, start.parentOffset, "\n", chipChar));
  if (query === null) {
    slash = null;
    dismissed = -1;
    return;
  }
  const from = selection.from - query.length - 1;
  const items = from === dismissed ? [] : suggest(query);
  if (items.length === 0) {
    slash = null;
    return;
  }
  const at = view.coordsAtPos(from);
  const index = slash?.from === from ? Math.min(slash.index, items.length - 1) : 0;
  slash = {
    from,
    to: selection.from,
    items,
    index,
    rect: new DOMRect(at.left, at.top, 0, at.bottom - at.top),
  };
}

function pick(index: number): void {
  const open = slash;
  const item = open?.items[index];
  if (!open || !item) return;
  slash = null;
  editor
    ?.chain()
    .focus()
    .insertContentAt(
      { from: open.from, to: open.to },
      { type: tokenNode, attrs: { token: item.token } },
    )
    .run();
}

function choose(step: number): void {
  if (!slash) return;
  slash.index = (slash.index + step + slash.items.length) % slash.items.length;
  tick().then(() =>
    list?.querySelector(`[data-index="${slash?.index}"]`)?.scrollIntoView({ block: "nearest" }),
  );
}

// Before the line keys, so Enter puts the value in while the list is open.
const Slash = Extension.create({
  name: "tooltipSlash",
  priority: 1100,
  addProseMirrorPlugins() {
    return [
      new Plugin({
        view: () => ({ update: (view) => follow(view) }),
        props: {
          handleKeyDown: (_, event) => {
            if (!slash) return false;
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
              choose(event.key === "ArrowDown" ? 1 : -1);
              return true;
            }
            if (event.key === "Enter" || event.key === "Tab") {
              pick(slash.index);
              return true;
            }
            if (event.key === "Escape") {
              // Spent here: the dialog under the list must not close.
              event.stopPropagation();
              dismissed = slash.from;
              slash = null;
              return true;
            }
            return false;
          },
        },
      }),
    ];
  },
});

onMount(() => {
  if (!root) return;
  editor = new Editor({
    element: root,
    extensions: [
      OneParagraph,
      Paragraph,
      Text,
      HardBreak,
      UndoRedo,
      Color,
      Token,
      Keys,
      Limit,
      Slash,
    ],
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
    onBlur: () => {
      slash = null;
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

{#if slash}
  {@const rect = slash.rect}
  <Popover.Root
    open
    onOpenChange={(open) => {
      if (!open && slash) {
        dismissed = slash.from;
        slash = null;
      }
    }}
  >
    <Popover.Portal>
      <Popover.Content
        customAnchor={{ getBoundingClientRect: () => rect }}
        side="bottom"
        align="start"
        sideOffset={4}
        collisionPadding={8}
        trapFocus={false}
        onOpenAutoFocus={(event) => event.preventDefault()}
        onCloseAutoFocus={(event) => event.preventDefault()}
        data-popup
        class="z-40 max-h-[280px] w-[380px] overflow-auto rounded-lg border border-line bg-surface p-1 text-[13px] text-body shadow-[0_4px_12px_rgba(0,0,0,.3)] outline-none"
      >
        <div bind:this={list} role="listbox" aria-label={label}>
          {#each slash.items as item, index (item.token)}
            <div
              role="option"
              tabindex="-1"
              aria-selected={index === slash.index}
              data-index={index}
              class="flex cursor-pointer items-center gap-2 rounded px-2 py-1 {index === slash.index
                ? 'bg-hover text-fg'
                : ''}"
              onpointerdown={(event) => event.preventDefault()}
              onpointermove={() => slash && (slash.index = index)}
              onclick={() => pick(index)}
              onkeydown={() => {}}
            >
              <span class="min-w-0 flex-1 truncate" title={item.owner ? `${item.owner}: ${item.name}` : item.name}>
                {#if item.owner}<span class="text-muted">{item.owner}: </span>{/if}{item.name}
              </span>
              <span class="shrink-0 font-mono text-[11.5px] text-faint">{item.token}</span>
              {#if item.value !== undefined}
                <span class="shrink-0 font-mono text-[11.5px] text-muted">{item.value}</span>
              {/if}
            </div>
          {/each}
        </div>
      </Popover.Content>
    </Popover.Portal>
  </Popover.Root>
{/if}
