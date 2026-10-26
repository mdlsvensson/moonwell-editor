/**
 * The table's logic with no screen: which cells a selection covers, how rows sort, and the edits that paste, fill
 * down, reset and the bar's expressions make from the selected cells. A bulk change is all or nothing: a cell of a
 * read-only row that it would change refuses the whole change, naming the cell, as does a value its field cannot
 * take; a cell that does not apply is skipped (the bar refuses it too).
 */
import type {
  CellEdit,
  ChoiceList,
  Column,
  FieldInfo,
  FieldView,
  Kind,
  TableCell,
  TableRow,
} from "./api.ts";
import { applyExpression, parseExpression } from "./expression.ts";

/** A cell of the shown table: a row and a column, both from 0. */
export type Cell = { row: number; col: number };

/** A block of cells: where the selection started and where it ends now (either can be the upper left). */
export type Selection = { anchor: Cell; focus: Cell };

const plainNumber = /^[-+]?(\d+\.?\d*|\.\d+)$/;

/** The cells of the block between anchor and focus, row by row from the top left. */
export function cellsOf(s: Selection): Cell[] {
  const top = Math.min(s.anchor.row, s.focus.row);
  const bottom = Math.max(s.anchor.row, s.focus.row);
  const left = Math.min(s.anchor.col, s.focus.col);
  const right = Math.max(s.anchor.col, s.focus.col);
  const cells: Cell[] = [];
  for (let row = top; row <= bottom; row++) {
    for (let col = left; col <= right; col++) cells.push({ row, col });
  }
  return cells;
}

/** How many rows and columns a table (or a block) has. */
export type Size = { rows: number; cols: number };

/** The block's upper left cell, which a paste starts at. */
export function topLeft(s: Selection): Cell {
  return { row: Math.min(s.anchor.row, s.focus.row), col: Math.min(s.anchor.col, s.focus.col) };
}

/** How many rows and columns the block spans. */
export function blockSize(s: Selection): Size {
  return {
    rows: Math.abs(s.anchor.row - s.focus.row) + 1,
    cols: Math.abs(s.anchor.col - s.focus.col) + 1,
  };
}

function clampCell(at: Cell, size: Size): Cell {
  return {
    row: Math.max(0, Math.min(size.rows - 1, at.row)),
    col: Math.max(0, Math.min(size.cols - 1, at.col)),
  };
}

/**
 * The selection after a move of the focus by rows and columns, stopping at the table's edge (an infinite move goes to
 * it). With `extend` the anchor stays, so the block grows or shrinks; without, the one cell moved to is selected. With
 * nothing selected it starts at the top left; an empty table selects nothing.
 */
export function moveSelection(
  s: Selection | null,
  rows: number,
  cols: number,
  extend: boolean,
  size: Size,
): Selection | null {
  if (size.rows <= 0 || size.cols <= 0) return null;
  if (!s) return { anchor: { row: 0, col: 0 }, focus: { row: 0, col: 0 } };
  const focus = clampCell({ row: s.focus.row + rows, col: s.focus.col + cols }, size);
  return { anchor: extend ? clampCell(s.anchor, size) : focus, focus };
}

/** Every cell of the table (Ctrl A); nothing in an empty table. */
export function selectAll(size: Size): Selection | null {
  if (size.rows <= 0 || size.cols <= 0) return null;
  return { anchor: { row: 0, col: 0 }, focus: { row: size.rows - 1, col: size.cols - 1 } };
}

/** A column's key: its field and level (`ugol:0`, `acdn:2`). */
export function columnKey(column: Column): string {
  return `${column.field}:${column.level}`;
}

/** A cell kept by its object and column (`columnKey`), with where it was when it was kept. */
export type CellRef = { id: string; key: string; at: Cell };

/**
 * A selection kept by objects and columns, so it stays on them when the rows are sorted or fetched again: its two
 * ends, and the objects (`rows`) and columns (`cols`) the block covers.
 */
export type SelectionRef = { anchor: CellRef; focus: CellRef; rows: string[]; cols: string[] };

function cellRef(at: Cell, rows: TableRow[], columns: Column[]): CellRef {
  const row = rows[at.row];
  const column = columns[at.col];
  return { id: row?.id ?? "", key: column ? columnKey(column) : "", at };
}

/** The selection as its objects and columns, to find again after the rows or columns change. */
export function selectionRef(s: Selection, rows: TableRow[], columns: Column[]): SelectionRef {
  const start = topLeft(s);
  const size = blockSize(s);
  return {
    anchor: cellRef(s.anchor, rows, columns),
    focus: cellRef(s.focus, rows, columns),
    rows: rows.slice(start.row, start.row + size.rows).map((row) => row.id),
    cols: columns.slice(start.col, start.col + size.cols).map(columnKey),
  };
}

/** True when the items hold exactly the values wanted, in any order. */
function sameSet(items: string[], wanted: string[]): boolean {
  const set = new Set(wanted);
  return items.length === set.size && items.every((item) => set.has(item));
}

/**
 * Where a kept selection is in rows and columns that changed (sorted again, fetched again after an edit, a column
 * added or removed): each end on its object's row and its column. An object or column that is gone falls back to the
 * old place, pulled inside the table. The block is kept only when it then covers exactly its own objects and columns
 * (in any order inside it); otherwise (other rows sorted in between, a column added inside it, one of its rows or
 * columns gone) it is never widened but becomes the one focused cell. Nothing kept, or an empty table, selects
 * nothing.
 */
export function relocateSelection(
  ref: SelectionRef | null,
  rows: TableRow[],
  columns: Column[],
): Selection | null {
  const size = { rows: rows.length, cols: columns.length };
  if (!ref || size.rows <= 0 || size.cols <= 0) return null;
  const find = (kept: CellRef): Cell => {
    const row = rows.findIndex((item) => item.id === kept.id);
    const col = columns.findIndex((column) => columnKey(column) === kept.key);
    return clampCell({ row: row < 0 ? kept.at.row : row, col: col < 0 ? kept.at.col : col }, size);
  };
  const moved = { anchor: find(ref.anchor), focus: find(ref.focus) };
  const start = topLeft(moved);
  const span = blockSize(moved);
  const covered = rows.slice(start.row, start.row + span.rows).map((row) => row.id);
  const coveredCols = columns.slice(start.col, start.col + span.cols).map(columnKey);
  if (sameSet(covered, ref.rows) && sameSet(coveredCols, ref.cols)) return moved;
  return { anchor: moved.focus, focus: moved.focus };
}

/** True when two selections have the same anchor and focus (both none counts). */
export function sameSelection(a: Selection | null, b: Selection | null): boolean {
  if (!a || !b) return a === b;
  return (
    a.anchor.row === b.anchor.row &&
    a.anchor.col === b.anchor.col &&
    a.focus.row === b.focus.row &&
    a.focus.col === b.focus.col
  );
}

/**
 * The selected cells' values as the data spells them, row by row, for copying as tab-separated text. A cell that does
 * not apply (or is outside the table) is empty.
 */
export function copyBlock(s: Selection, rows: TableRow[]): string[][] {
  const start = topLeft(s);
  const size = blockSize(s);
  return Array.from({ length: size.rows }, (_, r) =>
    Array.from({ length: size.cols }, (_, c) => {
      const cell = rows[start.row + r]?.cells[start.col + c];
      return cell?.applies ? cell.value : "";
    }),
  );
}

/** A cell as the details view's editor takes it: the column's field at its level, with the cell's value. */
export function cellField(info: FieldInfo, column: Column, cell: TableCell): FieldView {
  return {
    id: info.id,
    label: info.label,
    category: info.category,
    sort: info.sort,
    type: info.type,
    choice: info.choice,
    listOf: info.listOf,
    level: column.level,
    value: cell.value,
    base: cell.base,
    changed: cell.changed,
    skin: false,
    warning: cell.warning,
    modes: null,
  };
}

/** Why a cell cannot be edited (its row is read only, or the field does not apply to the object); null when it can. */
export function editRefusal(
  row: TableRow,
  cell: TableCell,
  column: Column,
  fields: FieldInfo[],
): string | null {
  if (row.readOnly !== null) return `Read only: ${row.readOnly}`;
  if (!cell.applies) return `${columnName(column, fields)} does not apply to ${row.name}.`;
  return null;
}

/** What a cell of the shown table is made of; null when the cell is outside the table. */
type Target = { row: TableRow; cell: TableCell; column: Column };

function targetOf(at: Cell, rows: TableRow[], columns: Column[]): Target | null {
  const row = rows[at.row];
  const cell = row?.cells[at.col];
  const column = columns[at.col];
  if (!row || !cell || !column) return null;
  return { row, cell, column };
}

/** A cell as a message names it: its row's name and its column (`Footman · Gold cost`). */
function whereOf(target: Target, fields: FieldInfo[]): string {
  return `${target.row.name} · ${columnName(target.column, fields)}`;
}

/** The refusal of a change of a cell whose row is read only, naming the cell; null when its row can be changed. */
function readOnlyRefusal(target: Target, fields: FieldInfo[]): { error: string } | null {
  if (target.row.readOnly === null) return null;
  return { error: `${whereOf(target, fields)}: ${target.row.readOnly}` };
}

/** The field a column shows; undefined when the kind's fields do not have it. */
function fieldOf(column: Column, fields: FieldInfo[]): FieldInfo | undefined {
  return fields.find((field) => field.id === column.field);
}

/** The edit that sets (or, with a null value, resets) a cell. */
function editOf(target: Target, value: string | null): CellEdit {
  return { id: target.row.id, field: target.column.field, level: target.column.level, value };
}

/**
 * The rows sorted by the cells of a column: numbers as numbers (before text), text without regard to case, and empty
 * cells (also cells that do not apply) last whichever way it goes. Rows that compare equal keep their order. The
 * rows given are not changed.
 */
export function sortRows(rows: TableRow[], col: number, descending: boolean): TableRow[] {
  const keyOf = (row: TableRow) => {
    const cell = row.cells[col];
    const value = cell?.applies ? cell.value.trim() : "";
    if (value === "") return { empty: true, number: null as number | null, text: "" };
    return { empty: false, number: plainNumber.test(value) ? Number(value) : null, text: value };
  };
  const keyed = rows.map((row) => ({ row, key: keyOf(row) }));
  keyed.sort((a, b) => {
    if (a.key.empty || b.key.empty) return Number(a.key.empty) - Number(b.key.empty);
    let order: number;
    if (a.key.number !== null && b.key.number !== null) order = a.key.number - b.key.number;
    else if (a.key.number !== null) order = -1;
    else if (b.key.number !== null) order = 1;
    else order = a.key.text.toLowerCase().localeCompare(b.key.text.toLowerCase());
    return descending ? -order : order;
  });
  return keyed.map((item) => item.row);
}

const emptyTypes = ["string", "objectList", "objectRef"];

/** True when an empty pasted cell is a value for the field (text and lists), not a gap. */
function takesEmpty(field: FieldInfo | undefined): boolean {
  return field !== undefined && emptyTypes.includes(field.type);
}

const numberTypes = ["int", "float"];
const yes = ["1", "yes", "true"];
const no = ["0", "no", "false"];
// How many of a choice's labels a refusal lists.
const labelsShown = 8;

/** What a value for a field is, as the data spells it, or why the field cannot take it. */
type Plain = { value: string } | { error: string };

/**
 * A plain value for a field, as the data spells it. A yes/no field takes 1, 0, yes, no, true or false (case aside,
 * spaces around ignored) and is sent as 1 or 0. A choice of one value takes one of its list's values, one of the other
 * spellings the game's data uses for it, or its label (case aside, in that order, so a value wins over another
 * entry's label), and is sent as the value. Anything else is taken as it is: text, lists, a flags list, and a choice
 * whose list is not known (the app checks those).
 */
function plainValue(
  field: FieldInfo | undefined,
  text: string,
  choices: Record<string, ChoiceList> | null,
): Plain {
  if (field?.type === "bool") {
    const typed = text.trim().toLowerCase();
    if (yes.includes(typed)) return { value: "1" };
    if (no.includes(typed)) return { value: "0" };
    return { error: `takes Yes or No (1 or 0), not "${text}".` };
  }
  const list = field?.type === "choice" && field.choice ? choices?.[field.choice] : undefined;
  if (!list || list.flags) return { value: text };
  const typed = text.trim().toLowerCase();
  const entry =
    list.entries.find((each) => each.value.toLowerCase() === typed) ??
    list.entries.find((each) => each.alternates?.some((other) => other.toLowerCase() === typed)) ??
    list.entries.find((each) => each.label.toLowerCase() === typed);
  if (entry) return { value: entry.value };
  const labels = list.entries.slice(0, labelsShown).map((each) => each.label);
  const more = list.entries.length > labelsShown ? ", …" : "";
  return { error: `"${text}" is not one of its values (${labels.join(", ")}${more}).` };
}

/**
 * A number pasted with a decimal comma (`1,5`), as spreadsheets in many countries copy it, with a dot (`1.5`): only a
 * plain decimal with exactly one comma and no dot. Anything else is left as it is.
 */
function decimalComma(value: string): string {
  if (value.split(",").length !== 2 || value.includes(".")) return value;
  const dotted = value.replace(",", ".");
  return plainNumber.test(dotted) ? dotted : value;
}

/** A pasted cell's value for a field: a number with a decimal comma read, yes/no and choices as `plainValue`. */
function pastedValue(
  field: FieldInfo | undefined,
  value: string,
  choices: Record<string, ChoiceList> | null,
): Plain {
  if (field && numberTypes.includes(field.type)) return { value: decimalComma(value) };
  return plainValue(field, value, choices);
}

/**
 * The edits that paste a block starting at a cell: down the shown rows and right across the columns, stopping at the
 * table's edge. An empty cell in the block is a gap: skipped for a column that is not text (`int`, `float`, `bool`,
 * `choice`, or a field not in `fields`), and written as "" for a `string`, `objectList` or `objectRef` column. A cell
 * that does not apply is skipped. A number with a decimal comma (`1,5`) is read as `1.5`; a yes/no or choice cell
 * is read as the bar reads it (`Yes`, a choice's label). All or nothing: a cell of a read-only row that the paste
 * would write, or a value its field cannot take, is an error that names the first such cell.
 */
export function pasteEdits(
  start: Cell,
  block: string[][],
  rows: TableRow[],
  columns: Column[],
  fields: FieldInfo[],
  choices: Record<string, ChoiceList> | null,
): CellEdit[] | { error: string } {
  const edits: CellEdit[] = [];
  for (const [r, blockRow] of block.entries()) {
    for (const [c, value] of blockRow.entries()) {
      const target = targetOf({ row: start.row + r, col: start.col + c }, rows, columns);
      if (!target) continue;
      const field = fieldOf(target.column, fields);
      if (value === "" && !takesEmpty(field)) continue;
      const refused = readOnlyRefusal(target, fields);
      if (refused) return refused;
      if (!target.cell.applies) continue;
      const checked = pastedValue(field, value, choices);
      if ("error" in checked) return { error: `${whereOf(target, fields)}: ${checked.error}` };
      edits.push(editOf(target, checked.value));
    }
  }
  return edits;
}

/**
 * The edits that copy the top cell of each selected column down the rest of the selection. A column whose top cell
 * does not apply copies nothing; a cell below that does not apply is skipped. The top row is only read, but a cell
 * below in a read-only row refuses the whole fill, an error that names the first such cell.
 */
export function fillDownEdits(
  s: Selection,
  rows: TableRow[],
  columns: Column[],
  fields: FieldInfo[],
): CellEdit[] | { error: string } {
  const top = Math.min(s.anchor.row, s.focus.row);
  const edits: CellEdit[] = [];
  for (const at of cellsOf(s)) {
    const source = rows[top]?.cells[at.col];
    const target = targetOf(at, rows, columns);
    if (at.row === top || !target || !source?.applies) continue;
    const refused = readOnlyRefusal(target, fields);
    if (refused) return refused;
    if (target.cell.applies) edits.push(editOf(target, source.value));
  }
  return edits;
}

/**
 * The edits that reset the selected cells to their standard values (a null value). A cell that does not apply is
 * skipped; a cell of a read-only row refuses the whole reset, an error that names the first such cell.
 */
export function resetEdits(
  s: Selection,
  rows: TableRow[],
  columns: Column[],
  fields: FieldInfo[],
): CellEdit[] | { error: string } {
  const edits: CellEdit[] = [];
  for (const at of cellsOf(s)) {
    const target = targetOf(at, rows, columns);
    if (!target) continue;
    const refused = readOnlyRefusal(target, fields);
    if (refused) return refused;
    if (target.cell.applies) edits.push(editOf(target, null));
  }
  return edits;
}

/** A column as a message names it: the field's label (its id when unknown), with the level for a per-level column. */
function columnName(column: Column, fields: FieldInfo[]): string {
  const label = fieldOf(column, fields)?.label ?? column.field;
  return column.level > 0 ? `${label} · ${column.level}` : label;
}

/** What the bar sets a column that is not a number to: the text trimmed, without one `=` in front. */
function barValue(text: string): string {
  const typed = text.trim();
  return typed.startsWith("=") ? typed.slice(1).trim() : typed;
}

/**
 * The edits that apply what was typed in the bar (a value or an expression) to every selected cell, one edit for each
 * cell. A number column (`int`, `float`) reads it as an expression; any other column takes a plain value: the text
 * trimmed, without one `=` in front (`=Footman` sets `Footman`), so `x001` or `-1` is a value there, not a
 * calculation. A yes/no or choice column reads that value as `plainValue` does (`Yes`, a choice's label) and gets
 * the data's spelling. All or nothing: when a cell cannot be changed (read only row, cell that does not apply, a
 * selection outside the table) or the value or expression does not fit its field, the answer is an error that names
 * the first such cell by its row name and column. Empty text is an error.
 */
export function bulkEdits(
  s: Selection,
  rows: TableRow[],
  columns: Column[],
  fields: FieldInfo[],
  text: string,
  choices: Record<string, ChoiceList> | null,
): CellEdit[] | { error: string } {
  const parsed = parseExpression(text);
  if ("error" in parsed) return parsed;

  const targets: Target[] = [];
  for (const at of cellsOf(s)) {
    const target = targetOf(at, rows, columns);
    if (!target) return { error: "The selection is outside the table." };
    const refused = readOnlyRefusal(target, fields);
    if (refused) return refused;
    if (!target.cell.applies)
      return { error: `${whereOf(target, fields)}: the field does not apply.` };
    targets.push(target);
  }

  const edits: CellEdit[] = [];
  for (const target of targets) {
    const field = fieldOf(target.column, fields);
    const where = whereOf(target, fields);
    if (!field) return { error: `${where}: unknown field.` };
    if (!numberTypes.includes(field.type)) {
      const plain = plainValue(field, barValue(text), choices);
      if ("error" in plain) return { error: `${where}: ${plain.error}` };
      edits.push(editOf(target, plain.value));
      continue;
    }
    const value = applyExpression(parsed, target.cell.value, field.type);
    if (typeof value !== "string") return { error: `${where}: ${value.error}` };
    edits.push(editOf(target, value));
  }
  return edits;
}

/**
 * The columns each kind's table starts with (a field the game data lacks is dropped when the columns load). A
 * per-level field starts at level 1.
 */
export const defaultColumns: Record<Kind, Column[]> = {
  unit: ["ugol", "ulum", "uhpm", "ua1b", "udef", "ua1c"].map((field) => ({ field, level: 0 })),
  item: ["igol", "ilum", "ilev"].map((field) => ({ field, level: 0 })),
  destructable: [{ field: "bhps", level: 0 }],
  doodad: [],
  ability: ["amcs", "acdn", "aran"].map((field) => ({ field, level: 1 })),
  buff: [],
  upgrade: [],
};
