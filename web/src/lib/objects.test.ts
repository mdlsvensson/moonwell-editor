import { beforeEach, describe, expect, it } from "vitest";
import type { Kind, TreeEntry } from "./api.ts";
import { findObjects, useTrees } from "./objects.ts";

function entry(id: string, name: string, path: string[]): TreeEntry {
  return {
    id,
    baseId: path[0] === "Custom" ? "AHhb" : null,
    name,
    suffix: "",
    path,
    changed: false,
  };
}

const abilities = [
  entry("A000", "Holy Bolt", ["Custom"]),
  entry("AHhb", "Holy Light", ["Human", "Heroes"]),
  entry("AHds", "Divine Shield", ["Human", "Heroes"]),
  entry("Ahol", "Unholy Frenzy", ["Undead", "Units"]),
];

beforeEach(() => {
  useTrees((kind: Kind) => (kind === "ability" ? abilities : undefined));
});

describe("findObjects", () => {
  it("finds by name or id, the exact id first, then names that start with the query", () => {
    expect(findObjects(["ability"], "holy", 10).map((found) => found.id)).toEqual([
      "A000",
      "AHhb",
      "Ahol",
    ]);
    expect(findObjects(["ability"], "ahds", 10).map((found) => found.id)).toEqual(["AHds"]);
  });

  it("says where each object sits", () => {
    expect(findObjects(["ability"], "AHhb", 10)[0]).toEqual({
      kind: "ability",
      id: "AHhb",
      name: "Holy Light",
      detail: "Human · Heroes",
    });
    expect(findObjects(["ability"], "A000", 10)[0]?.detail).toBe("Custom");
  });

  it("finds nothing for an empty query, a kind not loaded, or past the limit", () => {
    expect(findObjects(["ability"], "  ", 10)).toEqual([]);
    expect(findObjects(["unit"], "holy", 10)).toEqual([]);
    expect(findObjects(["ability"], "holy", 1)).toHaveLength(1);
  });
});
