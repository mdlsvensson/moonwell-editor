/**
 * How the details panel lays out an object's fields: in categories, in World Editor's order, with a per-level field
 * gathered into one row that holds every level (shown as a grid, levels across).
 */
import { type ChoiceList, type FieldView, type Kind, kinds, type ObjectView } from "./api.ts";

/** One row of the details panel: a field, or a per-level field with all its levels. */
export type Row = {
  id: string;
  label: string;
  sort: string;
  /** The field; for a per-level field its first level, which carries the type, choice list and so on. */
  field: FieldView;
  /** A per-level field's levels, from level 1 up; null for a field with no levels. */
  levels: FieldView[] | null;
};

/** One collapsible category of the details panel. */
export type Category = {
  /** The metadata's category, as written (`stats`). */
  key: string;
  /** Its name to show (`Stats`). */
  name: string;
  rows: Row[];
};

/** The categories in the order the panel shows them; any other comes after these, by name. */
export const categoryOrder = [
  "stats",
  "combat",
  "abil",
  "art",
  "move",
  "path",
  "sound",
  "tech",
  "text",
  "editor",
  "data",
];

const categoryNames: Record<string, string> = {
  stats: "Stats",
  combat: "Combat",
  abil: "Abilities",
  art: "Art",
  move: "Movement",
  path: "Pathing",
  sound: "Sound",
  tech: "Techtree",
  text: "Text",
  editor: "Editor",
  data: "Data",
};

/** A category's name to show: the known ones by name, another with a capital letter, none as "Other". */
export function categoryName(key: string): string {
  if (key === "") return "Other";
  return categoryNames[key] ?? key.charAt(0).toUpperCase() + key.slice(1);
}

/**
 * Sorts fields into categories and rows.
 * - `filter` keeps a row whose label, id or value (any level's) holds it, ignoring case; "" keeps every row.
 * - `changedOnly` keeps a row the map changes (at any level, for a per-level field, which then shows every level).
 * - `shown` gives the text shown for a value (such as a choice's label), which the filter matches too.
 * Categories come in `categoryOrder`, then any other by name; rows by their sort key (a field without one last).
 * A category with no rows left is left out.
 */
export function groupFields(
  fields: FieldView[],
  filter: string,
  changedOnly: boolean,
  shown?: (field: FieldView) => string,
): Category[] {
  const needle = filter.trim().toLowerCase();
  const byCategory = new Map<string, Row[]>();
  const perLevel = new Map<string, Row>();

  for (const field of fields) {
    const known = field.level > 0 ? perLevel.get(field.id) : undefined;
    if (known) {
      known.levels?.push(field);
      continue;
    }
    const row: Row = {
      id: field.id,
      label: field.label,
      sort: field.sort,
      field,
      levels: field.level > 0 ? [field] : null,
    };
    if (field.level > 0) perLevel.set(field.id, row);
    const rows = byCategory.get(field.category) ?? [];
    rows.push(row);
    byCategory.set(field.category, rows);
  }

  const categories: Category[] = [];
  for (const [key, rows] of byCategory) {
    const kept = rows.filter(
      (row) => (!changedOnly || isChanged(row)) && matches(row, needle, shown),
    );
    if (kept.length === 0) continue;
    for (const row of kept) {
      row.levels?.sort((a, b) => a.level - b.level);
      if (row.levels?.[0]) row.field = row.levels[0];
    }
    // Array.sort keeps equal items in their order, so fields with the same key stay as the app gave them.
    kept.sort(bySortKey);
    categories.push({ key, name: categoryName(key), rows: kept });
  }
  return categories.sort((a, b) => categoryRank(a.key, b.key));
}

/** True when the map changes the row's field (at any level). */
export function isChanged(row: Row): boolean {
  return row.levels ? row.levels.some((level) => level.changed) : row.field.changed;
}

function matches(row: Row, needle: string, shown?: (field: FieldView) => string): boolean {
  if (needle === "") return true;
  if (row.label.toLowerCase().includes(needle) || row.id.toLowerCase().includes(needle)) {
    return true;
  }
  return (row.levels ?? [row.field]).some(
    (field) =>
      field.value.toLowerCase().includes(needle) ||
      shown?.(field).toLowerCase().includes(needle) === true,
  );
}

function bySortKey(a: Row, b: Row): number {
  if (a.sort === b.sort) return 0;
  if (a.sort === "") return 1;
  if (b.sort === "") return -1;
  return a.sort < b.sort ? -1 : 1;
}

function categoryRank(a: string, b: string): number {
  const rankA = categoryOrder.indexOf(a);
  const rankB = categoryOrder.indexOf(b);
  if (rankA >= 0 && rankB >= 0) return rankA - rankB;
  if (rankA >= 0) return -1;
  if (rankB >= 0) return 1;
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Finds an object's name by what a list holds (a `FieldView`'s `listOf`) and its id; undefined when unknown. */
export type NameOf = (listOf: string, id: string) => string | undefined;

/** Finds the object an id in a list stands for: its kind and name; undefined when it is not known. */
export type Lookup = (listOf: string, id: string) => { kind: Kind; name: string } | undefined;

/**
 * The text shown for a field's value: a choice's label (or the labels of the values a list or bits hold), a list's
 * ids with the objects' names where known, Yes or No for a true-or-false field, else the value itself.
 */
export function shownValue(
  field: FieldView,
  choices: Record<string, ChoiceList> | null,
  nameOf: NameOf,
): string {
  if (field.type === "choice") {
    const list = field.choice ? choices?.[field.choice] : undefined;
    return list ? choiceText(list, field.value) : field.value;
  }
  if (field.type === "objectList" || field.type === "objectRef") {
    const listOf = field.listOf ?? "";
    return listIds(field.value)
      .map((id) => {
        const name = nameOf(listOf, id);
        return name ? `${name} (${id})` : id;
      })
      .join(", ");
  }
  if (field.type === "bool") {
    if (field.value === "1") return "Yes";
    if (field.value === "0") return "No";
  }
  return field.value;
}

// One value's label; a comma list's labels; or, for a list stored as a number, the labels of its bits (value n of the
// list is bit n).
function choiceText(list: ChoiceList, value: string): string {
  const label = (text: string) =>
    list.entries.find((entry) => entry.value.toLowerCase() === text.toLowerCase())?.label ?? text;
  if (!list.flags) return label(value);
  if (list.store === "int") {
    const number = Number.parseInt(value, 10);
    if (Number.isNaN(number)) return value;
    const set = list.entries.filter((entry) => (number >> Number(entry.value)) & 1);
    return set.length === 0 ? "None" : set.map((entry) => entry.label).join(", ");
  }
  return listIds(value).map(label).join(", ");
}

/** The ids of an object list (a comma list), trimmed, with empty ones left out. */
export function listIds(value: string): string[] {
  return value
    .split(",")
    .map((id) => id.trim())
    .filter((id) => id !== "");
}

/** The kinds a list's ids are of, by its `listOf`: `object` holds units and upgrades. */
export function listKinds(listOf: string | null): Kind[] {
  if (listOf === null) return [];
  if (listOf === "object") return ["unit", "upgrade"];
  const kind = listOf.toLowerCase();
  return kinds.filter((candidate) => candidate === kind);
}

/** The abilities an object's ability lists hold (a unit's normal and hero abilities, an item's), once each. */
export function abilityIds(view: ObjectView): string[] {
  const ids: string[] = [];
  for (const field of view.fields) {
    if (field.listOf !== "Ability") continue;
    for (const id of listIds(field.value)) {
      if (!ids.includes(id)) ids.push(id);
    }
  }
  return ids;
}
