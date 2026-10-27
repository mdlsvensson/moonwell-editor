import { describe, expect, it } from "vitest";
import type { Column, FieldInfo, TableCell, TableRow, TreeEntry } from "./api.ts";
import { defaultColumns } from "./grid.ts";
import {
  addColumns,
  type Chips,
  columnKey,
  defaultChips,
  fitColumns,
  groupChips,
  levelColumnLimit,
  mostLevels,
  nextSort,
  oneAtATime,
  readColumns,
  removeColumn,
  rowIds,
  rowsReadiness,
  type Store,
  searchFields,
  sortTable,
  storageKey,
  toggleGroup,
  writeColumns,
} from "./table.ts";
import { t } from "./text.ts";

function entry(id: string, path: string[], over: Partial<TreeEntry> = {}): TreeEntry {
  return {
    id,
    baseId: path[0] === "Custom" ? "hfoo" : null,
    name: `Name ${id}`,
    suffix: "",
    path,
    changed: false,
    ...over,
  };
}

const entries = [
  entry("h000", ["Custom"], { name: "Spearman", changed: true }),
  entry("h001", ["Custom"], { name: "Champion" }),
  entry("hfoo", ["human", "Melee", "Units"], { name: "Footman", changed: true }),
  entry("hkni", ["human", "Melee", "Units"], { name: "Knight" }),
  entry("ogru", ["orc", "Melee", "Units"], { name: "Grunt" }),
  entry("nzom", ["neutral", "Campaign", "Units"], { name: "Zombie", suffix: "(Evil)" }),
];

const standardOnly = entries.filter((each) => each.path[0] !== "Custom");

function info(id: string, perLevel = false, over: Partial<FieldInfo> = {}): FieldInfo {
  return {
    id,
    label: `Label ${id}`,
    category: "stats",
    sort: "",
    type: "int",
    perLevel,
    choice: null,
    listOf: null,
    ...over,
  };
}

function cell(value: string, over: Partial<TableCell> = {}): TableCell {
  return { value, base: value, changed: false, applies: true, warning: null, ...over };
}

function row(id: string, name: string, values: string[]): TableRow {
  return { id, name, baseId: null, readOnly: null, cells: values.map((value) => cell(value)) };
}

/** A store in memory, which can be made to throw. */
function memory(throws = false): Store & { items: Map<string, string> } {
  const items = new Map<string, string>();
  return {
    items,
    getItem: (key) => {
      if (throws) throw new Error("denied");
      return items.get(key) ?? null;
    },
    setItem: (key, value) => {
      if (throws) throw new Error("denied");
      items.set(key, value);
    },
  };
}

describe("the row chips", () => {
  it("are Custom first, then the tree's top groups in order, with counts", () => {
    expect(groupChips(entries)).toEqual([
      { key: "Custom", name: t("tree.custom"), count: 2 },
      { key: "human", name: "Human", count: 2 },
      { key: "orc", name: "Orc", count: 1 },
      { key: "neutral", name: "Neutral", count: 1 },
    ]);
  });

  it("show Custom also when the map has no custom objects", () => {
    expect(groupChips(standardOnly)[0]).toEqual({
      key: "Custom",
      name: t("tree.custom"),
      count: 0,
    });
  });

  it("start on the custom objects when the map has any", () => {
    expect(defaultChips(entries, "ogru")).toEqual({ groups: ["Custom"], changed: false });
  });

  it("start on the selected object's group when the map has no custom objects", () => {
    expect(defaultChips(standardOnly, "ogru")).toEqual({ groups: ["orc"], changed: false });
  });

  it("start on the first group with nothing selected, and on none with no objects", () => {
    expect(defaultChips(standardOnly, null)).toEqual({ groups: ["human"], changed: false });
    expect(defaultChips([], null)).toEqual({ groups: [], changed: false });
  });

  it("toggle a group on and off", () => {
    const on = toggleGroup({ groups: ["Custom"], changed: true }, "orc");
    expect(on).toEqual({ groups: ["Custom", "orc"], changed: true });
    expect(toggleGroup(on, "Custom")).toEqual({ groups: ["orc"], changed: true });
  });
});

describe("the rows", () => {
  const all: Chips = { groups: [], changed: false };

  it("are every object, in the tree's order, when no group is picked", () => {
    expect(rowIds(entries, all, "")).toEqual(["h000", "h001", "hfoo", "hkni", "ogru", "nzom"]);
  });

  it("are the objects of the picked groups", () => {
    expect(rowIds(entries, { groups: ["Custom", "orc"], changed: false }, "")).toEqual([
      "h000",
      "h001",
      "ogru",
    ]);
  });

  it("keep only changed objects with Changed (every custom object counts as changed)", () => {
    expect(rowIds(entries, { groups: [], changed: true }, "")).toEqual(["h000", "h001", "hfoo"]);
    expect(rowIds(entries, { groups: ["human"], changed: true }, "")).toEqual(["hfoo"]);
  });

  it("follow the tree's filter text: id, name or suffix, ignoring case", () => {
    expect(rowIds(entries, all, "KNI")).toEqual(["hkni"]);
    expect(rowIds(entries, all, " spear ")).toEqual(["h000"]);
    expect(rowIds(entries, all, "evil")).toEqual(["nzom"]);
    expect(rowIds(entries, { groups: ["orc"], changed: false }, "foot")).toEqual([]);
  });

  it("give each id once", () => {
    const twice = [...entries, entry("hfoo", ["human", "Melee", "Units"])];
    expect(rowIds(twice, all, "hfoo")).toEqual(["hfoo"]);
  });
});

describe("the columns", () => {
  const fields = [info("ugol"), info("ulum"), info("acdn", true)];

  it("drop a field the kind lacks, a level that does not fit, and a column given twice", () => {
    const given: Column[] = [
      { field: "ugol", level: 0 },
      { field: "gone", level: 0 },
      { field: "ulum", level: 1 },
      { field: "acdn", level: 0 },
      { field: "acdn", level: 2 },
      { field: "ugol", level: 0 },
    ];
    expect(fitColumns(given, fields)).toEqual([
      { field: "ugol", level: 0 },
      { field: "acdn", level: 2 },
    ]);
  });

  it("take nothing that is not a list of columns", () => {
    expect(fitColumns({ field: "ugol" }, fields)).toEqual([]);
    expect(fitColumns([1, null, "ugol", { field: "ugol", level: "0" }], fields)).toEqual([]);
  });

  it("start as the kind's defaults, without a default field the kind lacks", () => {
    const unitFields = ["ugol", "ulum", "uhpm", "ua1b", "udef"].map((id) => info(id));
    expect(readColumns("unit", unitFields, () => memory())).toEqual(
      defaultColumns.unit.filter((column) => column.field !== "ua1c"),
    );
  });

  it("are kept per kind and read back", () => {
    const store = memory();
    writeColumns("unit", [{ field: "ulum", level: 0 }], () => store);
    expect(store.items.get(storageKey("unit"))).toBe('[{"field":"ulum","level":0}]');
    expect(storageKey("unit")).toBe("objectEditor.columns.unit");
    expect(readColumns("unit", fields, () => store)).toEqual([{ field: "ulum", level: 0 }]);
    expect(readColumns("item", fields, () => store)).toEqual([]); // the item defaults are not fields here
  });

  it("keep an empty list the user chose", () => {
    const store = memory();
    writeColumns("unit", [], () => store);
    expect(readColumns("unit", fields, () => store)).toEqual([]);
  });

  it("ignore a store that throws, is missing or holds broken text", () => {
    const unitFields = defaultColumns.unit.map((column) => info(column.field));
    const throwing = memory(true);
    expect(() => writeColumns("unit", [], () => throwing)).not.toThrow();
    expect(readColumns("unit", unitFields, () => throwing)).toEqual(defaultColumns.unit);
    expect(
      readColumns("unit", unitFields, () => {
        throw new Error("no storage");
      }),
    ).toEqual(defaultColumns.unit);
    expect(readColumns("unit", unitFields, () => null)).toEqual(defaultColumns.unit);
    const broken = memory();
    broken.items.set(storageKey("unit"), "{not json");
    expect(readColumns("unit", unitFields, () => broken)).toEqual(defaultColumns.unit);
  });

  it("add a field with no levels once", () => {
    const columns = addColumns([], info("ugol"), 5);
    expect(columns).toEqual([{ field: "ugol", level: 0 }]);
    expect(addColumns(columns, info("ugol"), 1)).toEqual(columns);
  });

  it("add a per-level field at level 1, or at every level, never a (field, level) already shown", () => {
    const cooldown = info("acdn", true);
    const one = addColumns([{ field: "ugol", level: 0 }], cooldown, 1);
    expect(one).toEqual([
      { field: "ugol", level: 0 },
      { field: "acdn", level: 1 },
    ]);
    expect(addColumns(one, cooldown, 1)).toEqual(one);
    expect(addColumns(one, cooldown, 3).map(columnKey)).toEqual([
      "ugol:0",
      "acdn:1",
      "acdn:2",
      "acdn:3",
    ]);
    // The new levels go after the field's columns, not at the end.
    const between = [...one, { field: "ulum", level: 0 }];
    expect(addColumns(between, cooldown, 2).map(columnKey)).toEqual([
      "ugol:0",
      "acdn:1",
      "acdn:2",
      "ulum:0",
    ]);
    expect(addColumns([], cooldown, 0)).toEqual([{ field: "acdn", level: 1 }]);
  });

  it("add at most 20 levels of a per-level field at once", () => {
    expect(levelColumnLimit).toBe(20);
    const added = addColumns([], info("acdn", true), 34);
    expect(added).toHaveLength(20);
    expect(added.at(-1)).toEqual({ field: "acdn", level: 20 });
  });

  it("remove a column by its key", () => {
    const columns = [
      { field: "acdn", level: 1 },
      { field: "acdn", level: 2 },
    ];
    expect(removeColumn(columns, "acdn:1")).toEqual([{ field: "acdn", level: 2 }]);
  });
});

describe("the column picker", () => {
  const fields = [
    info("ugol", false, { label: "Gold Cost" }),
    info("uhpm", false, { label: "Hit Points Maximum (Base)" }),
    info("ua1c", false, { label: "Attack 1 - Cooldown Time", category: "combat" }),
  ];

  it("searches by label, id and category, every word, ignoring case", () => {
    expect(searchFields(fields, "").map((field) => field.id)).toEqual(["ugol", "uhpm", "ua1c"]);
    expect(searchFields(fields, "gold").map((field) => field.id)).toEqual(["ugol"]);
    expect(searchFields(fields, "UHPM").map((field) => field.id)).toEqual(["uhpm"]);
    expect(searchFields(fields, "combat").map((field) => field.id)).toEqual(["ua1c"]);
    expect(searchFields(fields, "cooldown attack").map((field) => field.id)).toEqual(["ua1c"]);
    expect(searchFields(fields, "gold attack")).toEqual([]);
  });

  it("finds the most levels any shown row has from their level counts", () => {
    const rows = [
      row("A000", "a", ["3"]),
      row("A001", "b", ["10"]),
      { ...row("A002", "c", [""]), cells: [cell("", { applies: false })] },
    ];
    expect(mostLevels(rows)).toBe(10);
    expect(mostLevels([])).toBe(1);
    expect(mostLevels([row("A000", "a", ["0"])])).toBe(1);
  });
});

describe("sorting by a header", () => {
  const columns: Column[] = [{ field: "ugol", level: 0 }];
  const rows = [
    row("hkni", "knight", ["245"]),
    row("hfoo", "Footman", ["135"]),
    row("h000", "Archer", ["135"]),
  ];

  it("goes up, then down, then back to the tree's order", () => {
    const up = nextSort(null, "name");
    expect(up).toEqual({ key: "name", descending: false });
    const down = nextSort(up, "name");
    expect(down).toEqual({ key: "name", descending: true });
    expect(nextSort(down, "name")).toBeNull();
    expect(nextSort(down, "ugol:0")).toEqual({ key: "ugol:0", descending: false });
  });

  it("sorts by name ignoring case, by id, or by a column's numbers", () => {
    const ids = (list: TableRow[]) => list.map((each) => each.id);
    expect(ids(sortTable(rows, { key: "name", descending: false }, columns))).toEqual([
      "h000",
      "hfoo",
      "hkni",
    ]);
    expect(ids(sortTable(rows, { key: "id", descending: true }, columns))).toEqual([
      "hkni",
      "hfoo",
      "h000",
    ]);
    expect(ids(sortTable(rows, { key: "ugol:0", descending: false }, columns))).toEqual([
      "hfoo",
      "h000",
      "hkni",
    ]);
    expect(ids(sortTable(rows, { key: "ugol:0", descending: true }, columns))).toEqual([
      "hkni",
      "hfoo",
      "h000",
    ]);
  });

  it("keeps the order with no sort or a column that is not shown", () => {
    expect(sortTable(rows, null, columns)).toBe(rows);
    expect(sortTable(rows, { key: "uhpm:0", descending: false }, columns)).toBe(rows);
  });
});

describe("rowsReadiness", () => {
  const now = { dirty: true, changedObjects: 1 };
  const before = { dirty: false, changedObjects: 0 };
  const ready = {
    editsOut: 0,
    session: now,
    waitingFor: null,
    rowsFor: { session: now },
    rowsError: "",
  };

  it("is ready when no edit is on its way and the rows were fetched for the map as it is", () => {
    expect(rowsReadiness(ready)).toBe("ready");
    expect(rowsReadiness({ ...ready, waitingFor: { session: before } })).toBe("ready");
  });

  it("waits while an edit is on its way", () => {
    expect(rowsReadiness({ ...ready, editsOut: 1 })).toBe("wait");
  });

  it("waits for the map's new state after an edit that changed cells", () => {
    expect(rowsReadiness({ ...ready, waitingFor: { session: now } })).toBe("wait");
  });

  it("waits for the rows of the map's new state, or for any rows at all", () => {
    expect(rowsReadiness({ ...ready, rowsFor: { session: before } })).toBe("wait");
    expect(rowsReadiness({ ...ready, rowsFor: null })).toBe("wait");
  });

  it("gives the reason when the rows could not be fetched", () => {
    expect(rowsReadiness({ ...ready, editsOut: 1, rowsError: "No map." })).toEqual({
      error: "No map.",
    });
  });
});

describe("oneAtATime", () => {
  /** A promise the test settles by hand. */
  function later<T>() {
    let settle!: (value: T) => void;
    let fail!: (error: unknown) => void;
    const promise = new Promise<T>((resolve, reject) => {
      settle = resolve;
      fail = reject;
    });
    return { promise, settle, fail };
  }
  const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

  it("starts each task once the one before has finished, in the order given", async () => {
    const queue = oneAtATime();
    const started: string[] = [];
    const first = later<string>();
    const a = queue(() => {
      started.push("a");
      return first.promise;
    });
    const b = queue(async () => {
      started.push("b");
      return "b";
    });
    await tick();
    expect(started).toEqual(["a"]);
    first.settle("a");
    expect(await a).toBe("a");
    expect(await b).toBe("b");
    expect(started).toEqual(["a", "b"]);
  });

  it("goes on after a task that failed, which fails on its own", async () => {
    const queue = oneAtATime();
    const failed = queue(async () => {
      throw new Error("refused");
    });
    const next = queue(async () => "next");
    await expect(failed).rejects.toThrow("refused");
    expect(await next).toBe("next");
  });

  it("makes two quick changes each work from the value the other left", async () => {
    // Two +10% changes, each reading the value, waiting for the app, then writing: one at a time they make 121.
    let value = 100;
    const plusTenPercent = async () => {
      const read = value;
      await tick();
      value = Math.round(read * 1.1);
    };
    const queue = oneAtATime();
    await Promise.all([queue(plusTenPercent), queue(plusTenPercent)]);
    expect(value).toBe(121);

    // Without the queue, both read 100.
    value = 100;
    await Promise.all([plusTenPercent(), plusTenPercent()]);
    expect(value).toBe(110);
  });
});
