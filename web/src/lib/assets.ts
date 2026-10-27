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
