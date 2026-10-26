import { describe, expect, it } from "vitest";
import type { Column, FieldInfo, TableCell, TableRow } from "./api.ts";
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

const rows = (): TableRow[] => [
  row("h000", ["100", "1.5", "Footman"]),
  row("h001", ["200", "2.5", "Rifleman"]),
  row("h002", ["300", "3.5", "Knight"]),
];

const sel = (r1: number, c1: number, r2: number, c2: number) => ({
  anchor: { row: r1, col: c1 },
  focus: { row: r2, col: c2 },
});

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
    expect(pasteEdits({ row: 1, col: 0 }, block, rows(), columns, fields)).toEqual([
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
    expect(pasteEdits({ row: 2, col: 2 }, block, rows(), columns, fields)).toEqual([
      { id: "h002", field: "unam", level: 0, value: "a" },
    ]);
  });

  it("carries the column's level", () => {
    expect(
      pasteEdits({ row: 0, col: 0 }, [["5"]], rows(), [{ field: "acdn", level: 2 }], []),
    ).toEqual([{ id: "h000", field: "acdn", level: 2, value: "5" }]);
  });

  it("skips cells that do not apply and rows that are read only", () => {
    let input = rows();
    input = patched(input, 0, 1, cell("", { applies: false }));
    input[1] = row("h001", ["200", "2.5", "Rifleman"], { readOnly: "Read only." });
    const block = [
      ["1", "2"],
      ["3", "4"],
      ["5", "6"],
    ];
    expect(pasteEdits({ row: 0, col: 0 }, block, input, columns, fields)).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "1" },
      { id: "h002", field: "ugol", level: 0, value: "5" },
      { id: "h002", field: "uhpm", level: 0, value: "6" },
    ]);
  });

  it("skips an empty cell for a number column and writes it for a text column", () => {
    const block = [
      ["", "", ""],
      ["7", "", "x"],
    ];
    expect(pasteEdits({ row: 0, col: 0 }, block, rows(), columns, fields)).toEqual([
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
    expect(pasteEdits({ row: 0, col: 0 }, [["", "", "", "", ""]], input, cols, more)).toEqual([
      { id: "h000", field: "c", level: 0, value: "" },
      { id: "h000", field: "d", level: 0, value: "" },
    ]);
  });

  it("gives nothing for a start outside the table", () => {
    expect(pasteEdits({ row: 5, col: 0 }, [["1"]], rows(), columns, fields)).toEqual([]);
  });
});

describe("fillDownEdits", () => {
  it("copies the top cell of each column down the selection", () => {
    expect(fillDownEdits(sel(0, 0, 2, 1), rows(), columns)).toEqual([
      { id: "h001", field: "ugol", level: 0, value: "100" },
      { id: "h001", field: "uhpm", level: 0, value: "1.5" },
      { id: "h002", field: "ugol", level: 0, value: "100" },
      { id: "h002", field: "uhpm", level: 0, value: "1.5" },
    ]);
  });

  it("works from the top whichever way the selection was made", () => {
    expect(fillDownEdits(sel(2, 2, 1, 2), rows(), columns)).toEqual([
      { id: "h002", field: "unam", level: 0, value: "Rifleman" },
    ]);
  });

  it("skips destinations that do not apply or are read only", () => {
    let input = rows();
    input = patched(input, 1, 0, cell("", { applies: false }));
    input[2] = row("h002", ["300", "3.5", "Knight"], { readOnly: "Read only." });
    expect(fillDownEdits(sel(0, 0, 2, 1), input, columns)).toEqual([
      { id: "h001", field: "uhpm", level: 0, value: "1.5" },
    ]);
  });

  it("skips a column whose top cell does not apply", () => {
    let input = rows();
    input = patched(input, 0, 1, cell("", { applies: false }));
    expect(fillDownEdits(sel(0, 1, 2, 1), input, columns)).toEqual([]);
  });

  it("gives nothing for a single row", () => {
    expect(fillDownEdits(sel(1, 0, 1, 2), rows(), columns)).toEqual([]);
  });
});

describe("resetEdits", () => {
  it("resets each selected cell with a null value", () => {
    expect(resetEdits(sel(0, 1, 1, 2), rows(), columns)).toEqual([
      { id: "h000", field: "uhpm", level: 0, value: null },
      { id: "h000", field: "unam", level: 0, value: null },
      { id: "h001", field: "uhpm", level: 0, value: null },
      { id: "h001", field: "unam", level: 0, value: null },
    ]);
  });

  it("skips cells that do not apply and rows that are read only", () => {
    let input = rows();
    input = patched(input, 0, 0, cell("", { applies: false }));
    input[1] = row("h001", ["200", "2.5", "Rifleman"], { readOnly: "Read only." });
    expect(resetEdits(sel(0, 0, 1, 0), input, columns)).toEqual([]);
  });
});

describe("bulkEdits", () => {
  it("applies +5 over a block, one edit per cell", () => {
    expect(bulkEdits(sel(0, 0, 1, 1), rows(), columns, fields, "+5")).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "105" },
      { id: "h000", field: "uhpm", level: 0, value: "6.5" },
      { id: "h001", field: "ugol", level: 0, value: "205" },
      { id: "h001", field: "uhpm", level: 0, value: "7.5" },
    ]);
  });

  it("sets a text cell to a plain value", () => {
    expect(bulkEdits(sel(0, 2, 1, 2), rows(), columns, fields, "Grunt")).toEqual([
      { id: "h000", field: "unam", level: 0, value: "Grunt" },
      { id: "h001", field: "unam", level: 0, value: "Grunt" },
    ]);
  });

  it("calculates in number columns and sets the text as typed in a text column", () => {
    expect(bulkEdits(sel(0, 1, 0, 2), rows(), columns, fields, "+5")).toEqual([
      { id: "h000", field: "uhpm", level: 0, value: "6.5" },
      { id: "h000", field: "unam", level: 0, value: "+5" },
    ]);
  });

  it("does not read the text as a calculation in a column that is not a number", () => {
    const kinds: FieldInfo[] = [
      info("unam", "Name", "string"),
      info("ubdg", "Is a building", "bool"),
      info("umvt", "Movement type", "choice"),
      info("uabi", "Abilities", "objectList"),
      info("ucut", "Upgrades to", "objectRef"),
    ];
    const cols: Column[] = kinds.map((field) => ({ field: field.id, level: 0 }));
    const input = [row("h000", ["Footman", "0", "foot", "Adef", "h001"])];
    const all = sel(0, 0, 0, 4);
    const set = (text: string) =>
      (bulkEdits(all, input, cols, kinds, text) as { value: string | null }[]).map(
        (edit) => edit.value,
      );
    expect(set("x001")).toEqual(["x001", "x001", "x001", "x001", "x001"]);
    expect(set("-1")).toEqual(["-1", "-1", "-1", "-1", "-1"]);
    expect(set("=50")).toEqual(["=50", "=50", "=50", "=50", "=50"]);
    expect(set("*2")).toEqual(["*2", "*2", "*2", "*2", "*2"]);
  });

  it("keeps the expressions for number columns", () => {
    const one = sel(0, 0, 0, 0);
    expect(bulkEdits(one, rows(), columns, fields, "x2")).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "200" },
    ]);
    expect(bulkEdits(one, rows(), columns, fields, "=50")).toEqual([
      { id: "h000", field: "ugol", level: 0, value: "50" },
    ]);
  });

  it("refuses as a whole when a cell does not apply, naming the first by row and column", () => {
    let input = rows();
    input = patched(input, 1, 1, cell("", { applies: false }));
    input = patched(input, 2, 0, cell("", { applies: false }));
    const result = bulkEdits(sel(0, 0, 2, 1), input, columns, fields, "+5");
    expect(result).toHaveProperty("error");
    const message = (result as { error: string }).error;
    expect(message).toContain("Name h001");
    expect(message).toContain("Hit points");
  });

  it("refuses when a row is read only", () => {
    const input = rows();
    input[0] = row("h000", ["100", "1.5", "Footman"], { readOnly: "The file is damaged." });
    const result = bulkEdits(sel(0, 0, 0, 0), input, columns, fields, "5");
    expect(result).toHaveProperty("error");
    expect((result as { error: string }).error).toContain("Name h000");
  });

  it("refuses when the expression is bad or a result is not allowed", () => {
    const one = sel(0, 0, 0, 0);
    expect(bulkEdits(one, rows(), columns, fields, "")).toHaveProperty("error");
    expect(bulkEdits(one, rows(), columns, fields, "/0")).toHaveProperty("error");
    expect(bulkEdits(one, rows(), columns, fields, "abc")).toHaveProperty("error");
  });

  it("refuses a selection outside the table", () => {
    expect(bulkEdits(sel(0, 0, 5, 0), rows(), columns, fields, "5")).toHaveProperty("error");
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

  it("finds the same objects and columns after the rows are sorted again", () => {
    expect(relocateSelection(ref(), reversed(), columns)).toEqual(sel(2, 0, 1, 1));
  });

  it("finds the same columns after a column is added to the left", () => {
    const wider: Column[] = [{ field: "ulum", level: 0 }, ...columns];
    expect(relocateSelection(ref(), rows(), wider)).toEqual(sel(0, 1, 1, 2));
  });

  it("falls back to the old place, pulled inside the table, for a row that is gone", () => {
    const fewer = rows().filter((item) => item.id !== "h001");
    expect(relocateSelection(ref(), fewer, columns)).toEqual(sel(0, 0, 1, 1));
    expect(relocateSelection(ref(), fewer.slice(0, 1), columns)).toEqual(sel(0, 0, 0, 1));
  });

  it("falls back to the old place, pulled inside the table, for a column that is gone", () => {
    const fewer: Column[] = [columns[0] as Column, columns[2] as Column];
    expect(relocateSelection(ref(), rows(), fewer)).toEqual(sel(0, 0, 1, 1));
    expect(relocateSelection(ref(), rows(), fewer.slice(0, 1))).toEqual(sel(0, 0, 1, 0));
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
      "Read only: The file is damaged.",
    );
    expect(editRefusal(row("h000", [""]), cell("", { applies: false }), column, fields)).toBe(
      "Hit points does not apply to Name h000.",
    );
    expect(editRefusal(row("h000", ["1"]), cell("1"), column, fields)).toBeNull();
  });
});
