import { describe, expect, it } from "vitest";
import { editorAction, type GridAction, gridAction, type KeyPress } from "./gridKeys.ts";

function press(key: string, over: Partial<KeyPress> = {}): KeyPress {
  return { key, ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, ...over };
}

const page = 20;
const act = (key: string, over: Partial<KeyPress> = {}): GridAction =>
  gridAction(press(key, over), page);

describe("gridAction", () => {
  it("moves with the arrows, and Shift makes the block bigger", () => {
    expect(act("ArrowDown")).toEqual({ type: "move", rows: 1, cols: 0, extend: false });
    expect(act("ArrowUp")).toEqual({ type: "move", rows: -1, cols: 0, extend: false });
    expect(act("ArrowRight", { shiftKey: true })).toEqual({
      type: "move",
      rows: 0,
      cols: 1,
      extend: true,
    });
    expect(act("ArrowLeft")).toEqual({ type: "move", rows: 0, cols: -1, extend: false });
  });

  it("goes to the table's edge with Ctrl and an arrow, Home and End", () => {
    expect(act("ArrowDown", { ctrlKey: true })).toEqual({
      type: "move",
      rows: Number.POSITIVE_INFINITY,
      cols: 0,
      extend: false,
    });
    expect(act("ArrowLeft", { ctrlKey: true, shiftKey: true })).toEqual({
      type: "move",
      rows: 0,
      cols: Number.NEGATIVE_INFINITY,
      extend: true,
    });
    expect(act("Home")).toEqual({
      type: "move",
      rows: 0,
      cols: Number.NEGATIVE_INFINITY,
      extend: false,
    });
    expect(act("End", { ctrlKey: true })).toEqual({
      type: "move",
      rows: Number.POSITIVE_INFINITY,
      cols: Number.POSITIVE_INFINITY,
      extend: false,
    });
  });

  it("moves a page with Page Up and Page Down", () => {
    expect(act("PageDown")).toEqual({ type: "move", rows: page, cols: 0, extend: false });
    expect(act("PageUp", { shiftKey: true })).toEqual({
      type: "move",
      rows: -page,
      cols: 0,
      extend: true,
    });
  });

  it("leaves Tab to the page, so the keyboard can leave the grid", () => {
    expect(act("Tab")).toBeNull();
    expect(act("Tab", { shiftKey: true })).toBeNull();
  });

  it("leaves Alt and an arrow to the window", () => {
    expect(act("ArrowLeft", { altKey: true })).toBeNull();
  });

  it("edits with Enter and F2, and typing starts the edit with what was typed", () => {
    expect(act("Enter")).toEqual({ type: "edit", text: null });
    expect(act("F2")).toEqual({ type: "edit", text: null });
    expect(act("7")).toEqual({ type: "edit", text: "7" });
    expect(act("A", { shiftKey: true })).toEqual({ type: "edit", text: "A" });
    expect(act("Backspace")).toEqual({ type: "edit", text: "" });
  });

  it("types a character made with AltGr (Ctrl and Alt), but not a Ctrl shortcut", () => {
    expect(act("@", { ctrlKey: true, altKey: true })).toEqual({ type: "edit", text: "@" });
    expect(act("q", { ctrlKey: true })).toBeNull();
    expect(act("c", { metaKey: true })).toBeNull();
  });

  it("selects all, copies, pastes, fills down and resets", () => {
    expect(act("a", { ctrlKey: true })).toEqual({ type: "selectAll" });
    expect(act("c", { ctrlKey: true })).toEqual({ type: "copy" });
    expect(act("V", { ctrlKey: true })).toEqual({ type: "paste" });
    expect(act("d", { ctrlKey: true })).toEqual({ type: "fill" });
    expect(act("Delete")).toEqual({ type: "reset" });
  });

  it("leaves the window's keys alone", () => {
    expect(act("z", { ctrlKey: true })).toBeNull();
    expect(act("y", { ctrlKey: true })).toBeNull();
    expect(act("s", { ctrlKey: true })).toBeNull();
    expect(act("k", { ctrlKey: true })).toBeNull();
    expect(act("d", { ctrlKey: true, shiftKey: true })).toBeNull();
    expect(act("Delete", { shiftKey: true })).toBeNull();
    expect(act("Escape")).toBeNull();
    expect(act("Enter", { ctrlKey: true })).toBeNull();
    expect(act("Dead")).toBeNull();
    expect(act("Shift", { shiftKey: true })).toBeNull();
  });
});

describe("editorAction", () => {
  it("sends and moves down with Enter, right or left with Tab, and drops the edit with Esc", () => {
    expect(editorAction(press("Enter"))).toEqual({ type: "commit", rows: 1, cols: 0 });
    expect(editorAction(press("Tab"))).toEqual({ type: "commit", rows: 0, cols: 1 });
    expect(editorAction(press("Tab", { shiftKey: true }))).toEqual({
      type: "commit",
      rows: 0,
      cols: -1,
    });
    expect(editorAction(press("Escape"))).toEqual({ type: "cancel" });
  });

  it("leaves Shift Enter (a new line), Ctrl Enter and every other key to the box", () => {
    expect(editorAction(press("Enter", { shiftKey: true }))).toBeNull();
    expect(editorAction(press("Enter", { ctrlKey: true }))).toBeNull();
    expect(editorAction(press("ArrowDown"))).toBeNull();
    expect(editorAction(press("a"))).toBeNull();
    expect(editorAction(press("Delete"))).toBeNull();
  });
});
