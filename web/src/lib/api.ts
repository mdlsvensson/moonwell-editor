/**
 * The app's commands for the open map, one typed function each, and the types of what they answer. The types follow
 * the C# records (camelCase): `Session`'s views in ObjectEditor.Model and the records in ObjectEditor.App's Commands.
 */
import { call, on } from "./bridge.ts";

/** The seven kinds of object, by the key the app uses, in the Object Editor's tab order. */
export const kinds = [
  "unit",
  "item",
  "destructable",
  "doodad",
  "ability",
  "buff",
  "upgrade",
] as const;

/** A kind of object, by its key. */
export type Kind = (typeof kinds)[number];

/** Every error code the app answers with (a `BridgeError`'s `code`). */
export type ErrorCode =
  | "noObject"
  | "noField"
  | "notNumber"
  | "idTaken"
  | "idInvalid"
  | "readOnly"
  | "badText"
  | "changedOnDisk"
  | "noMap"
  | "unknownCommand"
  | "badRequest"
  | "failed"
  | "gameDataMissing"
  | "gameDataLoading"
  | "gameDataFailed";

/** The map that was opened or reloaded. */
export type MapInfo = {
  /** The map folder's name. */
  name: string;
  /** The map folder's full path. */
  path: string;
  /** What is wrong with its files: a kind whose file is a problem is read only. */
  problems: string[];
  /** How many custom objects it has, by kind. */
  counts: Record<Kind, number>;
};

/** One object in a kind's tree. */
export type TreeEntry = {
  id: string;
  /** For a custom object, the standard object it is based on; null for a standard object. */
  baseId: string | null;
  name: string;
  /** World Editor's suffix, such as `(Evil)`; "" when it has none. */
  suffix: string;
  /** Where it sits in the tree: `["Custom"]`, or such as `["Human", "Melee", "Units"]`. */
  path: string[];
  /** True when the map has a modification of the object. */
  changed: boolean;
};

/** One link of an object's chain; `id` is "" for a link that is not an object (the kind, a hero, a building). */
export type ChainLink = { name: string; id: string };

/** One field of one object at one level, with its value. */
export type FieldView = {
  id: string;
  label: string;
  category: string;
  sort: string;
  /** The kind of value, as the model names it: `int`, `bool`, `string`, `choice` ... */
  type: string;
  /** For a choice field, the key of its list in `choices()`; null for other types. */
  choice: string | null;
  /**
   * For an `objectList` or `objectRef` field, what it holds: `Ability`, `Unit`, `Item`, `Upgrade`, `Buff`, or
   * `object` for units and upgrades together; null for other types.
   */
  listOf: string | null;
  /** The level (a doodad's variation), from 1, for a per-level field; 0 for any other. */
  level: number;
  value: string;
  /** The standard value; "" when there is none. */
  base: string;
  /** True when the map changes the field at this level. */
  changed: boolean;
  /** True when the field is kept in the skin file. */
  skin: boolean;
  /** What is wrong with the map's value by World Editor's limits; null when nothing is. */
  warning: string | null;
  /** The standard values per graphics mode (`sd`, `hd`, `de`) when they differ; null when they do not. */
  modes: Record<string, string> | null;
};

/** One object with every field that applies to it. */
export type ObjectView = {
  kind: Kind;
  id: string;
  baseId: string | null;
  name: string;
  chain: ChainLink[];
  /** The number of levels (variations for a doodad); 0 for kinds without levels. */
  levels: number;
  /** Why the object cannot be edited; null when it can. */
  readOnly: string | null;
  fields: FieldView[];
};

/** What a save did: the files written, and the backup folder of their previous bytes ("" when none). */
export type SaveReport = { written: string[]; backup: string };

/** One search result: an object, or (with `field` set) a field of a kind. */
export type Hit = { kind: Kind; id: string; name: string; detail: string; field: string | null };

/** One value of a choice list. */
export type ChoiceEntry = { value: string; label: string };

/** The values World Editor offers for one type of field. */
export type ChoiceList = {
  /** True when a field holds any of the values (a comma list or bits), false for one. */
  flags: boolean;
  /** How an object file stores it: `int` or `string`. */
  store: string;
  entries: ChoiceEntry[];
};

/** Where the open map stands: unsaved edits, and how many objects differ from the last save. */
export type SessionState = { dirty: boolean; changedObjects: number };

/** One field of one object at one level, for an edit. */
export type FieldRef = { kind: Kind; id: string; field: string; level: number };

/** What the app was started with: `map` is the folder from its command line, given once (null after). */
export type Startup = { map: string | null };

/** Asks what the app was started with; the page opens `map` once the game data is ready. */
export function startup(): Promise<Startup> {
  return call<Startup>("startup");
}

/** Opens a map folder: `path`, or the one the user picks in the app's dialog (null when they cancel). */
export function openMap(path?: string): Promise<MapInfo | null> {
  return call<MapInfo | null>("openMap", path === undefined ? {} : { path });
}

/** A kind's tree: the map's custom objects first, then every standard object. */
export function tree(kind: Kind): Promise<TreeEntry[]> {
  return call<TreeEntry[]>("tree", { kind });
}

/** An object with every field; also for a read-only kind (see `readOnly`). */
export function object(kind: Kind, id: string): Promise<ObjectView> {
  return call<ObjectView>("object", { kind, id });
}

/** Sets a field, typed as text, and answers with the object as it is now. */
export function setField(target: FieldRef, value: string): Promise<ObjectView> {
  const { kind, id, field, level } = target;
  return call<ObjectView>("setField", { kind, id, field, level, value });
}

/** Resets a field to its standard value, and answers with the object as it is now. */
export function resetField(target: FieldRef): Promise<ObjectView> {
  const { kind, id, field, level } = target;
  return call<ObjectView>("resetField", { kind, id, field, level });
}

/** Makes a custom object based on a standard one; answers with its id (the next free one when `id` is left out). */
export function createObject(kind: Kind, baseId: string, id?: string): Promise<string> {
  return call<string>("createObject", { kind, baseId, id });
}

/** Makes a custom object as a copy of another; answers with its id (the next free one when `id` is left out). */
export function copyObject(kind: Kind, fromId: string, id?: string): Promise<string> {
  return call<string>("copyObject", { kind, fromId, id });
}

/** Deletes a custom object. */
export async function deleteObject(kind: Kind, id: string): Promise<void> {
  await call<null>("deleteObject", { kind, id });
}

/** Undoes the last edit; false when there is none. */
export function undo(): Promise<boolean> {
  return call<boolean>("undo");
}

/** Makes the last undone edit again; false when there is none. */
export function redo(): Promise<boolean> {
  return call<boolean>("redo");
}

/** Saves the edits. Fails with `changedOnDisk` when the map changed on disk since it was read. */
export function save(): Promise<SaveReport> {
  return call<SaveReport>("save");
}

/** Reads the map again from disk, dropping the edits that were not saved. */
export function reload(): Promise<MapInfo> {
  return call<MapInfo>("reload");
}

/** Searches the objects of every kind and the kinds' fields; at most `limit` hits, best first. */
export function search(query: string, limit: number): Promise<Hit[]> {
  return call<Hit[]>("search", { query, limit });
}

/** Every choice list, by the key a `FieldView`'s `choice` names. */
export function choices(): Promise<Record<string, ChoiceList>> {
  return call<Record<string, ChoiceList>>("choices");
}

/**
 * Listens to `sessionChanged`, which the app sends after a map is opened or reloaded and after every edit, undo,
 * redo and save. Returns a function that stops listening.
 */
export function onSessionChanged(handler: (state: SessionState) => void): () => void {
  return on("sessionChanged", (payload) => handler(payload as SessionState));
}
