/** The game's icons and models for the pickers, and their search. */
import { type AssetList, assetList } from "./api.ts";

// Asked for once each (again while the install is still opening, or after a failure).
const lists: Partial<Record<"icon" | "model", Promise<AssetList>>> = {};

/** The game's icons or models. */
export function listOf(kind: "icon" | "model"): Promise<AssetList> {
  const known = lists[kind];
  if (known) return known;
  const asked = assetList(kind).then((list) => {
    if (!list.ready) delete lists[kind];
    return list;
  });
  asked.catch(() => delete lists[kind]);
  lists[kind] = asked;
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
