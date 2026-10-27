/**
 * Finds objects by name or id in the trees the page has loaded, for the pickers that add an object to a list. The page
 * hands over its trees once (`useTrees`); a kind whose tree is not loaded yet finds nothing.
 */
import type { Kind, TreeEntry } from "./api.ts";
import { entryMatches, isCustom } from "./tree.ts";

/** One object a picker can add: its kind, id and name, and where it sits in the tree. */
export type FoundObject = { kind: Kind; id: string; name: string; detail: string };

let trees: (kind: Kind) => TreeEntry[] | undefined = () => undefined;

/** Tells the finder where the page's trees are. */
export function useTrees(get: (kind: Kind) => TreeEntry[] | undefined): void {
  trees = get;
}

/**
 * The objects of `kinds` whose id, name or suffix holds `query` (ignoring case), best first: the exact id, then names
 * that start with it, then the rest; custom objects before standard ones, then in tree order. At most `limit`.
 */
export function findObjects(kinds: Kind[], query: string, limit: number): FoundObject[] {
  const needle = query.trim().toLowerCase();
  if (needle === "") return [];
  const found: { rank: number; at: number; object: FoundObject }[] = [];
  for (const kind of kinds) {
    for (const entry of trees(kind) ?? []) {
      if (!entryMatches(entry, needle)) continue;
      const rank =
        entry.id.toLowerCase() === needle ? 0 : entry.name.toLowerCase().startsWith(needle) ? 1 : 2;
      found.push({
        rank: rank * 2 + (isCustom(entry) ? 0 : 1),
        at: found.length,
        object: {
          kind,
          id: entry.id,
          name: entry.name,
          detail: isCustom(entry) ? "Custom" : entry.path.join(" · "),
        },
      });
    }
  }
  found.sort((a, b) => a.rank - b.rank || a.at - b.at);
  return found.slice(0, limit).map((each) => each.object);
}
