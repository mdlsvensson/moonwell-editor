/**
 * How a field is edited: which editor it gets, how a flags list or an object list is read and written back as the
 * data spells it, and what is checked before a value is sent to the app.
 */
import type { ChoiceList, FieldView, Kind, ObjectView, SaveReport } from "./api.ts";
import { entryOf, listFor, listIds, listKinds } from "./fields.ts";
import { t, tCount } from "./text.ts";

/** The editors a field can get. */
export type Editor = "number" | "toggle" | "select" | "flags" | "ids" | "text";

/**
 * The editor for a field: a number box, a toggle (true or false), a select (one value of a list, also a text field
 * World Editor offers a list for), checkboxes (any values of a list), chips with a picker (an object list), or a text
 * box for anything else, also a choice whose list is not known.
 */
export function editorFor(field: FieldView, choices: Record<string, ChoiceList> | null): Editor {
  if (field.type === "string" && listFor(field, choices)) return "select";
  switch (field.type) {
    case "int":
    case "float":
      return "number";
    case "bool":
      return "toggle";
    case "choice": {
      const list = field.choice ? choices?.[field.choice] : undefined;
      if (!list) return "text";
      return list.flags ? "flags" : "select";
    }
    case "objectList":
    case "objectRef":
      return listKinds(field.listOf).length > 0 ? "ids" : "text";
    default:
      return "text";
  }
}

// The extended tooltips (a unit's or item's, an ability's normal, turned-off and learn ones, an upgrade's, a buff's),
// which get a box of several lines (Sven, 2026-10-27).
const extendedTooltips = new Set(["utub", "aub1", "auu1", "arut", "gub1", "fube"]);

/** True for an extended tooltip field. */
export function isExtendedTooltip(field: Pick<FieldView, "id">): boolean {
  return extendedTooltips.has(field.id);
}

/**
 * The values a flags field holds: the entries' values as the list spells them, then any the list does not know, as
 * written. For a list stored as a number, the numbers of its set bits (value n is bit n).
 */
export function parseFlags(value: string, list: ChoiceList): string[] {
  if (list.store === "int") {
    const number = Number.parseInt(value, 10);
    if (Number.isNaN(number)) return [];
    const bits: string[] = [];
    for (let bit = 0; bit < 32; bit++) {
      if ((number >> bit) & 1) bits.push(String(bit));
    }
    return bits;
  }
  return listIds(value).map((text) => entryOf(list, text)?.value ?? text);
}

/**
 * A flags field's value from the values ticked: a comma list in the order of the list's entries (then any value it
 * does not know, in the order given), or, for a list stored as a number, the sum of the bits.
 */
export function formatFlags(ticked: Iterable<string>, list: ChoiceList): string {
  const set = [...ticked];
  if (list.store === "int") {
    let number = 0;
    for (const bit of set) number |= 1 << Number(bit);
    return String(number);
  }
  const known = list.entries.map((entry) => entry.value).filter((value) => set.includes(value));
  const unknown = set.filter((value) => !list.entries.some((entry) => entry.value === value));
  return [...known, ...unknown].join(",");
}

/** A value checked before it is sent: the text to send, or what is wrong with it. */
export type Checked = { ok: true; value: string } | { ok: false; problem: string };

const wholeNumber = /^[+-]?\d+$/;
const realNumber = /^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/;

/**
 * Checks a typed value as the app would: a whole-number field takes a 32-bit whole number and a real field a number
 * (both trimmed, with the app's own wording when they are not); text goes as typed.
 */
export function checkValue(field: FieldView, text: string): Checked {
  const trimmed = text.trim();
  if (field.type === "int") {
    const number = Number(trimmed);
    if (wholeNumber.test(trimmed) && number >= -2147483648 && number <= 2147483647) {
      return { ok: true, value: trimmed };
    }
    return { ok: false, problem: t("value.notWhole", { field: field.label, text }) };
  }
  if (field.type === "float") {
    if (realNumber.test(trimmed) && Number.isFinite(Number(trimmed))) {
      return { ok: true, value: trimmed };
    }
    return { ok: false, problem: t("value.notReal", { field: field.label, text }) };
  }
  return { ok: true, value: text };
}

/** An object list with an id added at the end (once); a field that holds one id gets it instead. */
export function addId(value: string, id: string, single: boolean): string {
  if (single) return id;
  const ids = listIds(value);
  if (ids.includes(id)) return ids.join(",");
  return [...ids, id].join(",");
}

/** An object list with the id at a place (from 0) taken out. */
export function removeId(value: string, index: number): string {
  return listIds(value)
    .filter((_, at) => at !== index)
    .join(",");
}

// The fields an object's name comes from, by kind, first found first (as the app's Resolver has them).
const nameFields: Record<Kind, string[]> = {
  unit: ["unam"],
  item: ["unam"],
  destructable: ["bnam"],
  doodad: ["dnam"],
  ability: ["anam"],
  buff: ["ftip", "fnam"],
  upgrade: ["gnam"],
};

/** The field that holds an object's name (F2 goes there); undefined when it has none. */
export function nameField(view: ObjectView): FieldView | undefined {
  for (const id of nameFields[view.kind]) {
    const found = view.fields.find((field) => field.id === id);
    if (found) return found;
  }
  return undefined;
}

/**
 * A status-bar notice's tooltip: its whole text first (the bar cuts a long one short), then its own title when it
 * has one.
 */
export function noticeTitle(notice: { text: string; title?: string }): string {
  return notice.title ? `${notice.text}\n${notice.title}` : notice.text;
}

/**
 * What the status bar says after a save: the files written, where the backup went (its last two parts), and the
 * save's note when it has one.
 */
export function savedText(report: SaveReport): string {
  const count = report.written.length;
  if (count === 0) return t("save.nothing");
  const parts = [tCount("save.saved", count)];
  if (report.backup !== "") {
    const folders = report.backup.split(/[\\/]/).filter(Boolean);
    parts.push(t("save.backup", { folder: folders.slice(-2).join("\\") }));
  }
  if (report.note !== "") parts.push(report.note);
  return parts.join(" · ");
}
