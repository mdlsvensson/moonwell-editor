/**
 * The rows of a kind's tree: the map's custom objects first, then the standard objects grouped by their path. The
 * tree pane draws only the rows in view, so it works from this flat list.
 */
import type { Kind, TreeEntry } from "./api.ts";

/** One drawn row: a heading, a group that opens and closes (depth -1 for Custom), or an object. */
export type Line =
  | { type: "heading"; key: string; text: string }
  | { type: "group"; key: string; name: string; depth: number; open: boolean; count: number }
  | { type: "entry"; key: string; entry: TreeEntry; depth: number };

type Group = {
  name: string;
  key: string;
  groups: Map<string, Group>;
  entries: TreeEntry[];
  count: number;
};

// The data names races in lower case (`nightelf`); the tree shows them as World Editor does.
const groupNames: Record<string, string> = { nightelf: "Night Elf" };

/** A group's name to show: `nightelf` as Night Elf, any other with a capital letter. */
export function groupName(name: string): string {
  return groupNames[name] ?? name.charAt(0).toUpperCase() + name.slice(1);
}

/** True for one of the map's custom objects (its path is `["Custom"]`). */
export function isCustom(entry: TreeEntry): boolean {
  return entry.path.length === 1 && entry.path[0] === "Custom";
}

/** The key of the Custom group of a kind. */
export function customKey(kind: Kind): string {
  return `${kind}:Custom`;
}

/** The keys of the groups an object is in, broadest first: what must be open for it to show. */
export function groupKeys(kind: Kind, entry: TreeEntry): string[] {
  if (isCustom(entry)) return [customKey(kind)];
  return entry.path.map((_, depth) => `${kind}:${entry.path.slice(0, depth + 1).join("/")}`);
}

/** The kinds' names, as the kind buttons show them. */
export const kindNames: Record<Kind, string> = {
  unit: "Units",
  item: "Items",
  destructable: "Destructables",
  doodad: "Doodads",
  ability: "Abilities",
  buff: "Buffs",
  upgrade: "Upgrades",
};

/** True when an object's id, name or suffix holds `filter`, ignoring case; an empty filter keeps every object. */
export function entryMatches(entry: TreeEntry, filter: string): boolean {
  const needle = filter.trim().toLowerCase();
  return needle === "" || matches(entry, needle);
}

function matches(entry: TreeEntry, needle: string): boolean {
  return (
    entry.id.toLowerCase().includes(needle) ||
    entry.name.toLowerCase().includes(needle) ||
    entry.suffix.toLowerCase().includes(needle)
  );
}

/**
 * The tree's rows. `filter` keeps the objects whose id, name or suffix holds it (ignoring case), and opens every group
 * while it is set; otherwise `isOpen` says which groups are open.
 */
export function treeLines(
  entries: TreeEntry[],
  kind: Kind,
  filter: string,
  isOpen: (key: string) => boolean,
): Line[] {
  const needle = filter.trim().toLowerCase();
  const shown = needle ? entries.filter((entry) => matches(entry, needle)) : entries;
  const open = (key: string) => needle !== "" || isOpen(key);
  const result: Line[] = [];

  const custom = shown.filter(isCustom);
  if (custom.length > 0 || !needle) {
    const key = customKey(kind);
    const customOpen = open(key);
    result.push({
      type: "group",
      key,
      name: "Custom · this map",
      depth: -1,
      open: customOpen,
      count: custom.length,
    });
    if (customOpen) {
      for (const entry of custom) {
        result.push({ type: "entry", key: `e:${entry.id}`, entry, depth: 0 });
      }
    }
  }

  const root: Group = { name: "", key: "", groups: new Map(), entries: [], count: 0 };
  for (const entry of shown) {
    if (isCustom(entry)) continue;
    const keys = groupKeys(kind, entry);
    let group = root;
    group.count++;
    entry.path.forEach((name, depth) => {
      let child = group.groups.get(name);
      if (!child) {
        child = { name, key: keys[depth] ?? "", groups: new Map(), entries: [], count: 0 };
        group.groups.set(name, child);
      }
      child.count++;
      group = child;
    });
    group.entries.push(entry);
  }
  if (root.count > 0) result.push({ type: "heading", key: "h:standard", text: "Standard" });

  const walk = (group: Group, depth: number) => {
    for (const child of group.groups.values()) {
      const childOpen = open(child.key);
      result.push({
        type: "group",
        key: child.key,
        name: child.name,
        depth,
        open: childOpen,
        count: child.count,
      });
      if (childOpen) walk(child, depth + 1);
    }
    for (const entry of group.entries) {
      result.push({ type: "entry", key: `e:${entry.id}`, entry, depth });
    }
  };
  walk(root, 0);
  return result;
}

/**
 * The object `by` rows of objects below (1) or above (-1) `selected` among the drawn rows, skipping groups; the first
 * object when nothing is selected or the selection is not drawn. Undefined past either end.
 */
export function nextObject(lines: Line[], selected: string | null, by: 1 | -1): string | undefined {
  const objects = lines.filter((line) => line.type === "entry");
  const at = objects.findIndex((line) => line.entry.id === selected);
  const next = at < 0 ? objects[0] : objects[at + by];
  return next?.entry.id;
}
