/**
 * The app's commands for the open map, one typed function each, and the types of what they answer. The types follow
 * the C# records (camelCase): `Session`'s views in MoonwellEditor.Model and the records in MoonwellEditor.App's Commands.
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
  | "badValue"
  | "idTaken"
  | "idInvalid"
  | "readOnly"
  | "badText"
  | "changedOnDisk"
  | "mapFile"
  | "noMap"
  | "unsaved"
  | "unknownCommand"
  | "badRequest"
  | "failed"
  | "gameDataMissing"
  | "gameDataLoading"
  | "gameDataFailed";

/** The map that was opened or reloaded. */
export type MapInfo = {
  /** The map folder's name, or the archive's file name (`Arena.w3x`). */
  name: string;
  /** The map folder's or the archive file's full path. */
  path: string;
  /** True for an archive (`.w3x`, `.w3m`), false for a folder. */
  archive: boolean;
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

/**
 * What a save did: the files written, the backup folder ("" when none: a folder map's previous bytes of the files, an
 * archive's whole previous archive), and a note ("" for none): for an archive saved although replacing the old one
 * reported an error, that error.
 */
export type SaveReport = { written: string[]; backup: string; note: string };

/** One search result: an object, or (with `field` set) a field of a kind. */
export type Hit = { kind: Kind; id: string; name: string; detail: string; field: string | null };

/** One value of a choice list. */
export type ChoiceEntry = {
  value: string;
  label: string;
  /** The other spellings the game's data uses for the value (`aliv` for `alive`); left out or empty when none. */
  alternates?: string[];
};

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

/** One field of a kind, for choosing the table's columns (the kind's fields come in World Editor's order). */
export type FieldInfo = {
  id: string;
  label: string;
  category: string;
  sort: string;
  /** The kind of value, as the model names it: `int`, `bool`, `string`, `choice` ... */
  type: string;
  /** True when the field has a value for each level (each variation for a doodad). */
  perLevel: boolean;
  /** For a choice field, the key of its list in `choices()`; null for other types. */
  choice: string | null;
  /** For an `objectList` or `objectRef` field, what it holds; null for other types. */
  listOf: string | null;
};

/** One column of the table: a field, at a level (from 1) for a per-level field, else 0. */
export type Column = { field: string; level: number };

/** One cell of the table: what an object shows for the column's field at its level. */
export type TableCell = {
  /** The value shown; "" when the cell does not apply. */
  value: string;
  /** The standard value; "" when there is none or the cell does not apply. */
  base: string;
  /** True when the map has a modification of the field at this level. */
  changed: boolean;
  /** False when the field does not belong to the object, or the level is past its level count. */
  applies: boolean;
  /** What is wrong with the map's value by World Editor's limits; null when nothing is. */
  warning: string | null;
};

/** One object's row of the table: one cell for each column, in the order of the columns. */
export type TableRow = {
  id: string;
  name: string;
  /** For a custom object, the standard object it is based on; null for a standard object. */
  baseId: string | null;
  /** Why the object cannot be edited; null when it can. */
  readOnly: string | null;
  cells: TableCell[];
};

/** One cell to set: a field of an object at a level; a null `value` resets it to its standard value. */
export type CellEdit = { id: string; field: string; level: number; value: string | null };

/** The open map and where it stands, for a page that was loaded again. */
export type CurrentMap = { map: MapInfo; session: SessionState };

/** What the app was started with: `map` is the folder or archive from its command line, given once (null after). */
export type Startup = { map: string | null };

/** Asks what the app was started with; the page opens `map` once the game data is ready. */
export function startup(): Promise<Startup> {
  return call<Startup>("startup");
}

/** What `openMap` opens: a path, or what the user picks in a dialog. */
export type OpenMapOptions = {
  /** A map folder or an archive file (`.w3x`, `.w3m`); left out, the user picks one. */
  path?: string;
  /** With no path: true asks for an archive in the Open File dialog, false (the default) for a folder. */
  archive?: boolean;
  /** Lets the app drop unsaved edits: pass it only once the user chose Discard (or saved). */
  discard?: boolean;
};

/**
 * Opens a map, a folder or an archive: `path`, or the one the user picks in the app's dialog (null when they cancel).
 * A map that cannot be opened (none there, an archive War3Net cannot read) fails with `failed` and the reason. While
 * the open map has unsaved edits it fails with `unsaved`, unless `discard` is true.
 */
export function openMap(options: OpenMapOptions = {}): Promise<MapInfo | null> {
  const { path, archive = false, discard = false } = options;
  return call<MapInfo | null>("openMap", {
    ...(path === undefined ? {} : { path }),
    ...(archive ? { archive } : {}),
    ...(discard ? { discard } : {}),
  });
}

/** The map that is open in the app and where it stands, or null: a page loaded again shows it. */
export function currentMap(): Promise<CurrentMap | null> {
  return call<CurrentMap | null>("currentMap");
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

/** The id `createObject` would give a new object based on `baseId` when given none (nothing is made). */
export function nextId(kind: Kind, baseId: string): Promise<string> {
  return call<string>("nextId", { kind, baseId });
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

/**
 * Saves the edits. Fails with `changedOnDisk` when the map changed on disk since it was read, and with `mapFile` when
 * it could not be read or written (the message says why; the edits stay unsaved).
 */
export function save(): Promise<SaveReport> {
  return call<SaveReport>("save");
}

/**
 * Reads the map again from disk, dropping the edits that were not saved. With unsaved edits it fails with `unsaved`,
 * unless `discard` is true: pass it only once the user chose Discard. A map that cannot be read fails with `mapFile`.
 */
export function reload(discard = false): Promise<MapInfo> {
  return call<MapInfo>("reload", discard ? { discard } : {});
}

/** Searches the objects of every kind and the kinds' fields; at most `limit` hits, best first. */
export function search(query: string, limit: number): Promise<Hit[]> {
  return call<Hit[]>("search", { query, limit });
}

/** Every choice list, by the key a `FieldView`'s `choice` names. */
export function choices(): Promise<Record<string, ChoiceList>> {
  return call<Record<string, ChoiceList>>("choices");
}

/** A kind's fields, by category and then World Editor's sort order. */
export function fields(kind: Kind): Promise<FieldInfo[]> {
  return call<FieldInfo[]>("fields", { kind });
}

/**
 * The table's rows: one for each object of `ids` that the kind has (an unknown id is left out), in the order of
 * `ids`, with one cell for each column.
 */
export function table(kind: Kind, ids: string[], columns: Column[]): Promise<TableRow[]> {
  return call<TableRow[]>("table", { kind, ids, columns });
}

/**
 * Sets (or, with a null `value`, resets) many cells as one edit: all or nothing, and one undo takes them all back.
 * Answers with `{ changed }`, how many cells changed; the page fetches its rows again with `table`. When a cell is refused the
 * error's message starts with the cell, and nothing changes.
 */
export function setCells(kind: Kind, edits: CellEdit[]): Promise<{ changed: number }> {
  return call<{ changed: number }>("setCells", { kind, edits });
}

/**
 * Listens to `sessionChanged`, which the app sends after a map is opened or reloaded and after every edit, undo,
 * redo and save. Returns a function that stops listening.
 */
export function onSessionChanged(handler: (state: SessionState) => void): () => void {
  return on("sessionChanged", (payload) => handler(payload as SessionState));
}

/** What `closeRequested` carries: whether the window saw the map with unsaved edits. */
export type CloseRequest = { dirty: boolean };

/**
 * Listens to `closeRequested`, which the app sends when the user closes the window while the page is loaded. The
 * window stays open; the page answers with `closeAsking` at once, then `closeWindow` at once when nothing is typed or
 * unsaved, else once the user has saved or chosen to discard the edits. Returns a function that stops listening.
 */
export function onCloseRequested(handler: (request: CloseRequest) => void): () => void {
  return on("closeRequested", (payload) =>
    handler({ dirty: (payload as Partial<CloseRequest> | null)?.dirty === true }),
  );
}

/** Tells the app the page is asking the user about the unsaved edits (so the window does not ask on its own). */
export async function closeAsking(): Promise<void> {
  await call<null>("closeAsking");
}

/** Closes the window, unsaved edits or not. */
export async function closeWindow(): Promise<void> {
  await call<null>("closeWindow");
}
