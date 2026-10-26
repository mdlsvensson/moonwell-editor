/**
 * The table's logic with no screen: which cells a selection covers, how rows sort, and the edits that paste, fill
 * down, reset and the bar's expressions make from the selected cells. A cell can be changed in bulk only when its row
 * is not read only and the cell applies.
 */
import type { CellEdit, Column, FieldInfo, Kind, TableCell, TableRow } from "./api.ts";
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

/** What a cell of the shown table is made of; null when the cell is outside the table. */
type Target = { row: TableRow; cell: TableCell; column: Column };

function targetOf(at: Cell, rows: TableRow[], columns: Column[]): Target | null {
  const row = rows[at.row];
  const cell = row?.cells[at.col];
  const column = columns[at.col];
  if (!row || !cell || !column) return null;
  return { row, cell, column };
}

/** The target of a cell a bulk change may change: its row is not read only and the cell applies; else null. */
function editableTarget(at: Cell, rows: TableRow[], columns: Column[]): Target | null {
  const target = targetOf(at, rows, columns);
  return target && target.row.readOnly === null && target.cell.applies ? target : null;
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

/** True when an empty pasted cell is a value for the column's field (text and lists), not a gap. */
function takesEmpty(column: Column, fields: FieldInfo[]): boolean {
  const type = fields.find((field) => field.id === column.field)?.type;
  return type !== undefined && emptyTypes.includes(type);
}

/**
 * The edits that paste a block starting at a cell: down the shown rows and right across the columns, stopping at the
 * table's edge. Cells that cannot be changed (read only row, cell that does not apply) are skipped. An empty cell
 * in the block is a gap: skipped for a column that is not text (`int`, `float`, `bool`, `choice`, or a field not in
 * `fields`), and written as "" for a `string`, `objectList` or `objectRef` column.
 */
export function pasteEdits(
  start: Cell,
  block: string[][],
  rows: TableRow[],
  columns: Column[],
  fields: FieldInfo[],
): CellEdit[] {
  const edits: CellEdit[] = [];
  for (const [r, blockRow] of block.entries()) {
    for (const [c, value] of blockRow.entries()) {
      const target = editableTarget({ row: start.row + r, col: start.col + c }, rows, columns);
      if (!target) continue;
      if (value === "" && !takesEmpty(target.column, fields)) continue;
      edits.push(editOf(target, value));
    }
  }
  return edits;
}

/**
 * The edits that copy the top cell of each selected column down the rest of the selection. A column whose top cell
 * does not apply copies nothing; cells that cannot be changed are skipped.
 */
export function fillDownEdits(s: Selection, rows: TableRow[], columns: Column[]): CellEdit[] {
  const cells = cellsOf(s);
  const top = Math.min(s.anchor.row, s.focus.row);
  const edits: CellEdit[] = [];
  for (const at of cells) {
    const target = editableTarget(at, rows, columns);
    const source = rows[top]?.cells[at.col];
    if (at.row === top || !target || !source?.applies) continue;
    edits.push(editOf(target, source.value));
  }
  return edits;
}

/** The edits that reset the selected cells to their standard values (a null value); cells that cannot be changed are skipped. */
export function resetEdits(s: Selection, rows: TableRow[], columns: Column[]): CellEdit[] {
  const edits: CellEdit[] = [];
  for (const at of cellsOf(s)) {
    const target = editableTarget(at, rows, columns);
    if (target) edits.push(editOf(target, null));
  }
  return edits;
}

/** A column as a message names it: the field's label (its id when unknown), with the level for a per-level column. */
function columnName(column: Column, fields: FieldInfo[]): string {
  const label = fields.find((field) => field.id === column.field)?.label ?? column.field;
  return column.level > 0 ? `${label} · ${column.level}` : label;
}

/**
 * The edits that apply what was typed in the bar (a value or an expression) to every selected cell, one edit for each
 * cell. All or nothing: when a cell cannot be changed (read only row, cell that does not apply, a selection outside
 * the table) or the expression does not fit its field, the answer is an error that names the first such cell by its
 * row name and column.
 */
export function bulkEdits(
  s: Selection,
  rows: TableRow[],
  columns: Column[],
  fields: FieldInfo[],
  text: string,
): CellEdit[] | { error: string } {
  const parsed = parseExpression(text);
  if ("error" in parsed) return parsed;

  const targets: Target[] = [];
  for (const at of cellsOf(s)) {
    const target = targetOf(at, rows, columns);
    if (!target) return { error: "The selection is outside the table." };
    const where = `${target.row.name} · ${columnName(target.column, fields)}`;
    if (target.row.readOnly !== null) return { error: `${where}: ${target.row.readOnly}` };
    if (!target.cell.applies) return { error: `${where}: the field does not apply.` };
    targets.push(target);
  }

  const edits: CellEdit[] = [];
  for (const target of targets) {
    const field = fields.find((candidate) => candidate.id === target.column.field);
    const where = `${target.row.name} · ${columnName(target.column, fields)}`;
    if (!field) return { error: `${where}: unknown field.` };
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
