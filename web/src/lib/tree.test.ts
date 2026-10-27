import { describe, expect, it } from "vitest";
import type { TreeEntry } from "./api.ts";
import { t } from "./text.ts";
import { groupKeys, groupName, type Line, nextObject, treeLines } from "./tree.ts";

function entry(id: string, path: string[], name = id): TreeEntry {
  return {
    id,
    baseId: path[0] === "Custom" ? "hfoo" : null,
    name,
    suffix: "",
    path,
    changed: false,
  };
}

const entries = [
  entry("hfoo", ["human", "Melee", "Units"], "Footman"),
  entry("h000", ["Custom"], "Spearman"),
  entry("hkni", ["human", "Melee", "Units"], "Knight"),
  entry("Hpal", ["human", "Melee", "Heroes"], "Paladin"),
  entry("ogru", ["orc", "Melee", "Units"], "Grunt"),
  entry("h001", ["Custom"], "Champion"),
];

/** Each row as a short text: `[+]`/`[-]` for an open/closed group, the id for an object. */
function shape(lines: Line[]): string[] {
  return lines.map((line) => {
    if (line.type === "group") return `${line.open ? "[-]" : "[+]"} ${line.name} ${line.count}`;
    return line.entry.id;
  });
}

const allOpen = () => true;

describe("the tree's rows", () => {
  it("put the standard objects grouped by path first, then the custom ones, as World Editor does", () => {
    expect(shape(treeLines(entries, "unit", "", allOpen))).toEqual([
      `[-] ${t("tree.standard")} 4`,
      "[-] human 3",
      "[-] Melee 3",
      "[-] Units 2",
      "hfoo",
      "hkni",
      "[-] Heroes 1",
      "Hpal",
      "[-] orc 1",
      "[-] Melee 1",
      "[-] Units 1",
      "ogru",
      `[-] ${t("tree.custom")} 2`,
      "h000",
      "h001",
    ]);
  });

  it("leave out what is inside a closed group", () => {
    const open = (key: string) => ["unit:Custom", "unit:Standard", "unit:human"].includes(key);
    expect(shape(treeLines(entries, "unit", "", open))).toEqual([
      `[-] ${t("tree.standard")} 4`,
      "[-] human 3",
      "[+] Melee 3",
      "[+] orc 1",
      `[-] ${t("tree.custom")} 2`,
      "h000",
      "h001",
    ]);
  });

  it("have no Custom group while the kind has no custom objects", () => {
    const standard = entries.filter((entry) => entry.path[0] !== "Custom");
    expect(shape(treeLines(standard, "unit", "", allOpen))[0]).toBe(`[-] ${t("tree.standard")} 4`);
  });

  it("hide every standard object while Standard is closed", () => {
    const open = (key: string) => key !== "unit:Standard";
    expect(shape(treeLines(entries, "unit", "", open))).toEqual([
      `[+] ${t("tree.standard")} 4`,
      `[-] ${t("tree.custom")} 2`,
      "h000",
      "h001",
    ]);
  });

  it("open every group while filtering, and keep only the matches", () => {
    const closed = () => false;
    expect(shape(treeLines(entries, "unit", "KNI", closed))).toEqual([
      `[-] ${t("tree.standard")} 1`,
      "[-] human 1",
      "[-] Melee 1",
      "[-] Units 1",
      "hkni",
    ]);
  });

  it("key a group by the kind and its path", () => {
    expect(groupKeys("unit", entries[0] as TreeEntry)).toEqual([
      "unit:Standard",
      "unit:human",
      "unit:human/Melee",
      "unit:human/Melee/Units",
    ]);
    expect(groupKeys("unit", entries[1] as TreeEntry)).toEqual(["unit:Custom"]);
  });

  it("show races as World Editor names them", () => {
    expect(groupName("nightelf")).toBe("Night Elf");
    expect(groupName("human")).toBe("Human");
  });
});

describe("↑ and ↓", () => {
  const lines = treeLines(entries, "unit", "", allOpen);

  it("move to the next or previous object, over group rows", () => {
    expect(nextObject(lines, "ogru", 1)).toBe("h000");
    expect(nextObject(lines, "hkni", 1)).toBe("Hpal");
    expect(nextObject(lines, "Hpal", -1)).toBe("hkni");
  });

  it("go all the way down the list, one object at a time", () => {
    const seen: string[] = [];
    let at: string | null = null;
    for (let step = 0; step < 10; step++) {
      const next = nextObject(lines, at, 1);
      if (!next) break;
      seen.push(next);
      at = next;
    }
    expect(seen).toEqual(["hfoo", "hkni", "Hpal", "ogru", "h000", "h001"]);
  });

  it("stop at either end", () => {
    expect(nextObject(lines, "h001", 1)).toBeUndefined();
    expect(nextObject(lines, "hfoo", -1)).toBeUndefined();
  });
});
