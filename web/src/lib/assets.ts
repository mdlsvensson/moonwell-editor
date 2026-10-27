/** The game's icons and models for the pickers, and their search. */
import { type AssetList, assetList } from "./api.ts";

// Asked for once each per graphics mode (again while the install is still opening, or after a failure).
const lists = new Map<string, Promise<AssetList>>();

/** The game's icons or models in a graphics mode (its own tier's too). */
export function listOf(kind: "icon" | "model", mode: string): Promise<AssetList> {
  const key = `${kind}|${mode}`;
  const known = lists.get(key);
  if (known) return known;
  const asked = assetList(kind, mode).then((list) => {
    if (!list.ready) lists.delete(key);
    return list;
  });
  asked.catch(() => lists.delete(key));
  lists.set(key, asked);
  return asked;
}

/** The paths that hold every word of the query, ignoring case and either slash; at most `limit`, in order. */
export function matching(paths: string[], query: string, limit: number): string[] {
  const words = query.toLowerCase().replaceAll("/", "\\").split(/\s+/).filter(Boolean);
  const found: string[] = [];
  for (const path of paths) {
    const lower = path.toLowerCase();
    if (words.every((word) => lower.includes(word))) {
      found.push(path);
      if (found.length >= limit) break;
    }
  }
  return found;
}

/**
 * The paths that hold every word of the query (ignoring case and either slash), best first, at most `limit`: those
 * whose file name holds every word before those that hold one only in their folders, then by `order` (lower first:
 * the folders or files the field is likely to want; Infinity for none), then as listed. Nothing is left out for not
 * being preferred (Sven, 2026-10-27: any model on any model field).
 */
export function ranked(
  paths: string[],
  query: string,
  limit: number,
  order: (path: string) => number,
): string[] {
  const words = query.toLowerCase().replaceAll("/", "\\").split(/\s+/).filter(Boolean);
  const found: { path: string; inName: boolean; rank: number; at: number }[] = [];
  paths.forEach((path, at) => {
    const lower = path.toLowerCase();
    if (!words.every((word) => lower.includes(word))) return;
    const name = lower.slice(lower.lastIndexOf("\\") + 1);
    found.push({ path, inName: words.every((word) => name.includes(word)), rank: order(path), at });
  });
  found.sort(
    (a, b) =>
      Number(b.inName) - Number(a.inName) ||
      (a.rank === b.rank ? 0 : a.rank < b.rank ? -1 : 1) ||
      a.at - b.at,
  );
  return found.slice(0, limit).map((each) => each.path);
}

/**
 * The folders a model field's picker shows first, by the object's kind and the field (a unit's model in Units\, its
 * projectile in Abilities\Weapons\, an item's in Objects\InventoryItems\, a doodad's in Doodads\ ...).
 */
// A folder as object data writes it: back slashes.
const slashes = (folder: string) => folder.replaceAll("/", "\\");

export function modelFolders(objectKind: string, fieldId: string): string[] {
  const missile = ["ua1m", "ua2m", "amat", "fmat"].includes(fieldId);
  if (missile) return [slashes("Abilities/Weapons/")];
  switch (objectKind) {
    case "unit":
      return fieldId === "uspa"
        ? ["Objects/Spawnmodels/", "Units/"].map(slashes)
        : [slashes("Units/")];
    case "item":
      return [slashes("Objects/InventoryItems/")];
    case "destructable":
    case "doodad":
      return ["Doodads/", "Objects/"].map(slashes);
    default:
      return ["Abilities/Spells/", "Abilities/"].map(slashes);
  }
}

/** The order `ranked` takes for a list of preferred folders (by the first that holds the path). */
export function inFolders(folders: string[]): (path: string) => number {
  const lower = folders.map((folder) => folder.toLowerCase());
  return (path) => {
    const at = lower.findIndex((folder) => path.toLowerCase().startsWith(folder));
    return at < 0 ? Number.POSITIVE_INFINITY : at;
  };
}

/** The order `ranked` takes for a list of preferred files (the icons the kind's objects use), in their order. */
export function inList(files: string[]): (path: string) => number {
  const places = new Map<string, number>();
  files.forEach((file, at) => {
    const key = file.toLowerCase().replaceAll("/", "\\");
    if (!places.has(key)) places.set(key, at);
  });
  return (path) => places.get(path.toLowerCase()) ?? Number.POSITIVE_INFINITY;
}
