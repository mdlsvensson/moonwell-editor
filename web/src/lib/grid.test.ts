import { describe, expect, it } from "vitest";
import type { CellEdit, ChoiceList, Column, FieldInfo, TableCell, TableRow } from "./api.ts";
import {
  blockSize,
  bulkEdits,
  cellField,
  cellsOf,
  copyBlock,
  defaultColumns,
  editRefusal,
  fillDownEdits,
  moveSelection,
  pasteEdits,
  relocateSelection,
  resetEdits,
  sameSelection,
  selectAll,
  selectionRef,
  sortRows,
  topLeft,
} from "./grid.ts";
import { t } from "./text.ts";

function cell(value: string, over: Partial<TableCell> = {}): TableCell {
  return { value, base: value, changed: false, applies: true, warning: null, ...over };
}

function row(id: string, values: string[], over: Partial<TableRow> = {}): TableRow {
  return {
    id,
    name: `Name ${id}`,
    baseId: null,
    readOnly: null,
    cells: values.map((value) => cell(value)),
    ...over,
  };
}

function info(id: string, label: string, type: string, perLevel = false): FieldInfo {
  return { id, label, category: "stats", sort: "", type, perLevel, choice: null, listOf: null };
}

const columns: Column[] = [
  { field: "ugol", level: 0 },
  { field: "uhpm", level: 0 },
  { field: "unam", level: 0 },
];

const fields: FieldInfo[] = [
  info("ugol", "Gold cost", "int"),
  info("uhpm", "Hit points", "float"),
  info("unam", "Name", "string"),
];

/** The rows with one cell replaced. */
function patched(list: TableRow[], at: number, col: number, replacement: TableCell): TableRow[] {
  return list.map((item, index) =>
    index === at
      ? { ...item, cells: item.cells.map((old, i) => (i === col ? replacement : old)) }
      : item,
  );
}

/** The rows with one of them read only. */
function readOnlyRow(list: TableRow[], at: number, reason: string): TableRow[] {
  return list.map((item, index) => (index === at ? { ...item, readOnly: reason } : item));
}

const rows = (): TableRow[] => [
  row("h000", ["100", "1.5", "Footman"]),
  row("h001", ["200", "2.5", "Rifleman"]),
  row("h002", ["300", "3.5", "Knight"]),
];

const sel = (r1: number, c1: number, r2: number, c2: number) => ({
  anchor: { row: r1, col: c1 },
  focus: { row: r2, col: c2 },
});

const choices: Record<string, ChoiceList> = {
  attackType: {
    flags: false,
    store: "string",
    entries: [
      { value: "unknown", label: "None" },
      { value: "normal", label: "Normal" },
      { value: "pierce", label: "Pierce" },
    ],
  },
  defenseType: {
    flags: false,
    store: "string",
    entries: [
      { value: "large", label: "Large" },
      { value: "fort", label: "Fortified" },
      { value: "none", label: "Unarmored" },
    ],
  },
  deathType: {
    flags: false,
    store: "string",
    entries: [{ value: "alive", label: "Alive", alternates: ["aliv"] }],
  },
  targetList: {
    flags: true,
    store: "string",
    entries: [
      { value: "ground", label: "Ground" },
      { value: "air", label: "Air" },
    ],
  },
};

const choiceFields: FieldInfo[] = [
  info("ubdg", "Is a building", "bool"),
  { ...info("ua1t", "Attack type", "choice"), choice: "attackType" },
  { ...info("udty", "Defense type", "choice"), choice: "defenseType" },
  { ...info("udty2", "Death type", "choice"), choice: "deathType" },
  { ...info("atar", "Targets", "choice"), choice: "targetList" },
  { ...info("umvt", "Movement", "choice"), choice: "moveType" },
];

const choiceColumns: Column[] = choiceFields.map((field) => ({ field: field.id, level: 0 }));

const choiceRows = (): TableRow[] => [
  row("h000", ["0", "normal", "large", "alive", "ground", "foot"]),
  row("h001", ["0", "pierce", "fort", "alive", "air", "horse"]),
];

describe("cellsOf", () => {
  it("lists the block row by row", () => {
    expect(cellsOf(sel(0, 0, 1, 1))).toEqual([
      { row: 0, col: 0 },
      { row: 0, col: 1 },
      { row: 1, col: 0 },
      { row: 1, col: 1 },
    ]);
  });

  it("is the same block when the focus is above and left of the anchor", () => {
    expect(cellsOf(sel(1, 2, 0, 1))).toEqual([
      { row: 0, col: 1 },
      { row: 0, col: 2 },
      { row: 1, col: 1 },
      { row: 1, col: 2 },
    ]);
  });

  it("is one cell when anchor and focus are the same", () => {
    expect(cellsOf(sel(2, 3, 2, 3))).toEqual([{ row: 2, col: 3 }]);
  });
});

describe("sortRows", () => {
  it("sorts numbers as numbers, both ways, without changing the input", () => {
    const input = [row("a", ["10"]), row("b", ["9"]), row("c", ["100"]), row("d", ["-5.5"])];
    expect(sortRows(input, 0, false).map((r) => r.id)).toEqual(["d", "b", "a", "c"]);
    expect(sortRows(input, 0, true).map((r) => r.id)).toEqual(["c", "a", "b", "d"]);
    expect(input.map((r) => r.id)).toEqual(["a", "b", "c", "d"]);
  });

  it("sorts text without regard to case", () => {
    const input = [row("a", ["banana"]), row("b", ["Apple"]), row("c", ["cherry"])];
    expect(sortRows(input, 0, false).map((r) => r.id)).toEqual(["b", "a", "c"]);
    expect(sortRows(input, 0, true).map((r) => r.id)).toEqual(["c", "a", "b"]);
  });

  it("puts empty cells and cells that do not apply last, both ways", () => {
    const input = [
      row("a", [""]),
      row("b", ["5"]),
      { ...row("c", ["7"]), cells: [cell("7", { applies: false })] },
      row("d", ["1"]),
    ];
    expect(sortRows(input, 0, false).map((r) => r.id)).toEqual(["d", "b", "a", "c"]);
    expect(sortRows(input, 0, true).map((r) => r.id)).toEqual(["b", "d", "a", "c"]);
  });

  it("keeps the order of equal cells", () => {
    const input = [row("a", ["1"]), row("b", ["1"]), row("c", ["1"])];
    expect(sortRows(input, 0, false).map((r) => r.id)).toEqual(["a", "b", "c"]);
    expect(sortRows(input, 0, true).map((r) => r.id)).toEqual(["a", "b", "c"]);
  });
});

describe("pasteEdits", () => {
  it("pastes a block from the start cell, down the rows and right across the columns", () => {
    const block = [
      ["1", "2"],
      ["3", "4"],
    ];
    expect(pasteEdits({ row: 1, col: 0 }, block, rows(), columns, fields, null)).toEqual([
      { id: "h001", field: "ugol", level: 0, value: "1" },
      { id: "h001", field: "uhpm", level: 0, value: "2" },
      { id: "h002", field: "ugol", level: 0, value: "3" },
      { id: "h002", field: "uhpm", level: 0, value: "4" },
    ]);
  });

  it("stops at the table's edge", () => {
    const block = [
      ["a", "b"],
      ["c", "d"],
    ];
    expect(pasteEdits({ row: 2, col: 2 }, block, rows(), columns, fields, null)).toEqual([
      { id: "h002", field: "unam", level: 0, value: "a" },
    ]);
  });

  it("carries the column's level", () => {
    expect(
      pasteEdits({ row: 0, col: 0 }, [["5"]], rows(), [{ field: "acdn", level: 2 }], [], null),
    ).toEqual([{ id: "h000", field: "acdn", level: 2, value: "5" }]);
  });

  it("skips cells that do not apply", () => {
    let input = rows();
    input = patched(input, 0, 1, cell("", { applies: false }));
    const block = [
      ["1", "2"],
      ["3", "4"],
    ];
    expect(pasteEdits({ row: 0, col: 0 }, block, input, columns, fields, null)).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "1" },
      { id: "h001", field: "ugol", level: 0, value: "3" },
      { id: "h001", field: "uhpm", level: 0, value: "4" },
    ]);
  });

  it("refuses as a whole when the block covers a read-only row, naming its first cell", () => {
    let input = rows();
    input = patched(input, 1, 0, cell("", { applies: false }));
    input = readOnlyRow(input, 1, "The file is damaged.");
    const block = [
      ["1", "2"],
      ["3", "4"],
      ["5", "6"],
    ];
    expect(pasteEdits({ row: 0, col: 0 }, block, input, columns, fields, null)).toEqual({
      error: "Name h001 · Gold cost: The file is damaged.",
    });
    // A gap in the block writes nothing, so it is no change of the read-only row.
    expect(pasteEdits({ row: 1, col: 0 }, [[""], ["7"]], input, columns, fields, null)).toEqual([
      { id: "h002", field: "ugol", level: 0, value: "7" },
    ]);
  });

  it("reads a decimal comma in a number column", () => {
    const block = [["1,5", "2,25", "1,5"]];
    expect(pasteEdits({ row: 0, col: 0 }, block, rows(), columns, fields, null)).toEqual([
      // A whole-number column gets 1.5, which the app then refuses as a fraction.
      { id: "h000", field: "ugol", level: 0, value: "1.5" },
      { id: "h000", field: "uhpm", level: 0, value: "2.25" },
      { id: "h000", field: "unam", level: 0, value: "1,5" },
    ]);
    const kept = (value: string) =>
      (pasteEdits({ row: 0, col: 1 }, [[value]], rows(), columns, fields, null) as CellEdit[])[0]
        ?.value;
    expect(kept("-1,5")).toBe("-1.5");
    expect(kept(",5")).toBe(".5");
    expect(kept("1,234,5")).toBe("1,234,5");
    expect(kept("1.234,5")).toBe("1.234,5");
    expect(kept("1,5e3")).toBe("1,5e3");
    expect(kept("a,5")).toBe("a,5");
    expect(kept("1.5")).toBe("1.5");
  });

  it("skips an empty cell for a number column and writes it for a text column", () => {
    const block = [
      ["", "", ""],
      ["7", "", "x"],
    ];
    expect(pasteEdits({ row: 0, col: 0 }, block, rows(), columns, fields, null)).toEqual([
      { id: "h000", field: "unam", level: 0, value: "" },
      { id: "h001", field: "ugol", level: 0, value: "7" },
      { id: "h001", field: "unam", level: 0, value: "x" },
    ]);
  });

  it("skips an empty cell for bool, choice and unknown fields, writes it for lists", () => {
    const more: FieldInfo[] = [
      info("a", "A", "bool"),
      info("b", "B", "choice"),
      info("c", "C", "objectList"),
      info("d", "D", "objectRef"),
    ];
    const cols: Column[] = [
      ...more.map((f) => ({ field: f.id, level: 0 })),
      { field: "zzzz", level: 0 },
    ];
    const input = [row("h000", ["", "", "", "", ""])];
    expect(pasteEdits({ row: 0, col: 0 }, [["", "", "", "", ""]], input, cols, more, null)).toEqual(
      [
        { id: "h000", field: "c", level: 0, value: "" },
        { id: "h000", field: "d", level: 0, value: "" },
      ],
    );
  });

  it("gives nothing for a start outside the table", () => {
    expect(pasteEdits({ row: 5, col: 0 }, [["1"]], rows(), columns, fields, null)).toEqual([]);
  });

  it("reads yes/no and a choice's value or label, and refuses anything else naming the cell", () => {
    const paste = (block: string[][]) =>
      pasteEdits({ row: 0, col: 0 }, block, choiceRows(), choiceColumns, choiceFields, choices);
    expect(paste([["Yes", "Normal", "LARGE", "aliv"]])).toEqual([
      { id: "h000", field: "ubdg", level: 0, value: "1" },
      { id: "h000", field: "ua1t", level: 0, value: "normal" },
      { id: "h000", field: "udty", level: 0, value: "large" },
      { id: "h000", field: "udty2", level: 0, value: "alive" },
    ]);
    expect(paste([["0", "Fortified"]])).toEqual({
      error: `Name h000 · Attack type: ${t("grid.notOneOfValues", { text: "Fortified", values: "None, Normal, Pierce" })}`,
    });
    expect(paste([["7"]])).toEqual({
      error: `Name h000 · Is a building: ${t("grid.takesYesNo", { text: "7" })}`,
    });
    // A flags list is pasted as it is.
    expect(
      pasteEdits(
        { row: 0, col: 4 },
        [["ground,air"]],
        choiceRows(),
        choiceColumns,
        choiceFields,
        choices,
      ),
    ).toEqual([{ id: "h000", field: "atar", level: 0, value: "ground,air" }]);
  });
});

describe("fillDownEdits", () => {
  it("copies the top cell of each column down the selection", () => {
    expect(fillDownEdits(sel(0, 0, 2, 1), rows(), columns, fields)).toEqual([
      { id: "h001", field: "ugol", level: 0, value: "100" },
      { id: "h001", field: "uhpm", level: 0, value: "1.5" },
      { id: "h002", field: "ugol", level: 0, value: "100" },
      { id: "h002", field: "uhpm", level: 0, value: "1.5" },
    ]);
  });

  it("works from the top whichever way the selection was made", () => {
    expect(fillDownEdits(sel(2, 2, 1, 2), rows(), columns, fields)).toEqual([
      { id: "h002", field: "unam", level: 0, value: "Rifleman" },
    ]);
  });

  it("skips destinations that do not apply", () => {
    let input = rows();
    input = patched(input, 1, 0, cell("", { applies: false }));
    expect(fillDownEdits(sel(0, 0, 2, 1), input, columns, fields)).toEqual([
      { id: "h001", field: "uhpm", level: 0, value: "1.5" },
      { id: "h002", field: "ugol", level: 0, value: "100" },
      { id: "h002", field: "uhpm", level: 0, value: "1.5" },
    ]);
  });

  it("refuses as a whole when a destination row is read only, naming its first cell", () => {
    const input = readOnlyRow(rows(), 2, "Read only.");
    expect(fillDownEdits(sel(0, 1, 2, 2), input, columns, fields)).toEqual({
      error: "Name h002 · Hit points: Read only.",
    });
    // The top row is only read from.
    const top = readOnlyRow(rows(), 0, "Read only.");
    expect(fillDownEdits(sel(0, 0, 1, 0), top, columns, fields)).toEqual([
      { id: "h001", field: "ugol", level: 0, value: "100" },
    ]);
  });

  it("skips a column whose top cell does not apply", () => {
    let input = rows();
    input = patched(input, 0, 1, cell("", { applies: false }));
    expect(fillDownEdits(sel(0, 1, 2, 1), input, columns, fields)).toEqual([]);
  });

  it("gives nothing for a single row", () => {
    expect(fillDownEdits(sel(1, 0, 1, 2), rows(), columns, fields)).toEqual([]);
  });
});

describe("resetEdits", () => {
  it("resets each selected cell with a null value", () => {
    expect(resetEdits(sel(0, 1, 1, 2), rows(), columns, fields)).toEqual([
      { id: "h000", field: "uhpm", level: 0, value: null },
      { id: "h000", field: "unam", level: 0, value: null },
      { id: "h001", field: "uhpm", level: 0, value: null },
      { id: "h001", field: "unam", level: 0, value: null },
    ]);
  });

  it("skips cells that do not apply", () => {
    let input = rows();
    input = patched(input, 0, 0, cell("", { applies: false }));
    expect(resetEdits(sel(0, 0, 1, 0), input, columns, fields)).toEqual([
      { id: "h001", field: "ugol", level: 0, value: null },
    ]);
  });

  it("refuses as a whole when a row is read only, naming its first cell", () => {
    let input = rows();
    input = patched(input, 0, 0, cell("", { applies: false }));
    input[1] = row("h001", ["200", "2.5", "Rifleman"], { readOnly: "Read only." });
    expect(resetEdits(sel(0, 0, 1, 1), input, columns, fields)).toEqual({
      error: "Name h001 · Gold cost: Read only.",
    });
  });
});

describe("bulkEdits", () => {
  it("applies +5 over a block, one edit per cell", () => {
    expect(bulkEdits(sel(0, 0, 1, 1), rows(), columns, fields, "+5", null)).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "105" },
      { id: "h000", field: "uhpm", level: 0, value: "6.5" },
      { id: "h001", field: "ugol", level: 0, value: "205" },
      { id: "h001", field: "uhpm", level: 0, value: "7.5" },
    ]);
  });

  it("sets a text cell to a plain value", () => {
    expect(bulkEdits(sel(0, 2, 1, 2), rows(), columns, fields, "Grunt", null)).toEqual([
      { id: "h000", field: "unam", level: 0, value: "Grunt" },
      { id: "h001", field: "unam", level: 0, value: "Grunt" },
    ]);
  });

  it("calculates in number columns and sets the text as typed in a text column", () => {
    expect(bulkEdits(sel(0, 1, 0, 2), rows(), columns, fields, "+5", null)).toEqual([
      { id: "h000", field: "uhpm", level: 0, value: "6.5" },
      { id: "h000", field: "unam", level: 0, value: "+5" },
    ]);
  });

  it("does not read the text as a calculation in a column that is not a number", () => {
    const kinds: FieldInfo[] = [
      info("unam", "Name", "string"),
      info("umvt", "Movement type", "choice"),
      info("uabi", "Abilities", "objectList"),
      info("ucut", "Upgrades to", "objectRef"),
    ];
    const cols: Column[] = kinds.map((field) => ({ field: field.id, level: 0 }));
    const input = [row("h000", ["Footman", "foot", "Adef", "h001"])];
    const all = sel(0, 0, 0, 3);
    const set = (text: string) =>
      (bulkEdits(all, input, cols, kinds, text, null) as { value: string | null }[]).map(
        (edit) => edit.value,
      );
    expect(set("x001")).toEqual(["x001", "x001", "x001", "x001"]);
    expect(set("-1")).toEqual(["-1", "-1", "-1", "-1"]);
    expect(set("*2")).toEqual(["*2", "*2", "*2", "*2"]);
    expect(set("+10%")).toEqual(["+10%", "+10%", "+10%", "+10%"]);
  });

  it("trims a plain value and drops one = in front, as for numbers", () => {
    const one = sel(0, 2, 0, 2);
    const set = (text: string) =>
      (bulkEdits(one, rows(), columns, fields, text, null) as CellEdit[])[0]?.value;
    expect(set("=Footman")).toBe("Footman");
    expect(set("  Footman  ")).toBe("Footman");
    expect(set(" = Footman ")).toBe("Footman");
    expect(set("==x")).toBe("=x");
    expect(set("=50")).toBe("50");
    expect(set("Foot man")).toBe("Foot man");
  });

  it("takes yes/no and a choice's value or label, and sends the value", () => {
    const set = (col: number, text: string) =>
      bulkEdits(sel(0, col, 1, col), choiceRows(), choiceColumns, choiceFields, text, choices);
    const values = (col: number, text: string) =>
      (set(col, text) as CellEdit[]).map((edit) => edit.value);
    for (const yes of ["1", "yes", "Yes", "TRUE", " true "])
      expect(values(0, yes)).toEqual(["1", "1"]);
    for (const no of ["0", "no", "NO", "False", "=0"]) expect(values(0, no)).toEqual(["0", "0"]);
    expect(values(1, "normal")).toEqual(["normal", "normal"]);
    expect(values(1, "Normal")).toEqual(["normal", "normal"]);
    expect(values(1, "None")).toEqual(["unknown", "unknown"]);
    expect(values(1, "=Pierce")).toEqual(["pierce", "pierce"]);
    expect(values(2, "Fortified")).toEqual(["fort", "fort"]);
    // A value comes before a label: "none" is Unarmored's value, not a label.
    expect(values(2, "none")).toEqual(["none", "none"]);
    expect(values(3, "ALIV")).toEqual(["alive", "alive"]);
    // A list that is not known takes the text, as does a flags list.
    expect(values(5, "Hover")).toEqual(["Hover", "Hover"]);
    expect(values(4, "ground,air")).toEqual(["ground,air", "ground,air"]);
  });

  it("refuses another value for yes/no or a choice, naming the first cell", () => {
    const set = (col: number, text: string) =>
      bulkEdits(sel(0, col, 1, col), choiceRows(), choiceColumns, choiceFields, text, choices);
    expect(set(0, "7")).toEqual({
      error: `Name h000 · Is a building: ${t("grid.takesYesNo", { text: "7" })}`,
    });
    expect(set(0, "y")).toHaveProperty("error");
    expect(set(1, "Bogus")).toEqual({
      error: `Name h000 · Attack type: ${t("grid.notOneOfValues", { text: "Bogus", values: "None, Normal, Pierce" })}`,
    });
    expect(set(2, "Fortress")).toHaveProperty("error");
    // With no choice lists loaded, a choice takes the text; a yes/no field still refuses.
    expect(
      bulkEdits(sel(0, 1, 0, 1), choiceRows(), choiceColumns, choiceFields, "Bogus", null),
    ).toEqual([{ id: "h000", field: "ua1t", level: 0, value: "Bogus" }]);
    expect(
      bulkEdits(sel(0, 0, 0, 0), choiceRows(), choiceColumns, choiceFields, "7", null),
    ).toHaveProperty("error");
  });

  it("keeps the expressions for number columns", () => {
    const one = sel(0, 0, 0, 0);
    expect(bulkEdits(one, rows(), columns, fields, "x2", null)).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "200" },
    ]);
    expect(bulkEdits(one, rows(), columns, fields, "=50", null)).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "50" },
    ]);
  });

  it("refuses as a whole when a cell does not apply, naming the first by row and column", () => {
    let input = rows();
    input = patched(input, 1, 1, cell("", { applies: false }));
    input = patched(input, 2, 0, cell("", { applies: false }));
    const result = bulkEdits(sel(0, 0, 2, 1), input, columns, fields, "+5", null);
    expect(result).toHaveProperty("error");
    const message = (result as { error: string }).error;
    expect(message).toContain("Name h001");
    expect(message).toContain("Hit points");
  });

  it("refuses when a row is read only", () => {
    const input = rows();
    input[0] = row("h000", ["100", "1.5", "Footman"], { readOnly: "The file is damaged." });
    const result = bulkEdits(sel(0, 0, 0, 0), input, columns, fields, "5", null);
    expect(result).toHaveProperty("error");
    expect((result as { error: string }).error).toContain("Name h000");
  });

  it("refuses when the expression is bad or a result is not allowed", () => {
    const one = sel(0, 0, 0, 0);
    expect(bulkEdits(one, rows(), columns, fields, "", null)).toHaveProperty("error");
    expect(bulkEdits(one, rows(), columns, fields, "/0", null)).toHaveProperty("error");
    expect(bulkEdits(one, rows(), columns, fields, "abc", null)).toHaveProperty("error");
  });

  it("refuses a selection outside the table", () => {
    expect(bulkEdits(sel(0, 0, 5, 0), rows(), columns, fields, "5", null)).toHaveProperty("error");
  });

  it("names the level of a per-level column in a message", () => {
    let input = [row("A000", [""])];
    input = patched(input, 0, 0, cell("", { applies: false }));
    const result = bulkEdits(
      sel(0, 0, 0, 0),
      input,
      [{ field: "acdn", level: 2 }],
      [info("acdn", "Cooldown", "float", true)],
      "5",
      null,
    );
    expect((result as { error: string }).error).toContain("Cooldown · 2");
  });
});

describe("defaultColumns", () => {
  it("has the columns the spec names", () => {
    expect(defaultColumns.unit.map((c) => c.field)).toEqual([
      "ugol",
      "ulum",
      "uhpm",
      "ua1b",
      "udef",
      "ua1c",
    ]);
    expect(defaultColumns.item.map((c) => c.field)).toEqual(["igol", "ilum", "ilev"]);
    expect(defaultColumns.ability).toEqual([
      { field: "amcs", level: 1 },
      { field: "acdn", level: 1 },
      { field: "aran", level: 1 },
    ]);
    expect(defaultColumns.destructable).toEqual([{ field: "bhps", level: 0 }]);
    expect(defaultColumns.doodad).toEqual([]);
    expect(defaultColumns.buff).toEqual([]);
    expect(defaultColumns.upgrade).toEqual([]);
  });
});

describe("moveSelection", () => {
  const size = { rows: 3, cols: 4 };

  it("starts at the top left when nothing is selected", () => {
    expect(moveSelection(null, 1, 0, false, size)).toEqual(sel(0, 0, 0, 0));
  });

  it("moves the focus and leaves one cell selected", () => {
    expect(moveSelection(sel(0, 0, 1, 1), 1, 1, false, size)).toEqual(sel(2, 2, 2, 2));
  });

  it("makes the block bigger from the anchor with extend", () => {
    expect(moveSelection(sel(1, 1, 1, 1), 1, -1, true, size)).toEqual(sel(1, 1, 2, 0));
  });

  it("stops at the table's edge, also for a move to the edge", () => {
    expect(moveSelection(sel(2, 3, 2, 3), 1, 1, false, size)).toEqual(sel(2, 3, 2, 3));
    expect(
      moveSelection(
        sel(1, 1, 1, 1),
        Number.POSITIVE_INFINITY,
        Number.NEGATIVE_INFINITY,
        false,
        size,
      ),
    ).toEqual(sel(2, 0, 2, 0));
  });

  it("selects nothing in an empty table", () => {
    expect(moveSelection(sel(0, 0, 0, 0), 1, 0, false, { rows: 0, cols: 4 })).toBeNull();
    expect(moveSelection(null, 1, 0, false, { rows: 3, cols: 0 })).toBeNull();
  });
});

describe("selectAll", () => {
  it("selects every shown cell, or nothing in an empty table", () => {
    expect(selectAll({ rows: 3, cols: 4 })).toEqual(sel(0, 0, 2, 3));
    expect(selectAll({ rows: 0, cols: 4 })).toBeNull();
  });
});

describe("selectionRef and relocateSelection", () => {
  // A block from h000 · ugol to h001 · uhpm, kept by object and column.
  const ref = () => selectionRef(sel(0, 0, 1, 1), rows(), columns);
  const reversed = () => [...rows()].reverse();

  it("keeps the block on its objects when a sort keeps its rows together", () => {
    expect(relocateSelection(ref(), reversed(), columns)).toEqual(sel(2, 0, 1, 1));
  });

  it("keeps the block on its columns after a column is added to the left", () => {
    const wider: Column[] = [{ field: "ulum", level: 0 }, ...columns];
    expect(relocateSelection(ref(), rows(), wider)).toEqual(sel(0, 1, 1, 2));
  });

  it("never widens the block: a sort that puts other rows between its own collapses it to the focus", () => {
    // Sorted by gold: A100 B150 C200 D210 E300, A..C selected; +100% gives A200 B300 C400, sorted again
    // A200 D210 B300 E300 C400.
    const gold = (id: string, value: string) => row(id, [value, "1", id]);
    const before = [gold("A", "100"), gold("B", "150"), gold("C", "200"), gold("D", "210")];
    before.push(gold("E", "300"));
    const kept = selectionRef(sel(0, 0, 2, 0), before, columns);
    const after = [gold("A", "200"), gold("D", "210"), gold("B", "300"), gold("E", "300")];
    after.push(gold("C", "400"));
    expect(relocateSelection(kept, after, columns)).toEqual(sel(4, 0, 4, 0));
  });

  it("collapses the block to the focus when a column is inserted inside it", () => {
    const inside: Column[] = [
      columns[0] as Column,
      { field: "ulum", level: 0 },
      ...columns.slice(1),
    ];
    expect(relocateSelection(ref(), rows(), inside)).toEqual(sel(1, 2, 1, 2));
  });

  it("collapses the block to the focus when a row of it is gone", () => {
    const withoutAnchor = rows().filter((item) => item.id !== "h000");
    expect(relocateSelection(ref(), withoutAnchor, columns)).toEqual(sel(0, 1, 0, 1));
  });

  it("falls back to the focus's old place, pulled inside the table, when its row is gone", () => {
    const fewer = rows().filter((item) => item.id !== "h001");
    expect(relocateSelection(ref(), fewer, columns)).toEqual(sel(1, 1, 1, 1));
    expect(relocateSelection(ref(), fewer.slice(0, 1), columns)).toEqual(sel(0, 1, 0, 1));
  });

  it("falls back to the focus's old place, pulled inside the table, when its column is gone", () => {
    const fewer: Column[] = [columns[0] as Column, columns[2] as Column];
    expect(relocateSelection(ref(), rows(), fewer)).toEqual(sel(1, 1, 1, 1));
    expect(relocateSelection(ref(), rows(), fewer.slice(0, 1))).toEqual(sel(1, 0, 1, 0));
  });

  it("selects nothing with nothing kept or in an empty table", () => {
    expect(relocateSelection(null, rows(), columns)).toBeNull();
    expect(relocateSelection(ref(), [], columns)).toBeNull();
    expect(relocateSelection(ref(), rows(), [])).toBeNull();
  });

  it("tells whether two selections cover the same cells the same way", () => {
    expect(sameSelection(sel(0, 0, 1, 1), sel(0, 0, 1, 1))).toBe(true);
    expect(sameSelection(sel(0, 0, 1, 1), sel(1, 1, 0, 0))).toBe(false);
    expect(sameSelection(null, null)).toBe(true);
    expect(sameSelection(sel(0, 0, 0, 0), null)).toBe(false);
  });
});

describe("topLeft and blockSize", () => {
  it("give the block's upper left cell and its size whichever way it was made", () => {
    expect(topLeft(sel(2, 3, 0, 1))).toEqual({ row: 0, col: 1 });
    expect(blockSize(sel(2, 3, 0, 1))).toEqual({ rows: 3, cols: 3 });
  });
});

describe("copyBlock", () => {
  it("copies the values as the data spells them, a cell that does not apply as empty", () => {
    let input = rows();
    input = patched(input, 1, 0, cell("", { applies: false }));
    expect(copyBlock(sel(1, 1, 0, 0), input)).toEqual([
      ["100", "1.5"],
      ["", "2.5"],
    ]);
  });
});

describe("cellField", () => {
  it("gives the details view's field for a cell, at the column's level", () => {
    const field = cellField(
      { ...info("acdn", "Cooldown", "float", true), choice: null, listOf: null },
      { field: "acdn", level: 2 },
      cell("8", { base: "10", changed: true, warning: "Too long." }),
    );
    expect(field).toEqual({
      id: "acdn",
      label: "Cooldown",
      category: "stats",
      sort: "",
      type: "float",
      choice: null,
      listOf: null,
      level: 2,
      value: "8",
      base: "10",
      changed: true,
      skin: false,
      warning: "Too long.",
      modes: null,
    });
  });
});

describe("editRefusal", () => {
  it("says why a cell cannot be edited: a read only row, or a field that does not apply", () => {
    const column: Column = { field: "uhpm", level: 0 };
    const readOnly = row("h000", ["1"], { readOnly: "The file is damaged." });
    expect(editRefusal(readOnly, cell("1"), column, fields)).toBe(
      t("grid.readOnly", { reason: "The file is damaged." }),
    );
    expect(editRefusal(row("h000", [""]), cell("", { applies: false }), column, fields)).toBe(
      t("grid.doesNotApplyTo", { field: "Hit points", object: "Name h000" }),
    );
    expect(editRefusal(row("h000", ["1"]), cell("1"), column, fields)).toBeNull();
  });
});
