/**
 * The Table view's logic with no screen: which objects are its rows (the row chips and the tree's filter text), the
 * columns each kind keeps on this computer, the column picker's search, and sorting by a header.
 */
import type { Column, FieldInfo, Kind, TableRow, TreeEntry } from "./api.ts";
import { categoryName } from "./fields.ts";
import { columnKey, defaultColumns, sortRows } from "./grid.ts";
import { t } from "./text.ts";
import { entryMatches, groupName, isCustom } from "./tree.ts";

/** The group of the map's custom objects (their path is `["Custom"]`). */
export const customGroup = "Custom";

/**
 * The row chips of one kind: the groups picked (a tree's top group such as `human`, or `Custom`), where none picked
 * means every group, and "Changed", which keeps only the objects the map changes.
 */
export type Chips = { groups: string[]; changed: boolean };

/** One group chip: the group's key (an object's first path step), its name to show and how many objects it has. */
export type GroupChip = { key: string; name: string; count: number };

/** How the table is sorted: by `name`, `id` or a column's key (`columnKey`), up or down. */
export type Sort = { key: string; descending: boolean };

/** What the Table view keeps per kind while the map is open: the row chips, the sort and the columns. */
export type TableState = {
  chips: Partial<Record<Kind, Chips>>;
  sort: Partial<Record<Kind, Sort | null>>;
  columns: Partial<Record<Kind, Column[]>>;
};

/** A Table view state with nothing chosen yet. */
export function newTableState(): TableState {
  return { chips: {}, sort: {}, columns: {} };
}

function groupOf(entry: TreeEntry): string {
  return entry.path[0] ?? "";
}

/** True when the map changes the object: a custom object, or a standard one the map modifies. */
export function isChangedEntry(entry: TreeEntry): boolean {
  return entry.changed || isCustom(entry);
}

/** The group chips of a kind's tree: Custom first (also when the map has none), then the top groups in tree order. */
export function groupChips(entries: TreeEntry[]): GroupChip[] {
  const counts = new Map<string, number>([[customGroup, 0]]);
  for (const entry of entries) {
    const key = groupOf(entry);
    if (key === "") continue;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return [...counts].map(([key, count]) => ({
    key,
    name: key === customGroup ? t("tree.custom") : groupName(key),
    count,
  }));
}

/**
 * The chips a kind's table starts with: the custom objects when the map has any, else the group of the selected
 * object, else the first group of the tree.
 */
export function defaultChips(entries: TreeEntry[], selected: string | null): Chips {
  if (entries.some(isCustom)) return { groups: [customGroup], changed: false };
  const picked = entries.find((entry) => entry.id === selected) ?? entries[0];
  const group = picked ? groupOf(picked) : "";
  return { groups: group === "" ? [] : [group], changed: false };
}

/** The chips with a group picked or unpicked. */
export function toggleGroup(chips: Chips, group: string): Chips {
  const groups = chips.groups.includes(group)
    ? chips.groups.filter((each) => each !== group)
    : [...chips.groups, group];
  return { ...chips, groups };
}

/**
 * The ids of the table's rows, in the tree's order and each once: the objects of the picked groups (every group when
 * none is picked), only the changed ones with "Changed", whose id, name or suffix holds the filter text.
 */
export function rowIds(entries: TreeEntry[], chips: Chips, filter: string): string[] {
  const seen = new Set<string>();
  const ids: string[] = [];
  for (const entry of entries) {
    if (chips.groups.length > 0 && !chips.groups.includes(groupOf(entry))) continue;
    if (chips.changed && !isChangedEntry(entry)) continue;
    if (!entryMatches(entry, filter) || seen.has(entry.id)) continue;
    seen.add(entry.id);
    ids.push(entry.id);
  }
  return ids;
}

// A column's key lives with the grid, which keeps a selection by it; it is used here too.
export { columnKey };

/**
 * The columns that fit the kind's fields: a column of a field the kind lacks is dropped, as is one whose level does
 * not fit its field (from 1 for a per-level field, 0 for any other), and a column given twice is kept once. Anything
 * that is not a list of columns gives none.
 */
export function fitColumns(columns: unknown, fields: FieldInfo[]): Column[] {
  if (!Array.isArray(columns)) return [];
  const byId = new Map(fields.map((field) => [field.id, field]));
  const seen = new Set<string>();
  const result: Column[] = [];
  for (const item of columns) {
    if (typeof item !== "object" || item === null) continue;
    const { field, level } = item as { field?: unknown; level?: unknown };
    if (typeof field !== "string" || typeof level !== "number" || !Number.isInteger(level))
      continue;
    const info = byId.get(field);
    if (!info || (info.perLevel ? level < 1 : level !== 0)) continue;
    const column = { field, level };
    if (seen.has(columnKey(column))) continue;
    seen.add(columnKey(column));
    result.push(column);
  }
  return result;
}

/** Where a kind's columns are kept in the window's storage. */
export function storageKey(kind: Kind): string {
  return `objectEditor.columns.${kind}`;
}

/** The window's storage, as far as the columns use it. */
export type Store = Pick<Storage, "getItem" | "setItem">;

/**
 * A kind's columns: the ones kept in `store` (as `storageKey` names them), else the kind's default columns, fitted to
 * its fields (`fitColumns`). A store that is missing, throws or holds something that is not a list gives the defaults.
 */
export function readColumns(kind: Kind, fields: FieldInfo[], store: () => Store | null): Column[] {
  let kept: unknown = null;
  try {
    const text = store()?.getItem(storageKey(kind));
    if (text) kept = JSON.parse(text);
  } catch {
    kept = null;
  }
  return fitColumns(Array.isArray(kept) ? kept : defaultColumns[kind], fields);
}

/** Keeps a kind's columns in `store`; a store that is missing or throws is ignored. */
export function writeColumns(kind: Kind, columns: Column[], store: () => Store | null): void {
  try {
    store()?.setItem(storageKey(kind), JSON.stringify(columns));
  } catch {
    // The columns are kept for this window only.
  }
}

/** The most level columns "All levels" adds at once, so a long level count does not flood the table. */
export const levelColumnLimit = 20;

/**
 * The columns with a field added: a field with no levels as one column, a per-level field as one column for each
 * level from 1 to `levels` (at least level 1, at most `levelColumnLimit`). They go after the field's last column when
 * it has one, else at the end. A (field, level) that is already a column is not added again.
 */
export function addColumns(columns: Column[], field: FieldInfo, levels: number): Column[] {
  const count = Math.min(levelColumnLimit, Math.max(1, Math.floor(levels)));
  const wanted: Column[] = field.perLevel
    ? Array.from({ length: count }, (_, at) => ({
        field: field.id,
        level: at + 1,
      }))
    : [{ field: field.id, level: 0 }];
  const keys = new Set(columns.map(columnKey));
  const added = wanted.filter((column) => !keys.has(columnKey(column)));
  const after = columns.findLastIndex((column) => column.field === field.id) + 1;
  const at = after > 0 ? after : columns.length;
  return [...columns.slice(0, at), ...added, ...columns.slice(at)];
}

/** The columns without the one with `key`. */
export function removeColumn(columns: Column[], key: string): Column[] {
  return columns.filter((column) => columnKey(column) !== key);
}

/**
 * The fields the column picker shows for what was typed: those whose label, id or category holds every word of it,
 * ignoring case, in the order given. Nothing typed keeps every field.
 */
export function searchFields(fields: FieldInfo[], query: string): FieldInfo[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return fields;
  return fields.filter((field) => {
    const text =
      `${field.label} ${field.id} ${field.category} ${categoryName(field.category)}`.toLowerCase();
    return words.every((word) => text.includes(word));
  });
}

/**
 * The field that holds an object's number of levels, by kind (a doodad's number of variations); none for the kinds
 * without levels. The column picker asks the table for it to add "all levels".
 */
export const levelFields: Partial<Record<Kind, string>> = {
  ability: "alev",
  upgrade: "glvl",
  doodad: "dvar",
};

/** The most levels any row has, from rows whose first cell is the level count: at least 1. */
export function mostLevels(rows: TableRow[]): number {
  let most = 1;
  for (const row of rows) {
    const cell = row.cells[0];
    const levels = cell?.applies ? Number.parseInt(cell.value, 10) : Number.NaN;
    if (Number.isFinite(levels) && levels > most) most = levels;
  }
  return most;
}

/** The sort after a click on a header: up first, then down, then back to the tree's order. */
export function nextSort(sort: Sort | null, key: string): Sort | null {
  if (sort?.key !== key) return { key, descending: false };
  return sort.descending ? null : { key, descending: true };
}

/**
 * The rows in the order of `sort`: by name (ignoring case) or id, or by a column's cells as `sortRows` sorts them. No
 * sort, or a column that is not shown, keeps the order given. The rows given are not changed.
 */
export function sortTable(rows: TableRow[], sort: Sort | null, columns: Column[]): TableRow[] {
  if (!sort) return rows;
  if (sort.key === "name" || sort.key === "id") {
    const text = (row: TableRow) => (sort.key === "name" ? row.name : row.id);
    const order = sort.descending ? -1 : 1;
    return [...rows].sort(
      (a, b) => order * text(a).localeCompare(text(b), undefined, { sensitivity: "base" }),
    );
  }
  const col = columns.findIndex((column) => columnKey(column) === sort.key);
  return col < 0 ? rows : sortRows(rows, col, sort.descending);
}

/**
 * Where the table's rows stand for a bulk change, which must be worked out from the values as they are: the edits
 * from this view still on their way (`editsOut`), the map's state now (`session`, a new object for each change), the
 * state an edit changed until the next one comes (`waitingFor`), the state the rows shown were fetched for
 * (`rowsFor`), and why the last fetch failed (`rowsError`, "" when it did not).
 */
export type RowsFreshness = {
  editsOut: number;
  session: unknown;
  waitingFor: { session: unknown } | null;
  rowsFor: { session: unknown } | null;
  rowsError: string;
};

/**
 * Whether a bulk change can be worked out now: "ready" when no edit is on its way and the rows were fetched for the
 * map as it is now; "wait" until then; or the reason the rows could not be fetched.
 */
export function rowsReadiness(f: RowsFreshness): "ready" | "wait" | { error: string } {
  if (f.rowsError) return { error: f.rowsError };
  if (f.editsOut > 0) return "wait";
  if (f.waitingFor && f.waitingFor.session === f.session) return "wait";
  if (!f.rowsFor || f.rowsFor.session !== f.session) return "wait";
  return "ready";
}

/** Runs a task after the ones given before it; answers what it answers. */
export type Queue = <T>(task: () => Promise<T>) => Promise<T>;

/**
 * A queue that runs tasks one at a time, in the order they were given: each starts once the one before has finished,
 * whether it worked or failed (a failure is passed on to whoever gave that task only). The table's bulk changes go
 * through one, so a second change (Enter twice on `+10%`, a paste then Ctrl D) works from what the first one left.
 */
export function oneAtATime(): Queue {
  let last: Promise<unknown> = Promise.resolve();
  return <T>(task: () => Promise<T>): Promise<T> => {
    const run = last.then(task);
    last = run.catch(() => undefined);
    return run;
  };
}
