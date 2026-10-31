/** What the trigger editor's lists show: the tree's rows and what a parameter or a new function can be. */
import type { Kind } from "../api.ts";
import type { PickKind } from "../map/picking.ts";
import type {
  Catalog,
  CatalogFunction,
  CatalogPreset,
  FunctionKind,
  FunctionView,
  ParameterView,
  TreeItem,
  TriggerView,
  Variable,
} from "./api.ts";

/** One row of the tree. */
export type TreeRow = { item: TreeItem; depth: number; children: boolean };

/**
 * The rows to show, in tree order: the root, then what is in each open category. With a filter, every item whose name
 * has it and the categories around it, open or not.
 */
export function treeRows(
  items: TreeItem[],
  isOpen: (item: TreeItem) => boolean,
  filter: string,
): TreeRow[] {
  const byParent = new Map<number, TreeItem[]>();
  for (const item of items) {
    if (item.kind === "root") continue;
    const siblings = byParent.get(item.parent);
    if (siblings) siblings.push(item);
    else byParent.set(item.parent, [item]);
  }
  const wanted = filter.trim().toLowerCase();
  const matches = (item: TreeItem): boolean =>
    item.name.toLowerCase().includes(wanted) ||
    (byParent.get(item.id) ?? []).some((child) => matches(child));

  const rows: TreeRow[] = [];
  const add = (item: TreeItem, depth: number): void => {
    const children = byParent.get(item.id) ?? [];
    rows.push({ item, depth, children: children.length > 0 });
    if (wanted === "" ? item.kind !== "root" && !isOpen(item) : false) return;
    for (const child of children) {
      if (wanted === "" || matches(child)) add(child, depth + 1);
    }
  };
  const root = items.find((item) => item.kind === "root");
  if (root) add(root, 0);
  return rows;
}

/** The named values of a type. */
export function presetsOf(catalog: Catalog, type: string): CatalogPreset[] {
  return catalog.presets.filter((preset) => preset.type === type);
}

/** The functions that can fill a parameter of a type; in an event's arguments, only those allowed there. */
export function callsFor(catalog: Catalog, type: string, inEvent: boolean): CatalogFunction[] {
  if (type === "code") return catalog.functions.filter((each) => each.kind === "action");
  if (type === "boolexpr" || type === "boolcall") {
    return catalog.functions.filter((each) => each.kind === "condition");
  }
  if (type === "eventcall") return catalog.functions.filter((each) => each.kind === "event");
  return catalog.functions.filter(
    (each) => each.kind === "call" && each.returns === type && (!inEvent || each.inEvents),
  );
}

/** The map's variables a parameter of a type can name. */
export function variablesFor(variables: Variable[], type: string): Variable[] {
  if (type === "AnyGlobal") return variables;
  const wanted = type === "integervar" ? "integer" : type;
  return variables.filter((variable) => variable.type === wanted);
}

// Types whose value is typed in, or chosen from the object lists: the rest are things of the game.
const typed = new Set([
  "integer",
  "real",
  "string",
  "StringExt",
  "stringnoformat",
  "scriptcode",
  "imagefile",
  "modelfile",
  "anyfile",
  "preloadfile",
  "musicfile",
  "skymodelstring",
  "dncmodelstring",
  "animationname",
  "attachpoint",
  "unitcode",
  "itemcode",
  "abilcode",
  "heroskillcode",
  "buffcode",
  "destructablecode",
  "doodadcode",
  "techcode",
  "ordercode",
  "weathereffectcode",
  "timedlifebuffcode",
]);

/** True when a parameter of a type can hold a typed value; so can any type that offers nothing else. */
export function valueOffered(catalog: Catalog, type: string): boolean {
  return typed.has(type) || catalog.types.every((each) => each.key !== type);
}

/** The object editor's kind whose objects a type's value names; null for any other type. */
export function objectKindOf(type: string): Kind | null {
  switch (type) {
    case "unitcode":
      return "unit";
    case "itemcode":
      return "item";
    case "abilcode":
    case "heroskillcode":
      return "ability";
    case "buffcode":
      return "buff";
    case "destructablecode":
      return "destructable";
    case "doodadcode":
      return "doodad";
    case "techcode":
      return "upgrade";
    default:
      return null;
  }
}

/** What the map view can pick for a type; null for a type it has nothing for. */
export function pickKindOf(type: string): PickKind | null {
  switch (type) {
    case "unit":
      return "unit";
    case "item":
      return "item";
    case "destructable":
      return "destructable";
    case "rect":
      return "region";
    case "camerasetup":
      return "camera";
    case "location":
      return "point";
    default:
      return null;
  }
}

/** A kind's functions in a category ("" for all), whose title or category has `text`. */
export function functionsOf(
  catalog: Catalog,
  kind: FunctionKind,
  category: string,
  text: string,
): CatalogFunction[] {
  const wanted = text.trim().toLowerCase();
  return catalog.functions.filter(
    (each) =>
      each.kind === kind &&
      (category === "" || each.category === category) &&
      (wanted === "" || functionTitle(catalog, each).toLowerCase().includes(wanted)),
  );
}

/** A function's name in a list: `Unit - Create Units Facing Angle`. */
export function functionTitle(catalog: Catalog, function_: CatalogFunction): string {
  const category = catalog.categories.find((each) => each.key === function_.category);
  return category?.shown ? `${category.title} - ${function_.title}` : function_.title;
}

function inFunction(function_: FunctionView, path: string): ParameterView | null {
  for (const part of function_.sentence) {
    const found = part.parameter && inParameter(part.parameter, path);
    if (found) return found;
  }
  for (const block of function_.blocks) {
    for (const child of block.functions) {
      const found = inFunction(child, path);
      if (found) return found;
    }
  }
  return null;
}

function inParameter(parameter: ParameterView, path: string): ParameterView | null {
  if (parameter.path === path) return parameter;
  return (
    (parameter.function && inFunction(parameter.function, path)) ||
    (parameter.index && inParameter(parameter.index, path))
  );
}

/** The parameter at a path of a trigger; null when it has none there. */
export function findParameter(trigger: TriggerView, path: string): ParameterView | null {
  for (const function_ of [...trigger.events, ...trigger.conditions, ...trigger.actions]) {
    const found = inFunction(function_, path);
    if (found) return found;
  }
  return null;
}
