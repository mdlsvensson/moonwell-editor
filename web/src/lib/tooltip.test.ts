import { describe, expect, it } from "vitest";
import type { FieldView, ObjectView } from "./api.ts";
import { t } from "./text.ts";
import {
  abilityTextValues,
  fillData,
  formatNumber,
  parseColors,
  placeholderNumber,
  textKey,
  tooltipLines,
  tooltipOf,
  wholeTextValues,
  withHotkey,
} from "./tooltip.ts";

/** A field with plain defaults; `over` sets what a test cares about. */
function field(over: Partial<FieldView> & { id: string }): FieldView {
  return {
    label: over.id,
    category: "text",
    sort: "",
    type: "string",
    choice: null,
    listOf: null,
    level: 0,
    value: "",
    base: "",
    changed: false,
    skin: false,
    warning: null,
    modes: null,
    ...over,
  };
}

function view(over: Partial<ObjectView> & Pick<ObjectView, "kind" | "fields">): ObjectView {
  return {
    id: "h000",
    baseId: "hfoo",
    name: "Footman",
    chain: [],
    levels: 0,
    readOnly: null,
    ...over,
  };
}

describe("parseColors", () => {
  it("gives a colour from |c to |r, ignoring the alpha", () => {
    expect(parseColors("Deals |cffff0000fire|r damage")).toEqual([
      [
        { text: "Deals ", color: null },
        { text: "fire", color: "#ff0000" },
        { text: " damage", color: null },
      ],
    ]);
    expect(parseColors("|c00FFCC00Gold")).toEqual([[{ text: "Gold", color: "#ffcc00" }]]);
  });

  it("breaks lines at |n and a real newline, keeping the colour", () => {
    expect(parseColors("a|nb\nc|cffffcc00d|ne")).toEqual([
      [{ text: "a", color: null }],
      [{ text: "b", color: null }],
      [
        { text: "c", color: null },
        { text: "d", color: "#ffcc00" },
      ],
      [{ text: "e", color: "#ffcc00" }],
    ]);
    expect(parseColors("a|n|nb")).toEqual([
      [{ text: "a", color: null }],
      [],
      [{ text: "b", color: null }],
    ]);
  });

  it("reads || as a bar and keeps an unknown | sequence as text", () => {
    expect(parseColors("a || b |x |cnothex")).toEqual([
      [{ text: "a | b |x |cnothex", color: null }],
    ]);
  });
});

describe("placeholderNumber", () => {
  it("shows the numbers the probes saw", () => {
    expect(placeholderNumber("0.11")).toBe("0");
    expect(placeholderNumber("1.5")).toBe("1");
    expect(placeholderNumber("0.11", "%")).toBe("11");
    expect(placeholderNumber("0.55", "%")).toBe("55");
    expect(placeholderNumber("0.125", ".")).toBe("0.1");
    expect(placeholderNumber("0.33", ".")).toBe("0.3");
    expect(placeholderNumber("1.5", ".")).toBe("1.5");
    expect(placeholderNumber("13", ".")).toBe("13");
  });

  it("leaves text that is no number", () => {
    expect(placeholderNumber("abc")).toBe("abc");
    expect(formatNumber("0.125")).toBe("0.125");
  });
});

describe("wholeTextValues", () => {
  it("gives a unit's hit points, regeneration and first attack's damage", () => {
    const footman = view({
      kind: "unit",
      fields: [
        field({ id: "uhpm", value: "420" }),
        field({ id: "uhpr", value: "0.25" }),
        field({ id: "ua1b", value: "11" }),
        field({ id: "ua1d", value: "1" }),
        field({ id: "ua1s", value: "2" }),
      ],
    });
    expect(wholeTextValues(footman)).toEqual({
      realHP: "420",
      regenHP: "0.25",
      mindmg1: "12",
      maxdmg1: "13",
    });
  });

  it("gives an item's charges, and leaves out a value whose field is missing", () => {
    expect(
      wholeTextValues(view({ kind: "item", fields: [field({ id: "iuse", value: "3" })] })),
    ).toEqual({
      uses: "3",
    });
    expect(
      wholeTextValues(view({ kind: "unit", fields: [field({ id: "uhpm", value: "420" })] })),
    ).toEqual({
      realHP: "420",
    });
  });
});

describe("fillData", () => {
  const values = { Dur1: "30.00", Cool2: "8.5", Area1: "250", DataB1: "0.25" };

  it("fills the object's own and its base's placeholders", () => {
    expect(fillData("Lasts <h000,Dur1> seconds.", ["h000", "AHhb"], values)).toBe(
      "Lasts 30 seconds.",
    );
    expect(fillData("<AHhb,Cool2>, <AHhb,area1>", ["h000", "AHhb"], values)).toBe("8, 250");
  });

  it("leaves another object's placeholder and an unknown key as written", () => {
    expect(fillData("<AHds,Dur1> <AHhb,DataA1>", ["AHhb"], values)).toBe(
      "<AHds,Dur1> <AHhb,DataA1>",
    );
  });

  it("shows a value as the game does: rounded down, with one decimal for ,. and times 100 for ,%", () => {
    expect(fillData("<AHhb,DataB1,%>%", ["AHhb"], values)).toBe("25%");
    expect(fillData("<AHhb,DataB1> <AHhb,DataB1,.> <AHhb,Cool2>", ["AHhb"], values)).toBe(
      "0 0.2 8",
    );
    expect(fillData("<AHhb,Cool2,.>", ["AHhb"], values)).toBe("8.5");
  });

  it("leaves a placeholder whose id is in another case, as the game does not fill it", () => {
    expect(fillData("<ahhb,Dur1>", ["AHhb"], values)).toBe("<ahhb,Dur1>");
  });

  it("marks the placeholders left in the text", () => {
    expect(tooltipLines("Heals |cffffcc00<AHhb,DataA1>|r hit points")).toEqual([
      [
        { text: "Heals ", color: null },
        { text: "<AHhb,DataA1>", color: "#ffcc00", placeholder: true },
        { text: " hit points", color: null },
      ],
    ]);
  });
});

describe("tooltipOf", () => {
  it("takes a unit's tip, text, costs, hotkey and icon", () => {
    const tip = tooltipOf(
      view({
        kind: "unit",
        fields: [
          field({ id: "utip", value: "Train Footman" }),
          field({ id: "utub", value: "Versatile foot soldier." }),
          field({ id: "uhot", type: "char", value: "F" }),
          field({ id: "ugol", type: "int", value: "135" }),
          field({ id: "ulum", type: "int", value: "0" }),
          field({ id: "ufoo", type: "int", value: "2" }),
          field({ id: "uico", type: "icon", asset: "icon", value: "BTNFootman.blp" }),
        ],
      }),
    );
    expect(tip).toEqual({
      icon: "BTNFootman.blp",
      title: "Train Footman (|cffffcc00F|r)",
      text: "Versatile foot soldier.",
      costs: [
        { label: t("tooltip.gold"), value: "135", icon: "gold" },
        { label: t("tooltip.food"), value: "2", icon: "food" },
      ],
      hotkey: "F",
      learn: null,
    });
  });

  it("takes an ability's fields at level 2 and fills its placeholders", () => {
    const perLevel = (id: string, values: string[]) =>
      values.map((value, i) => field({ id, level: i + 1, value }));
    const tip = tooltipOf(
      view({
        kind: "ability",
        id: "A000",
        baseId: "AHhb",
        name: "Holy Light",
        levels: 2,
        fields: [
          ...perLevel("atp1", [
            "Holy Light - [|cffffcc00Level 1|r]",
            "Holy Light - [|cffffcc00Level 2|r]",
          ]),
          ...perLevel("aub1", ["Cools <AHhb,Cool1>.", "Cools <A000,Cool2>, heals <AHhb,DataA2>."]),
          ...perLevel("amcs", ["65", "70"]),
          ...perLevel("acdn", ["5.00", "4.50"]),
          field({ id: "ahky", type: "char", value: "T" }),
          field({ id: "aher", type: "bool", value: "1" }),
          field({ id: "aret", value: "Learn Holy Light - [|cffffcc00Level %d|r]" }),
          field({ id: "arut", value: "Level 1 cools <AHhb,Cool1>." }),
          field({ id: "arhk", type: "char", value: "T" }),
          field({ id: "aart", asset: "icon", value: "BTNHolyBolt.blp" }),
        ],
      }),
      2,
    );
    expect(tip?.title).toBe("Holy Light - [|cffffcc00Level 2|r] (|cffffcc00T|r)");
    expect(tip?.text).toBe("Cools 4, heals <AHhb,DataA2>.");
    expect(tip?.costs).toEqual([
      { label: t("tooltip.mana"), value: "70", icon: "mana" },
      { label: t("tooltip.cooldown"), value: "4.5" },
    ]);
    expect(tip?.learn).toEqual({
      title: "Learn Holy Light - [|cffffcc00Level 2|r] (|cffffcc00T|r)",
      text: "Level 1 cools 5.",
    });
    expect(tip?.icon).toBe("BTNHolyBolt.blp");
  });

  it("adds an upgrade's cost per level, and has no tooltip for a doodad", () => {
    const tip = tooltipOf(
      view({
        kind: "upgrade",
        levels: 3,
        fields: [
          field({ id: "gtp1", level: 3, value: "Upgrade to Mithril" }),
          field({ id: "gglb", type: "int", value: "100" }),
          field({ id: "gglm", type: "int", value: "75" }),
        ],
      }),
      3,
    );
    expect(tip?.costs).toEqual([{ label: t("tooltip.gold"), value: "250", icon: "gold" }]);
    expect(tooltipOf(view({ kind: "doodad", fields: [] }))).toBeNull();
  });

  it("builds the ability values by text key and level, and leaves a hotkey out of an empty tip", () => {
    expect(
      abilityTextValues(
        view({
          kind: "ability",
          fields: [field({ id: "adur", level: 1, value: "3" }), field({ id: "anam" })],
        }),
      ),
    ).toEqual({ Dur1: "3" });
    expect(withHotkey("", "F")).toBe("");
  });
});

describe("textKey", () => {
  it("names an ability's data field by its letter and its other fields by the game's key", () => {
    expect(textKey({ id: "Hbz1", data: 1 })).toBe("DataA");
    expect(textKey({ id: "Hbz3", data: 3 })).toBe("DataC");
    expect(textKey({ id: "adur", data: 0 })).toBe("Dur");
    expect(textKey({ id: "anam" })).toBeUndefined();
  });
});
