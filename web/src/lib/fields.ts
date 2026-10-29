/**
 * How the details panel lays out an object's fields: in categories, in World Editor's order, with a per-level field
 * gathered into one row that holds every level (shown as a grid, levels across).
 */
import {
  type ChoiceEntry,
  type ChoiceList,
  type FieldView,
  type Kind,
  kinds,
  type ObjectView,
} from "./api.ts";
import { t } from "./text.ts";

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
  stats: t("category.stats"),
  combat: t("category.combat"),
  abil: t("category.abil"),
  art: t("category.art"),
  move: t("category.move"),
  path: t("category.path"),
  sound: t("category.sound"),
  tech: t("category.tech"),
  text: t("category.text"),
  editor: t("category.editor"),
  data: t("category.data"),
};

/** A category's name to show: the known ones by name, another with a capital letter, none as "Other". */
export function categoryName(key: string): string {
  if (key === "") return t("category.noCategory");
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

/**
 * Finds an object's name by what a list holds (a `FieldView`'s `listOf`) and its id; undefined when unknown. `bare`
 * asks for the name without its suffix.
 */
export type NameOf = (listOf: string, id: string, bare?: boolean) => string | undefined;

/**
 * Finds the object an id in a list stands for: its kind, its name (with its suffix, as the tree shows it) and its name
 * alone (`bare`, the name when absent); undefined when it is not known.
 */
export type Lookup = (
  listOf: string,
  id: string,
) => { kind: Kind; name: string; bare?: string } | undefined;

/** A `NameOf` that asks `lookup`. */
export function namesOf(lookup: Lookup): NameOf {
  return (listOf, id, bare) => {
    const found = lookup(listOf, id);
    return bare ? (found?.bare ?? found?.name) : found?.name;
  };
}

// The metadata types World Editor shows as None when they hold nothing: a file, a sound, an order (its screens,
// 2026-10-27). A list of models (an ability's Target art) shows nothing instead.
const noneTypes = new Set([
  "model",
  "icon",
  "texture",
  "soundLabel",
  "unitSound",
  "orderString",
  "pathingTexture",
  "shadowTexture",
  "uberSplat",
  "lightningEffect",
]);

/** The sort of file a field names, by its metadata type, for its names (`assetNames`); undefined for none. */
export function assetGroup(meta: string | null | undefined): string | undefined {
  switch (meta) {
    case "icon":
      return "icon";
    case "model":
    case "modelList":
      return "model";
    case "pathingTexture":
      return "pathing";
    case "shadowTexture":
      return "shadow";
    case "uberSplat":
      return "splat";
    case "texture":
      return "texture";
    default:
      return undefined;
  }
}

/**
 * A file's key among the names: its path in lower case with backslashes and without the extension of a model, image
 * or texture, as the app makes it (`units\human\peasant\peasant`).
 */
export function assetKey(path: string): string {
  const key = path.trim().replaceAll("/", "\\").toLowerCase();
  const dot = key.lastIndexOf(".");
  if (
    dot > key.lastIndexOf("\\") &&
    [".mdl", ".mdx", ".blp", ".dds", ".tga"].includes(key.slice(dot))
  ) {
    return key.slice(0, dot);
  }
  return key;
}

// World Editor cuts a long list of names after the last one that fits (seen: 118 characters kept, 130 cut; the
// agent's reading is 128), and ends it with ", ...".
const namesLimit = 128;

/** Names joined as World Editor shows them: `A, B, C`, cut with `, ...` when the next would pass its limit. */
export function namesText(names: string[]): string {
  let text = "";
  for (const name of names) {
    const next = text === "" ? name : `${text}, ${name}`;
    if (text !== "" && next.length > namesLimit) return `${text}, ...`;
    text = next;
  }
  return text;
}

let namedBy: (group: string, key: string) => string[] | undefined = () => undefined;

/** Sets where the names of files come from (`assetNames.svelte.ts`). */
export function setAssetNames(lookup: (group: string, key: string) => string[] | undefined): void {
  namedBy = lookup;
}

/**
 * The names World Editor shows a file field's value by (Sven, 2026-10-27): the objects that use the file, joined and
 * cut as it does; undefined when the field names no file or no object uses it (World Editor shows the path then).
 */
export function assetText(field: Pick<FieldView, "meta" | "value">): string | undefined {
  const group = assetGroup(field.meta);
  if (!group || field.value.trim() === "") return undefined;
  const names = namedBy(group, assetKey(field.value));
  return names && names.length > 0 ? namesText(names) : undefined;
}

// The metadata types that are text in the data but which World Editor offers as a list, the app's list of the same
// name: a ground texture (UberSplatData's splats) and a building's or destructable's shadow (Sven, 2026-10-27).
const metaLists = new Set(["uberSplat", "shadowTexture"]);

/** The list a field's value is one of: its choice list, or the list World Editor offers for its type; else none. */
export function listFor(
  field: Pick<FieldView, "choice" | "meta">,
  choices: Record<string, ChoiceList> | null,
): ChoiceList | undefined {
  if (field.choice) return choices?.[field.choice];
  return field.meta && metaLists.has(field.meta) ? choices?.[field.meta] : undefined;
}

/**
 * What World Editor shows for a field that holds nothing (Sven, 2026-10-27): 0 for a whole number, 0.000 for a real,
 * False for a true-or-false field, None for one value of a list or a file, sound or order, and nothing for text, flags
 * and object lists.
 */
export function emptyText(
  field: Pick<FieldView, "type" | "choice" | "meta">,
  choices: Record<string, ChoiceList> | null,
): string {
  if (field.type === "int") return "0";
  if (field.type === "float") return "0.000";
  if (field.type === "bool") return t("value.false");
  if (field.type === "choice") {
    const list = field.choice ? choices?.[field.choice] : undefined;
    return list?.flags ? "" : t("value.none");
  }
  return noneTypes.has(field.meta ?? "") ? t("value.none") : "";
}

/**
 * The text shown for a field's value: a choice's label (or the labels of the values a list or bits hold), a list's
 * ids with the objects' names where known, Yes or No for a true-or-false field, else the value itself. Only the
 * field's type, choice list, list kind and value are read, so a table cell shows the same way.
 */
/** A real number as World Editor shows it, with three decimals (`0.150`, `190.000`); other text as it is. */
export function realText(value: string): string {
  const trimmed = value.trim();
  if (trimmed === "" || !Number.isFinite(Number(trimmed))) return value;
  return Number(trimmed).toFixed(3);
}

export function shownValue(
  field: Pick<FieldView, "type" | "choice" | "listOf" | "value" | "meta">,
  choices: Record<string, ChoiceList> | null,
  nameOf: NameOf,
): string {
  if (field.value.trim() === "") return emptyText(field, choices);
  const offered = field.type === "choice" ? undefined : listFor(field, choices);
  const entry = offered && entryOf(offered, field.value);
  if (entry) return entry.label;
  const named = assetText(field);
  if (named !== undefined) return named;
  if (field.type === "choice") {
    const list = field.choice ? choices?.[field.choice] : undefined;
    return list ? choiceText(list, field.value) : field.value;
  }
  // As World Editor shows them (Sven, 2026-10-27): a list by its objects' names (an unknown id as it is; a skin list
  // by the names alone, without their suffixes), True or False, and a real number with three decimals.
  if (field.type === "objectList" || field.type === "objectRef") {
    const listOf = field.listOf ?? "";
    const bare = field.meta === "abilitySkinList";
    return listIds(field.value)
      .map((id) => nameOf(listOf, id, bare) || id)
      .join(", ");
  }
  if (field.type === "bool") {
    if (field.value === "1") return t("value.true");
    if (field.value === "0") return t("value.false");
  }
  if (field.type === "float") return realText(field.value);
  return field.value;
}

/**
 * The entry of a list a value stands for: by its value or one of its other spellings (`invu` for `invulnerable`),
 * ignoring case; undefined when the list does not know it.
 */
export function entryOf(list: ChoiceList, text: string): ChoiceEntry | undefined {
  const wanted = text.toLowerCase();
  return list.entries.find(
    (entry) =>
      entry.value.toLowerCase() === wanted ||
      (entry.alternates ?? []).some((other) => other.toLowerCase() === wanted),
  );
}

// One value's label; a comma list's labels; or, for a list stored as a number, the labels of its bits (value n of the
// list is bit n).
function choiceText(list: ChoiceList, value: string): string {
  const label = (text: string) => entryOf(list, text)?.label ?? text;
  if (!list.flags) return label(value);
  if (list.store === "int") {
    const number = Number.parseInt(value, 10);
    if (Number.isNaN(number)) return value;
    // A hidden bit (one World Editor does not list) is not shown, as World Editor shows none (Sven, 2026-10-28).
    const set = list.entries.filter(
      (entry) => !entry.hidden && (number & Number(entry.value)) !== 0,
    );
    return set.length === 0 ? t("value.none") : set.map((entry) => entry.label).join(", ");
  }
  return listIds(value).map(label).join(", ");
}

/**
 * Whether a typed id can be used: four characters from ! to ~, none of them a comma. The game splits a list field at
 * the comma, so an id with one is lost in a unit's ability list (Sven, 2026-10-29).
 */
export function isTypedId(id: string): boolean {
  return /^[\x21-\x2B\x2D-\x7E]{4}$/.test(id);
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

/** A button position's X and Y fields (`Button Position - Normal (X)` and `(Y)`), which the command card sets together. */
export type ButtonPair = { x: FieldView; y: FieldView };

/**
 * The button position pair `field` belongs to, from the object's fields: a field whose label starts with "Button
 * Position" and ends in "(X)" or "(Y)", and the field with the other letter; null for any other field, or when the
 * other half is missing.
 */
export function buttonPair(field: FieldView, fields: FieldView[]): ButtonPair | null {
  const match = /^(Button Position.*)\((X|Y)\)$/.exec(field.label);
  if (!match) return null;
  const other = `${match[1]}(${match[2] === "X" ? "Y" : "X"})`;
  const partner = fields.find((each) => each.label === other && each.level === field.level);
  if (!partner) return null;
  return match[2] === "X" ? { x: field, y: partner } : { x: partner, y: field };
}

const pluralNames: Record<Kind, string> = {
  unit: t("kindPlural.unit"),
  item: t("kindPlural.item"),
  destructable: t("kindPlural.destructable"),
  doodad: t("kindPlural.doodad"),
  ability: t("kindPlural.ability"),
  buff: t("kindPlural.buff"),
  upgrade: t("kindPlural.upgrade"),
};

/** The picker's prompt for a list of these kinds: "Search buffs by name or id…" (Sven, 2026-10-27). */
export function searchPrompt(kinds: Kind[]): string {
  const names = kinds.map((kind) => pluralNames[kind]);
  const what = names.length === 0 ? t("picker.searchObjects") : names.join(t("picker.searchJoin"));
  return t("picker.search", { what });
}

/** One object another object's fields name: its kind, id and name ("" when unknown), and the fields that name it. */
export type Used = { kind: Kind; id: string; name: string; fields: string[] };

/**
 * The objects an object's list fields name (its abilities, the units it trains, its requirements …), once each, in
 * field order, with the labels of the fields that name them. An id the trees do not know keeps the list's first kind.
 */
export function uses(view: ObjectView, lookup: Lookup): Used[] {
  const found = new Map<string, Used>();
  for (const field of view.fields) {
    if (field.listOf === null) continue;
    for (const id of listIds(field.value)) {
      const known = lookup(field.listOf, id);
      const kind = known?.kind ?? listKinds(field.listOf)[0];
      if (!kind) continue;
      const key = `${kind}:${id}`;
      const used = found.get(key) ?? { kind, id, name: known?.name ?? "", fields: [] };
      if (!used.fields.includes(field.label)) used.fields.push(field.label);
      found.set(key, used);
    }
  }
  return [...found.values()];
}
