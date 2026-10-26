/**
 * What a key does in the table's grid while no cell is being edited, decided with no screen: move or grow the
 * selection, edit the focused cell, or one of the bulk changes. Keys the grid does not take (undo, save, search, the
 * window's Alt ← and Alt →, and Tab, so the keyboard can leave the grid) give null and go on to the window.
 */

/** The parts of a key press the grid looks at (a `KeyboardEvent` has them all). */
export type KeyPress = {
  key: string;
  ctrlKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
  metaKey: boolean;
};

/**
 * What the grid does: move the focus by rows and columns (an infinite move goes to the table's edge; `extend` keeps
 * the anchor, so the block grows), select every cell, edit the focused cell (`text` is what was typed to start it,
 * "" to start empty, null to start from the value), or copy, paste, fill down or reset the selection.
 */
export type GridAction =
  | { type: "move"; rows: number; cols: number; extend: boolean }
  | { type: "selectAll" }
  | { type: "edit"; text: string | null }
  | { type: "copy" }
  | { type: "paste" }
  | { type: "fill" }
  | { type: "reset" }
  | null;

const edge = Number.POSITIVE_INFINITY;

const arrows: Record<string, [number, number]> = {
  ArrowUp: [-1, 0],
  ArrowDown: [1, 0],
  ArrowLeft: [0, -1],
  ArrowRight: [0, 1],
};

/**
 * The grid's action for a key press; `page` is how many rows a Page Up or Page Down moves. Ctrl with Alt is AltGr
 * (it types `@`, `$`, `{` on many keyboards), so a character typed with it starts an edit like any other.
 */
export function gridAction(press: KeyPress, page: number): GridAction {
  const { key, shiftKey: shift, altKey: alt, metaKey: meta } = press;
  const ctrl = press.ctrlKey && !alt;
  const plain = !press.ctrlKey && !alt && !meta;

  const arrow = arrows[key];
  if (arrow && !alt && !meta) {
    const [rows, cols] = arrow;
    // 0 times infinity is not a number, so only the arrow's own direction goes to the edge.
    const far = (step: number) => (step === 0 ? 0 : step * edge);
    return ctrl
      ? { type: "move", rows: far(rows), cols: far(cols), extend: shift }
      : { type: "move", rows, cols, extend: shift };
  }
  if ((key === "Home" || key === "End") && !alt && !meta) {
    const to = key === "Home" ? -edge : edge;
    return { type: "move", rows: ctrl ? to : 0, cols: to, extend: shift };
  }
  if ((key === "PageDown" || key === "PageUp") && plain) {
    return { type: "move", rows: key === "PageDown" ? page : -page, cols: 0, extend: shift };
  }

  if (ctrl && !shift && !meta) {
    switch (key.toLowerCase()) {
      case "a":
        return { type: "selectAll" };
      case "c":
        return { type: "copy" };
      case "v":
        return { type: "paste" };
      case "d":
        return { type: "fill" };
      default:
        return null;
    }
  }

  if (!plain) {
    // AltGr: a character, never a shortcut.
    const altGr = press.ctrlKey && alt && !meta;
    return altGr && key.length === 1 ? { type: "edit", text: key } : null;
  }
  if (key === "Delete" && !shift) return { type: "reset" };
  if ((key === "Enter" && !shift) || key === "F2") return { type: "edit", text: null };
  if (key === "Backspace" && !shift) return { type: "edit", text: "" };
  if (key.length === 1) return { type: "edit", text: key };
  return null;
}

/** What a key does in the cell's editor: send the value and move (rows and columns), or drop what was typed. */
export type EditorAction =
  | { type: "commit"; rows: number; cols: number }
  | { type: "cancel" }
  | null;

/**
 * The cell editor's action for a key press: Enter sends the value and moves down, Tab right (Shift Tab left), Esc drops
 * what was typed. Any other key (Shift Enter starts a new line in a long text) goes to the box.
 */
export function editorAction(press: KeyPress): EditorAction {
  const plain = !press.ctrlKey && !press.altKey && !press.metaKey;
  if (!plain) return null;
  if (press.key === "Escape") return { type: "cancel" };
  if (press.key === "Enter" && !press.shiftKey) return { type: "commit", rows: 1, cols: 0 };
  if (press.key === "Tab") return { type: "commit", rows: 0, cols: press.shiftKey ? -1 : 1 };
  return null;
}
