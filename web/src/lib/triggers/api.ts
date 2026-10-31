/**
 * The trigger editor's commands and the types of what they answer, which follow the C# records (camelCase):
 * `TriggerCatalog`, `TriggerDocument`'s views and `Commands.Triggers.cs` in the app.
 */
import { call } from "../bridge.ts";

export type SentencePart = { text: string; argument: number };

export type CatalogCategory = { key: string; title: string; icon: string; shown: boolean };

export type CatalogType = {
  key: string;
  title: string;
  global: boolean;
  comparable: boolean;
  base: string | null;
  default: string | null;
  defaultText: string | null;
};

export type CatalogPreset = {
  key: string;
  type: string;
  text: string;
  code: string;
  version: number;
};

export type FunctionKind = "event" | "condition" | "action" | "call";

export type CatalogFunction = {
  name: string;
  kind: FunctionKind;
  category: string;
  title: string;
  sentence: SentencePart[];
  arguments: string[];
  defaults: string[];
  returns: string | null;
  inEvents: boolean;
  version: number;
  hint: string;
};

/** The game's trigger functions. */
export type Catalog = {
  categories: CatalogCategory[];
  types: CatalogType[];
  presets: CatalogPreset[];
  functions: CatalogFunction[];
};

export type TreeKind =
  | "root"
  | "category"
  | "trigger"
  | "custom"
  | "script"
  | "comment"
  | "variable";

export type TreeItem = {
  id: number;
  parent: number;
  kind: TreeKind;
  name: string;
  enabled: boolean;
  expanded: boolean;
  /** The name a parameter holds for a trigger (`gg_trg_Wave_1`); "" for the other kinds. */
  ref: string;
};

export type Variable = {
  id: number;
  name: string;
  type: string;
  isArray: boolean;
  size: number;
  initial: string;
};

/** A region, camera or sound of the map that a trigger can name. */
export type MapReference = { type: string; ref: string; text: string };

export type TriggerTree = {
  items: TreeItem[];
  variables: Variable[];
  readOnly: boolean;
  problem: string | null;
  headerComment: string;
  headerCode: string;
  references: MapReference[];
};

export type ParameterKind = "preset" | "variable" | "function" | "value" | "none";

export type ParameterView = {
  path: string;
  type: string;
  name: string;
  kind: ParameterKind;
  value: string;
  text: string;
  /** A typed value as its box starts: not rounded, a strings-file text as its text. */
  typed: string;
  function: FunctionView | null;
  index: ParameterView | null;
};

export type TextPart = { text: string; parameter: ParameterView | null };

export type BlockView = {
  branch: number;
  title: string;
  kind: "condition" | "action";
  functions: FunctionView[];
};

export type FunctionView = {
  path: string;
  name: string;
  kind: FunctionKind;
  category: string;
  enabled: boolean;
  /** The block it is in; null in a trigger's own list. */
  branch: number | null;
  prefix: string;
  sentence: TextPart[];
  blocks: BlockView[];
};

export type TriggerView = {
  id: number;
  kind: TreeKind;
  name: string;
  description: string;
  enabled: boolean;
  initiallyOn: boolean;
  runOnInit: boolean;
  events: FunctionView[];
  conditions: FunctionView[];
  actions: FunctionView[];
  code: string | null;
};

/** The fields of a new or changed variable. */
export type VariableInput = {
  name: string;
  type: string;
  isArray: boolean;
  size: number;
  initial: string;
};

export function triggerCatalog(): Promise<Catalog> {
  return call<Catalog>("triggerCatalog");
}

export function triggerTree(): Promise<TriggerTree> {
  return call<TriggerTree>("triggerTree");
}

export function trigger(id: number): Promise<TriggerView> {
  return call<TriggerView>("trigger", { id });
}

/** Adds an item to a category; its id. */
export async function addItem(kind: TreeKind, parent: number): Promise<number> {
  return (await call<{ id: number }>("triggerAdd", { kind, parent })).id;
}

export async function renameItem(id: number, name: string): Promise<void> {
  await call<null>("triggerRename", { id, name });
}

export type TriggerOptions = {
  enabled?: boolean;
  initiallyOn?: boolean;
  runOnInit?: boolean;
  description?: string;
};

export async function setOptions(id: number, options: TriggerOptions): Promise<void> {
  await call<null>("triggerOptions", { id, ...options });
}

export async function setCode(id: number, code: string): Promise<void> {
  await call<null>("triggerCode", { id, code });
}

export async function setHeader(comment: string, code: string): Promise<void> {
  await call<null>("triggerHeader", { comment, code });
}

export async function deleteItem(id: number): Promise<void> {
  await call<null>("triggerDelete", { id });
}

export async function moveItem(id: number, parent: number, index: number): Promise<void> {
  await call<null>("triggerMove", { id, parent, index });
}

/** Adds a variable to a category; its id. */
export async function addVariable(variable: VariableInput, parent: number): Promise<number> {
  return (await call<{ id: number }>("triggerVariableAdd", { ...variable, parent })).id;
}

export async function setVariable(id: number, variable: VariableInput): Promise<void> {
  await call<null>("triggerVariableSet", { id, ...variable });
}

/** Where a new or copied function goes: a trigger's list, or a block of the function at `parent`. */
export type FunctionPlace = { parent: string | null; branch: number; index: number };

export async function addFunction(
  id: number,
  place: FunctionPlace,
  kind: FunctionKind,
  name: string,
): Promise<string> {
  return (await call<{ path: string }>("triggerFunctionAdd", { id, ...place, kind, name })).path;
}

/** Keeps a copy of a function in the app, for `pasteFunction`. */
export async function copyFunction(id: number, path: string): Promise<void> {
  await call<null>("triggerFunctionCopy", { id, path });
}

export async function pasteFunction(id: number, place: FunctionPlace): Promise<string> {
  return (await call<{ path: string }>("triggerFunctionPaste", { id, ...place })).path;
}

export async function changeFunction(id: number, path: string, name: string): Promise<void> {
  await call<null>("triggerFunctionChange", { id, path, name });
}

export async function deleteFunction(id: number, path: string): Promise<void> {
  await call<null>("triggerFunctionDelete", { id, path });
}

export async function enableFunction(id: number, path: string, enabled: boolean): Promise<void> {
  await call<null>("triggerFunctionEnable", { id, path, enabled });
}

export async function moveFunction(id: number, path: string, index: number): Promise<string> {
  return (await call<{ path: string }>("triggerFunctionMove", { id, path, index })).path;
}

export async function setParameter(
  id: number,
  path: string,
  kind: ParameterKind,
  value: string,
): Promise<void> {
  await call<null>("triggerParameter", { id, path, kind, value });
}
