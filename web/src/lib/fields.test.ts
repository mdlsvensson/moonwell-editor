import { describe, expect, it } from "vitest";
import type { ChoiceList, FieldView, ObjectView } from "./api.ts";
import {
  assetKey,
  assetText,
  buttonPair,
  type Category,
  groupFields,
  type Lookup,
  listIds,
  listKinds,
  namesText,
  type Row,
  searchPrompt,
  setAssetNames,
  shownValue,
  uses,
} from "./fields.ts";
import { t } from "./text.ts";

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
      t("category.abil"),
      t("category.move"),
      t("category.noCategory"),
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
        { value: "invulnerable", label: "Invulnerable", alternates: ["invu"] },
      ],
    },
    defenseTypeBits: {
      flags: true,
      store: "int",
      entries: [
        { value: "1", label: "Small" },
        { value: "2", label: "Medium" },
        { value: "4", label: "Large" },
        { value: "8", label: "Unlisted", hidden: true },
      ],
    },
  };
  const names: Record<string, string> = {
    AHhb: "Holy Light",
    AHds: "Divine Shield",
    AInv: "Inventory (Hero)",
  };
  const nameOf = (listOf: string, id: string, bare?: boolean) => {
    const name = listOf === "Ability" ? names[id] : undefined;
    return bare ? name?.replace(/ \(.*\)$/, "") : name;
  };
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

  it("lists the labels of a comma list of choices, a value by its other spelling too", () => {
    expect(show({ type: "choice", choice: "targetList", value: "ground,air" })).toBe("Ground, Air");
    // The game's data spells Invulnerable invu, which World Editor shows by its label (2026-10-27).
    expect(show({ type: "choice", choice: "targetList", value: "air,invu" })).toBe(
      "Air, Invulnerable",
    );
  });

  it("lists the labels of the bits a number holds", () => {
    expect(show({ type: "choice", choice: "defenseTypeBits", value: "5" })).toBe("Small, Large");
    expect(show({ type: "choice", choice: "defenseTypeBits", value: "0" })).toBe("None");
    // A hidden bit is not shown.
    expect(show({ type: "choice", choice: "defenseTypeBits", value: "12" })).toBe("Large");
  });

  it("names the objects of a list where they are known", () => {
    expect(show({ type: "objectList", listOf: "Ability", value: "AHhb,AHds,A0zz" })).toBe(
      "Holy Light, Divine Shield, A0zz",
    );
    expect(show({ type: "objectRef", listOf: "Ability", value: "AHds" })).toBe("Divine Shield");
  });

  it("names a skin list's abilities without their suffixes, as World Editor does", () => {
    const skins = { type: "objectList", listOf: "Ability", meta: "abilitySkinList" };
    expect(show({ ...skins, value: "AHhb,AInv" })).toBe("Holy Light, Inventory");
    expect(show({ type: "objectList", listOf: "Ability", value: "AInv" })).toBe("Inventory (Hero)");
  });

  it("is what World Editor shows for a field that holds nothing", () => {
    expect(show({ type: "int", value: "" })).toBe("0");
    expect(show({ type: "float", value: "" })).toBe("0.000");
    expect(show({ type: "bool", value: "" })).toBe("False");
    expect(show({ type: "choice", choice: "attackType", value: "" })).toBe("None");
    expect(show({ type: "choice", choice: "targetList", value: "" })).toBe("");
    expect(show({ type: "string", meta: "model", value: "" })).toBe("None");
    expect(show({ type: "string", meta: "soundLabel", value: "" })).toBe("None");
    expect(show({ type: "string", meta: "orderString", value: "" })).toBe("None");
    expect(show({ type: "string", meta: "modelList", value: "" })).toBe("");
    expect(show({ type: "string", meta: "string", value: "" })).toBe("");
    expect(show({ type: "objectList", listOf: "Ability", value: "" })).toBe("");
  });

  it("says True or False for a true-or-false field, and a real with three decimals, as World Editor does", () => {
    expect(show({ type: "bool", value: "1" })).toBe("True");
    expect(show({ type: "bool", value: "0" })).toBe("False");
    expect(show({ type: "float", value: "0.15" })).toBe("0.150");
    expect(show({ type: "float", value: "190" })).toBe("190.000");
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

  it("give the objects every list field names, once each, with the fields that name them", () => {
    const view = {
      fields: [
        field({
          id: "uabi",
          label: "Abilities",
          type: "objectList",
          listOf: "Ability",
          value: "Aatk,AInv",
        }),
        field({
          id: "uhab",
          label: "Hero Abilities",
          type: "objectList",
          listOf: "Ability",
          value: "AHhb,AInv",
        }),
        field({ id: "utra", label: "Trains", type: "objectList", listOf: "Unit", value: "hfoo" }),
        field({ id: "udaa", label: "Default", type: "objectRef", listOf: "Ability", value: "" }),
      ],
    } as ObjectView;
    const names: Record<string, string> = { Aatk: "Attack", AHhb: "Holy Light", hfoo: "Footman" };
    const lookup: Lookup = (listOf, id) =>
      names[id] ? { kind: listOf === "Unit" ? "unit" : "ability", name: names[id] } : undefined;
    expect(uses(view, lookup)).toEqual([
      { kind: "ability", id: "Aatk", name: "Attack", fields: ["Abilities"] },
      { kind: "ability", id: "AInv", name: "", fields: ["Abilities", "Hero Abilities"] },
      { kind: "ability", id: "AHhb", name: "Holy Light", fields: ["Hero Abilities"] },
      { kind: "unit", id: "hfoo", name: "Footman", fields: ["Trains"] },
    ]);
  });
});

describe("the picker's prompt", () => {
  it("names the kinds the list holds", () => {
    expect(searchPrompt(listKinds("Buff"))).toBe(
      t("picker.search", { what: t("kindPlural.buff") }),
    );
    expect(searchPrompt(listKinds("object"))).toBe(
      t("picker.search", {
        what: `${t("kindPlural.unit")}${t("picker.searchJoin")}${t("kindPlural.upgrade")}`,
      }),
    );
    expect(searchPrompt([])).toBe(t("picker.search", { what: t("picker.searchObjects") }));
  });
});

describe("a button position pair", () => {
  const x = field({ id: "abpx", label: "Button Position - Normal (X)", value: "1" });
  const y = field({ id: "abpy", label: "Button Position - Normal (Y)", value: "2" });
  const offX = field({ id: "aubx", label: "Button Position - Turn Off (X)" });

  it("is found from either half", () => {
    expect(buttonPair(x, [x, y, offX])).toEqual({ x, y });
    expect(buttonPair(y, [x, y, offX])).toEqual({ x, y });
  });

  it("is null for another field, or without its other half", () => {
    expect(buttonPair(life, [life, x, y])).toBeNull();
    expect(buttonPair(offX, [x, y, offX])).toBeNull();
  });
});

describe("files by name", () => {
  it("key a file by its path in lower case, without a model's or an image's extension", () => {
    expect(assetKey("Units/Human/Peasant/Peasant.mdl")).toBe(
      String.raw`units\human\peasant\peasant`,
    );
    expect(assetKey(String.raw`ReplaceableTextures\CommandButtons\BTNPeasant.blp`)).toBe(
      String.raw`replaceabletextures\commandbuttons\btnpeasant`,
    );
    expect(assetKey(String.raw`PathTextures\16x16Simple.tga`)).toBe(
      String.raw`pathtextures\16x16simple`,
    );
    // A dot in a folder's name is not an extension.
    expect(assetKey(String.raw`Abilities\Spells\HolyBolt.v2\Bolt`)).toBe(
      String.raw`abilities\spells\holybolt.v2\bolt`,
    );
  });

  it("join names as World Editor does, cut with ... once the next would pass its limit", () => {
    expect(namesText(["Castle", "Keep", "Town Hall"])).toBe("Castle, Keep, Town Hall");
    // The Paladin's icon, as World Editor showed it on 2026-10-27.
    const heroes = [
      "Paladin",
      "Admiral Proudmoore",
      "Dagren the Orcslayer",
      "Halahk the Lifebringer",
      "Magroth the Defender",
      "Lord Nicholas Buzan",
      "Lord Maxwell Tyrosus",
    ];
    expect(namesText(heroes)).toBe(
      "Paladin, Admiral Proudmoore, Dagren the Orcslayer, Halahk the Lifebringer, Magroth the Defender, Lord Nicholas Buzan, ...",
    );
    expect(namesText(["x".repeat(200)])).toBe("x".repeat(200));
  });

  it("show a file field by the objects that use it, or by its path when none does", () => {
    setAssetNames((group, key) =>
      group === "model" && key === String.raw`units\human\peasant\peasant`
        ? ["Peasant"]
        : undefined,
    );
    const model = { type: "string", meta: "model" };
    const peasant = String.raw`units\human\Peasant\Peasant`;
    const priest = String.raw`units\human\Priest\Priest`;
    expect(shownValue(field({ id: "umdl", ...model, value: peasant }), null, () => undefined)).toBe(
      "Peasant",
    );
    expect(shownValue(field({ id: "umdl", ...model, value: priest }), null, () => undefined)).toBe(
      priest,
    );
    expect(assetText({ meta: "string", value: peasant })).toBeUndefined();
    setAssetNames(() => undefined);
  });
});
