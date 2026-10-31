import { describe, expect, it } from "vitest";
import type { Catalog, CatalogFunction, TreeItem, TriggerView, Variable } from "./api.ts";
import {
  callsFor,
  dropPlaceAt,
  findParameter,
  functionsOf,
  functionTitle,
  objectKindOf,
  pickKindOf,
  presetsOf,
  treeDrop,
  treeRows,
  valueOffered,
  variablesFor,
} from "./lists.ts";

function fn(name: string, more: Partial<CatalogFunction> = {}): CatalogFunction {
  return {
    name,
    kind: "action",
    category: "TC_UNIT",
    title: name,
    sentence: [],
    arguments: [],
    defaults: [],
    returns: null,
    inEvents: false,
    version: 0,
    hint: "",
    ...more,
  };
}

const catalog: Catalog = {
  categories: [
    { key: "TC_UNIT", title: "Unit", icon: "", shown: true },
    { key: "TC_NOTHING", title: "General", icon: "", shown: false },
  ],
  types: [
    {
      key: "unit",
      title: "Unit",
      global: true,
      comparable: true,
      base: null,
      default: null,
      defaultText: null,
    },
    {
      key: "player",
      title: "Player",
      global: true,
      comparable: true,
      base: null,
      default: null,
      defaultText: null,
    },
    {
      key: "integer",
      title: "Integer",
      global: true,
      comparable: true,
      base: null,
      default: "0",
      defaultText: "0",
    },
    {
      key: "unitcode",
      title: "Unit-Type",
      global: true,
      comparable: true,
      base: "integer",
      default: null,
      defaultText: null,
    },
  ],
  presets: [
    { key: "Player00", type: "player", text: "Player 1 (Red)", code: "Player(0)", version: 0 },
    { key: "Player01", type: "player", text: "Player 2 (Blue)", code: "Player(1)", version: 0 },
    { key: "OperatorAdd", type: "ArithmeticOperator", text: "+", code: "+", version: 0 },
  ],
  functions: [
    fn("CreateUnit"),
    fn("DoNothing", { category: "TC_NOTHING" }),
    fn("MapInit", { kind: "event" }),
    fn("GetTriggerUnit", { kind: "call", returns: "unit" }),
    fn("GetLastCreatedUnit", { kind: "call", returns: "unit", inEvents: true }),
    fn("GetInteger", { kind: "call", returns: "integer" }),
    fn("IsAlive", { kind: "condition" }),
  ],
};

const variables: Variable[] = [
  { id: 1, name: "Hero", type: "unit", isArray: false, size: 1, initial: "" },
  { id: 2, name: "Count", type: "integer", isArray: false, size: 1, initial: "" },
];

describe("treeRows", () => {
  const items: TreeItem[] = [
    { id: 0, parent: -1, kind: "root", name: "Map", enabled: true, expanded: true, ref: "" },
    { id: 1, parent: 0, kind: "category", name: "Setup", enabled: true, expanded: true, ref: "" },
    { id: 2, parent: 1, kind: "trigger", name: "Start", enabled: true, expanded: false, ref: "" },
    { id: 3, parent: 0, kind: "category", name: "Later", enabled: true, expanded: false, ref: "" },
    { id: 4, parent: 3, kind: "variable", name: "Score", enabled: true, expanded: false, ref: "" },
  ];

  it("shows what is in an open category, one deeper", () => {
    const rows = treeRows(items, (item) => item.expanded, "");
    expect(rows.map((row) => [row.item.id, row.depth])).toEqual([
      [0, 0],
      [1, 1],
      [2, 2],
      [3, 1],
    ]);
    expect(rows[3]?.children).toBe(true);
    expect(rows[2]?.children).toBe(false);
  });

  it("a filter finds what is in a closed category, with the categories around it", () => {
    const rows = treeRows(items, () => false, "scor");
    expect(rows.map((row) => row.item.id)).toEqual([0, 3, 4]);
  });
});

describe("treeDrop", () => {
  const item = (id: number, parent: number, kind: TreeItem["kind"]): TreeItem => ({
    id,
    parent,
    kind,
    name: String(id),
    enabled: true,
    expanded: false,
    ref: "",
  });
  const items = [
    item(0, -1, "root"),
    item(1, 0, "category"),
    item(2, 1, "trigger"),
    item(3, 1, "trigger"),
    item(4, 1, "category"),
    item(5, 4, "trigger"),
    item(6, 0, "category"),
    item(7, 6, "variable"),
  ];
  const at = (id: number): TreeItem => items.find((each) => each.id === id) as TreeItem;
  const closed = (): boolean => false;
  const open = (): boolean => true;

  it("puts an item before or after another, counted with it taken out", () => {
    expect(treeDrop(items, at(2), at(3), "after", closed)).toEqual({ parent: 1, index: 1 });
    expect(treeDrop(items, at(3), at(2), "before", closed)).toEqual({ parent: 1, index: 0 });
  });

  it("puts an item at the end of a category it is dropped into", () => {
    expect(treeDrop(items, at(2), at(6), "inside", closed)).toEqual({ parent: 6, index: 1 });
  });

  it("below an open category is its first place", () => {
    expect(treeDrop(items, at(7), at(4), "after", open)).toEqual({ parent: 4, index: 0 });
  });

  it("refuses an item on itself or inside itself", () => {
    expect(treeDrop(items, at(1), at(1), "inside", closed)).toBeNull();
    expect(treeDrop(items, at(1), at(5), "before", closed)).toBeNull();
  });

  it("keeps everything but categories out of the map itself", () => {
    expect(treeDrop(items, at(2), at(0), "inside", closed)).toBeNull();
    expect(treeDrop(items, at(2), at(6), "before", closed)).toEqual({ parent: 6, index: 1 });
    expect(treeDrop(items, at(4), at(6), "before", closed)).toEqual({ parent: 0, index: 1 });
  });
});

describe("dropPlaceAt", () => {
  it("has a middle on a category and none on anything else", () => {
    expect(dropPlaceAt(13, 26, true)).toBe("inside");
    expect(dropPlaceAt(2, 26, true)).toBe("before");
    expect(dropPlaceAt(24, 26, true)).toBe("after");
    expect(dropPlaceAt(12, 26, false)).toBe("before");
    expect(dropPlaceAt(14, 26, false)).toBe("after");
  });
});

describe("the lists of a parameter", () => {
  it("offers the presets of a type", () => {
    expect(presetsOf(catalog, "player").map((preset) => preset.key)).toEqual([
      "Player00",
      "Player01",
    ]);
  });

  it("offers the calls that give the type, and only those allowed in an event", () => {
    expect(callsFor(catalog, "unit", false).map((call) => call.name)).toEqual([
      "GetTriggerUnit",
      "GetLastCreatedUnit",
    ]);
    expect(callsFor(catalog, "unit", true).map((call) => call.name)).toEqual([
      "GetLastCreatedUnit",
    ]);
  });

  it("a callback's function is a condition or an action", () => {
    expect(callsFor(catalog, "boolexpr", false).map((call) => call.name)).toEqual(["IsAlive"]);
    expect(callsFor(catalog, "code", false).map((call) => call.name)).toEqual([
      "CreateUnit",
      "DoNothing",
    ]);
  });

  it("offers the variables of the type, all of them for any variable", () => {
    expect(variablesFor(variables, "unit").map((each) => each.name)).toEqual(["Hero"]);
    expect(variablesFor(variables, "AnyGlobal").map((each) => each.name)).toEqual([
      "Hero",
      "Count",
    ]);
    expect(variablesFor(variables, "integervar").map((each) => each.name)).toEqual(["Count"]);
  });

  it("offers a typed value for numbers, text and objects, not for a thing of the game", () => {
    expect(valueOffered(catalog, "integer")).toBe(true);
    expect(valueOffered(catalog, "unitcode")).toBe(true);
    expect(valueOffered(catalog, "StringExt")).toBe(true);
    expect(valueOffered(catalog, "unit")).toBe(false);
    expect(valueOffered(catalog, "player")).toBe(false);
  });

  it("names the object list and the map pick of a type", () => {
    expect(objectKindOf("unitcode")).toBe("unit");
    expect(objectKindOf("heroskillcode")).toBe("ability");
    expect(objectKindOf("integer")).toBeNull();
    expect(pickKindOf("rect")).toBe("region");
    expect(pickKindOf("location")).toBe("point");
    expect(pickKindOf("integer")).toBeNull();
  });
});

describe("the function picker", () => {
  it("lists a kind's functions, by category and text", () => {
    expect(functionsOf(catalog, "action", "", "").map((each) => each.name)).toEqual([
      "CreateUnit",
      "DoNothing",
    ]);
    expect(functionsOf(catalog, "action", "TC_NOTHING", "").map((each) => each.name)).toEqual([
      "DoNothing",
    ]);
    expect(functionsOf(catalog, "action", "", "create").map((each) => each.name)).toEqual([
      "CreateUnit",
    ]);
  });

  it("a title has its category unless the category hides it", () => {
    expect(functionTitle(catalog, fn("Make", { title: "Make" }))).toBe("Unit - Make");
    expect(functionTitle(catalog, fn("Make", { title: "Make", category: "TC_NOTHING" }))).toBe(
      "Make",
    );
  });
});

describe("findParameter", () => {
  it("finds a parameter inside a function's parameter and in a block", () => {
    const inner = {
      path: "0/c1/p0/p0",
      type: "unit",
      name: "",
      kind: "none",
      value: "",
      text: "",
      typed: "",
      function: null,
      index: null,
    } as const;
    const outer = {
      path: "0/c1/p0",
      type: "location",
      name: "",
      kind: "function",
      value: "GetUnitLoc",
      text: "",
      typed: "",
      function: {
        path: "0/c1/p0",
        name: "GetUnitLoc",
        kind: "call",
        category: "",
        enabled: true,
        prefix: "",
        sentence: [{ text: "", parameter: inner }],
        blocks: [],
      },
      index: null,
    } as const;
    const trigger = {
      actions: [
        {
          path: "0",
          sentence: [],
          blocks: [
            {
              branch: 0,
              title: "",
              kind: "action",
              functions: [{ path: "0/c1", sentence: [{ text: "", parameter: outer }], blocks: [] }],
            },
          ],
        },
      ],
      events: [],
      conditions: [],
    } as unknown as TriggerView;

    expect(findParameter(trigger, "0/c1/p0")?.value).toBe("GetUnitLoc");
    expect(findParameter(trigger, "0/c1/p0/p0")?.type).toBe("unit");
    expect(findParameter(trigger, "9/p0")).toBeNull();
  });
});
