import { describe, expect, it } from "vitest";
import type { ChoiceList, FieldView, ObjectView } from "./api.ts";
import {
  abilityIds,
  type Category,
  groupFields,
  listIds,
  listKinds,
  type Row,
  shownValue,
} from "./fields.ts";

/** A field with plain defaults; `over` sets what a test cares about. */
function field(over: Partial<FieldView> & { id: string }): FieldView {
  return {
    label: over.id,
    category: "stats",
    sort: "",
    type: "int",
    choice: null,
    listOf: null,
    level: 0,
    value: "0",
    base: "0",
    changed: false,
    skin: false,
    warning: null,
    modes: null,
    ...over,
  };
}

/** Each category as its key and its rows' field ids, which is what most tests check. */
function shape(categories: Category[]): [string, string[]][] {
  return categories.map((category) => [category.key, category.rows.map((row) => row.id)]);
}

const life = field({ id: "uhpm", label: "Hit Points (Base)", value: "650", changed: true });
const regen = field({ id: "uhpr", label: "Hit Points Regeneration", value: "0.25" });
const speed = field({ id: "umvs", label: "Movement Speed", category: "move", value: "270" });
const name = field({
  id: "unam",
  label: "Name",
  category: "text",
  type: "string",
  value: "Footman",
});

describe("the filter", () => {
  const fields = [life, regen, speed, name];

  it("keeps every field when it is empty", () => {
    expect(shape(groupFields(fields, "", false))).toEqual([
      ["stats", ["uhpm", "uhpr"]],
      ["move", ["umvs"]],
      ["text", ["unam"]],
    ]);
  });

  it("matches the label, ignoring case", () => {
    expect(shape(groupFields(fields, "hit POI", false))).toEqual([["stats", ["uhpm", "uhpr"]]]);
  });

  it("matches the id", () => {
    expect(shape(groupFields(fields, "UMVS", false))).toEqual([["move", ["umvs"]]]);
  });

  it("matches the value", () => {
    expect(shape(groupFields(fields, "foot", false))).toEqual([["text", ["unam"]]]);
    expect(shape(groupFields(fields, "650", false))).toEqual([["stats", ["uhpm"]]]);
  });

  it("drops a category with nothing left", () => {
    expect(groupFields(fields, "no such field", false)).toEqual([]);
  });

  it("also matches the text shown for a value, when given", () => {
    const attack = field({ id: "ua1t", label: "Attack Type", type: "choice", value: "hero" });
    const shown = (view: FieldView) => (view.id === "ua1t" ? "Hero (shown)" : view.value);
    expect(shape(groupFields([attack, life], "(shown)", false, shown))).toEqual([
      ["stats", ["ua1t"]],
    ]);
  });
});

describe("changed only", () => {
  it("keeps only the fields the map changes", () => {
    expect(shape(groupFields([life, regen, speed], "", true))).toEqual([["stats", ["uhpm"]]]);
  });

  it("works together with the filter", () => {
    expect(groupFields([life, regen], "regen", true)).toEqual([]);
  });
});

describe("per-level fields", () => {
  const healed = [1, 2, 3].map((level) =>
    field({
      id: "Hhb1",
      label: "Amount Healed/Damaged",
      category: "data",
      level,
      value: String(100 * level),
      changed: level === 2,
    }),
  );
  const cooldown = [2, 1].map((level) =>
    field({ id: "acdn", label: "Cooldown", category: "stats", level, value: "9" }),
  );
  const levelCount = field({ id: "alev", label: "Levels", category: "data", value: "3" });

  it("make one row per field with its levels in order", () => {
    const categories = groupFields([...healed, ...cooldown, levelCount], "", false);
    expect(shape(categories)).toEqual([
      ["stats", ["acdn"]],
      ["data", ["Hhb1", "alev"]],
    ]);
    const row: Row | undefined = categories[1]?.rows[0];
    expect(row?.levels?.map((level) => level.level)).toEqual([1, 2, 3]);
    expect(row?.label).toBe("Amount Healed/Damaged");
    expect(row?.field).toBe(healed[0]);
    expect(categories[0]?.rows[0]?.levels?.map((level) => level.level)).toEqual([1, 2]);
  });

  it("leave a field of no level as a single row", () => {
    const row = groupFields([levelCount], "", false)[0]?.rows[0];
    expect(row?.levels).toBeNull();
    expect(row?.field).toBe(levelCount);
  });

  it("count as changed when any level is, and then show every level", () => {
    const categories = groupFields([...healed, ...cooldown], "", true);
    expect(shape(categories)).toEqual([["data", ["Hhb1"]]]);
    expect(categories[0]?.rows[0]?.levels).toHaveLength(3);
  });

  it("match the filter by any level's value", () => {
    expect(shape(groupFields([...healed, ...cooldown], "300", false))).toEqual([
      ["data", ["Hhb1"]],
    ]);
  });
});

describe("the order", () => {
  it("puts the categories in the editor's order, unknown ones last by name", () => {
    const fields = ["zeta", "data", "text", "alpha", "stats", "abil", "editor", "combat"].map(
      (category) => field({ id: `f_${category}`, category }),
    );
    expect(groupFields(fields, "", false).map((category) => category.key)).toEqual([
      "stats",
      "combat",
      "abil",
      "text",
      "editor",
      "data",
      "alpha",
      "zeta",
    ]);
  });

  it("follows the full list of known categories", () => {
    const order = [
      "stats",
      "combat",
      "abil",
      "art",
      "move",
      "path",
      "sound",
      "tech",
      "text",
      "editor",
      "data",
    ];
    const fields = [...order].reverse().map((category) => field({ id: category, category }));
    expect(groupFields(fields, "", false).map((category) => category.key)).toEqual(order);
  });

  it("sorts the fields by their sort key, keeping the given order for equal keys", () => {
    const fields = [
      field({ id: "c", sort: "b2" }),
      field({ id: "a", sort: "a1" }),
      field({ id: "d", sort: "b2" }),
      field({ id: "b", sort: "a9" }),
    ];
    expect(shape(groupFields(fields, "", false))).toEqual([["stats", ["a", "b", "c", "d"]]]);
  });

  it("puts fields with no sort key after the others", () => {
    const fields = [field({ id: "none" }), field({ id: "some", sort: "z9" })];
    expect(shape(groupFields(fields, "", false))).toEqual([["stats", ["some", "none"]]]);
  });

  it("gives each category a readable name", () => {
    const fields = [
      field({ id: "a", category: "abil" }),
      field({ id: "b", category: "move" }),
      field({ id: "c", category: "" }),
      field({ id: "d", category: "custom" }),
    ];
    expect(groupFields(fields, "", false).map((category) => category.name)).toEqual([
      "Abilities",
      "Movement",
      "Other",
      "Custom",
    ]);
  });
});

describe("the value shown", () => {
  const choices: Record<string, ChoiceList> = {
    attackType: {
      flags: false,
      store: "string",
      entries: [
        { value: "normal", label: "Normal" },
        { value: "hero", label: "Hero" },
      ],
    },
    targetList: {
      flags: true,
      store: "string",
      entries: [
        { value: "air", label: "Air" },
        { value: "ground", label: "Ground" },
      ],
    },
    defenseTypeBits: {
      flags: true,
      store: "int",
      entries: [
        { value: "0", label: "Small" },
        { value: "1", label: "Medium" },
        { value: "2", label: "Large" },
      ],
    },
  };
  const names: Record<string, string> = { AHhb: "Holy Light", AHds: "Divine Shield" };
  const nameOf = (listOf: string, id: string) => (listOf === "Ability" ? names[id] : undefined);
  const show = (over: Partial<FieldView>) =>
    shownValue(field({ id: "f", ...over }), choices, nameOf);

  it("is a choice's label", () => {
    expect(show({ type: "choice", choice: "attackType", value: "hero" })).toBe("Hero");
  });

  it("is the value itself for a choice that is not listed, or with no lists loaded", () => {
    expect(show({ type: "choice", choice: "attackType", value: "magic" })).toBe("magic");
    expect(show({ type: "choice", choice: "nosuch", value: "hero" })).toBe("hero");
    const field1 = field({ id: "f", type: "choice", choice: "attackType", value: "hero" });
    expect(shownValue(field1, null, nameOf)).toBe("hero");
  });

  it("lists the labels of a comma list of choices", () => {
    expect(show({ type: "choice", choice: "targetList", value: "ground,air" })).toBe("Ground, Air");
  });

  it("lists the labels of the bits a number holds", () => {
    expect(show({ type: "choice", choice: "defenseTypeBits", value: "5" })).toBe("Small, Large");
    expect(show({ type: "choice", choice: "defenseTypeBits", value: "0" })).toBe("None");
  });

  it("names the objects of a list where they are known", () => {
    expect(show({ type: "objectList", listOf: "Ability", value: "AHhb,AHds,A0zz" })).toBe(
      "Holy Light (AHhb), Divine Shield (AHds), A0zz",
    );
    expect(show({ type: "objectRef", listOf: "Ability", value: "AHds" })).toBe(
      "Divine Shield (AHds)",
    );
  });

  it("says yes or no for a true-or-false field", () => {
    expect(show({ type: "bool", value: "1" })).toBe("Yes");
    expect(show({ type: "bool", value: "0" })).toBe("No");
  });

  it("is the value for any other field", () => {
    expect(show({ type: "int", value: "420" })).toBe("420");
  });
});

describe("object lists", () => {
  it("split into ids", () => {
    expect(listIds("AHhb, AHds,,AInv ")).toEqual(["AHhb", "AHds", "AInv"]);
    expect(listIds("")).toEqual([]);
  });

  it("know the kinds their ids are of", () => {
    expect(listKinds("Ability")).toEqual(["ability"]);
    expect(listKinds("Buff")).toEqual(["buff"]);
    expect(listKinds("object")).toEqual(["unit", "upgrade"]);
    expect(listKinds(null)).toEqual([]);
  });

  it("give an object's abilities from every ability list, once each", () => {
    const view = {
      fields: [
        field({ id: "uabi", type: "objectList", listOf: "Ability", value: "Aatk,AInv" }),
        field({ id: "uhab", type: "objectList", listOf: "Ability", value: "AHhb,AInv" }),
        field({ id: "ubui", type: "objectList", listOf: "Unit", value: "hfoo" }),
        field({ id: "udaa", type: "objectRef", listOf: "Ability", value: "" }),
      ],
    } as ObjectView;
    expect(abilityIds(view)).toEqual(["Aatk", "AInv", "AHhb"]);
  });
});
