/**
 * The rows of a kind's tree: the standard objects grouped by their path, then the map's custom objects, grouped by
 * theirs in the same folders (Sven, 2026-10-28). The tree pane draws only the rows in view, so it works from this flat
 * list.
 */
import type { Kind, TreeEntry } from "./api.ts";
import { t } from "./text.ts";

/**
 * One drawn row: a group that opens and closes (depth -1 for the Custom and Standard headings, which open and close
 * like any group), or an object.
 */
export type Line =
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
const groupNames: Record<string, string> = {
  human: t("app.folder.human"),
  orc: t("app.folder.orc"),
  undead: t("app.folder.undead"),
  nightelf: t("app.folder.nightElf"),
};

/** A group's name to show: `nightelf` as Night Elf, any other with a capital letter. */
export function groupName(name: string): string {
  return groupNames[name] ?? name.charAt(0).toUpperCase() + name.slice(1);
}

/** True for one of the map's custom objects (its path starts with `Custom`, then its folders). */
export function isCustom(entry: TreeEntry): boolean {
  return entry.path[0] === t("app.folder.custom");
}

/** The key of the Custom group of a kind. */
export function customKey(kind: Kind): string {
  return `${kind}:Custom`;
}

/** The key of the Standard heading of a kind. */
export function standardKey(kind: Kind): string {
  return `${kind}:Standard`;
}

/** The keys of the groups an object is in, broadest first (Standard, then its path): what must be open for it to show. */
export function groupKeys(kind: Kind, entry: TreeEntry): string[] {
  const keys = entry.path.map((_, depth) => `${kind}:${entry.path.slice(0, depth + 1).join("/")}`);
  // A custom object's path starts with Custom, whose key is the Custom heading's.
  return isCustom(entry) ? keys : [standardKey(kind), ...keys];
}

/** The kinds' names, as the kind buttons show them. */
export const kindNames: Record<Kind, string> = {
  unit: t("kind.unit"),
  item: t("kind.item"),
  destructable: t("kind.destructable"),
  doodad: t("kind.doodad"),
  ability: t("kind.ability"),
  buff: t("kind.buff"),
  upgrade: t("kind.upgrade"),
};

/** Each kind's colour, as a text class: its icon and its objects' names show in it. */
export const kindText: Record<Kind, string> = {
  unit: "text-kind-unit",
  item: "text-kind-item",
  destructable: "text-kind-destructable",
  doodad: "text-kind-doodad",
  ability: "text-kind-ability",
  buff: "text-kind-buff",
  upgrade: "text-kind-upgrade",
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

  // The objects grouped by their folders: the standard ones by their path, the custom ones by theirs after Custom.
  const grow = (from: TreeEntry[], custom: boolean): Group => {
    const root: Group = { name: "", key: "", groups: new Map(), entries: [], count: 0 };
    for (const entry of from) {
      const keys = groupKeys(kind, entry);
      const path = custom ? entry.path.slice(1) : entry.path;
      let group = root;
      group.count++;
      path.forEach((name, depth) => {
        let child = group.groups.get(name);
        if (!child) {
          child = { name, key: keys[depth + 1] ?? "", groups: new Map(), entries: [], count: 0 };
          group.groups.set(name, child);
        }
        child.count++;
        group = child;
      });
      group.entries.push(entry);
    }
    return root;
  };
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

  // Standard opens and closes like Custom (Sven, 2026-10-27).
  const standard = grow(
    shown.filter((entry) => !isCustom(entry)),
    false,
  );
  const standardOpen = open(standardKey(kind));
  if (standard.count > 0) {
    result.push({
      type: "group",
      key: standardKey(kind),
      name: t("tree.standard"),
      depth: -1,
      open: standardOpen,
      count: standard.count,
    });
  }
  if (standardOpen) walk(standard, 0);

  // The Custom group comes after Standard, as World Editor's Custom Units come after its Standard Units, and shows
  // only while the kind has custom objects (Sven, 2026-10-27).
  const custom = grow(shown.filter(isCustom), true);
  if (custom.count > 0) {
    const key = customKey(kind);
    const customOpen = open(key);
    result.push({
      type: "group",
      key,
      name: t("tree.custom"),
      depth: -1,
      open: customOpen,
      count: custom.count,
    });
    if (customOpen) walk(custom, 0);
  }
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
