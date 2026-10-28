import { describe, expect, it } from "vitest";
import type { FieldView } from "./api.ts";
import {
  diceRange,
  parseBound,
  rangeEdits,
  rangeOf,
  rangeText,
  solveDice,
  totalLabel,
  withTotals,
} from "./dice.ts";
import type { Row } from "./fields.ts";

function field(id: string, value: string, label = id): FieldView {
  return {
    id,
    label,
    category: "combat",
    sort: id,
    type: "int",
    choice: null,
    listOf: null,
    level: 0,
    value,
    base: value,
    changed: false,
    skin: false,
    warning: null,
    modes: null,
  };
}

function row(view: FieldView): Row {
  return { id: view.id, label: view.label, sort: view.sort, field: view, levels: null };
}

// A Footman's first attack: 11 + 1d2.
const footman = {
  base: field("ua1b", "11", "Attack 1 - Damage Base"),
  dice: field("ua1d", "1"),
  sides: field("ua1s", "2"),
};

describe("diceRange", () => {
  it("is the base plus each die from 1 to its sides", () => {
    expect(diceRange(11, 1, 2)).toEqual({ min: 12, max: 13 });
    expect(diceRange(20, 3, 6)).toEqual({ min: 23, max: 38 });
  });

  it("is the base alone with no dice or dice of no sides", () => {
    expect(diceRange(5, 0, 6)).toEqual({ min: 5, max: 5 });
    expect(diceRange(5, 2, 0)).toEqual({ min: 5, max: 5 });
  });
});

describe("rangeText and parseBound", () => {
  it("write a range as the game does", () => {
    expect(rangeText({ min: 12, max: 13 })).toBe("12 - 13");
    expect(rangeText({ min: 7, max: 7 })).toBe("7");
  });

  it("read an end typed as a whole number, with or without spaces or a sign", () => {
    expect(parseBound("10")).toBe(10);
    expect(parseBound(" 15 ")).toBe(15);
    expect(parseBound("-5")).toBe(-5);
    expect(parseBound("+3")).toBe(3);
  });

  it("refuse an end that is not a whole number", () => {
    expect(parseBound("ten")).toBeNull();
    expect(parseBound("")).toBeNull();
    expect(parseBound("12 - 13")).toBeNull();
    expect(parseBound("1.5")).toBeNull();
  });
});

describe("solveDice", () => {
  it("keeps the dice there are when they split the range", () => {
    expect(solveDice({ min: 10, max: 39 }, 1)).toEqual({ base: 9, dice: 1, sides: 30 });
    expect(solveDice({ min: 23, max: 38 }, 3)).toEqual({ base: 20, dice: 3, sides: 6 });
  });

  it("takes fewer dice when the ones there are do not split the range", () => {
    // 4 dice cannot make a spread of 6; 3 can (3 dice of 3 sides).
    expect(solveDice({ min: 10, max: 16 }, 4)).toEqual({ base: 7, dice: 3, sides: 3 });
  });

  it("gives one number with dice of 1 side, and a total with no dice 1 die", () => {
    expect(solveDice({ min: 15, max: 15 }, 2)).toEqual({ base: 13, dice: 2, sides: 1 });
    expect(solveDice({ min: 4, max: 9 }, 0)).toEqual({ base: 3, dice: 1, sides: 6 });
  });

  it("always rolls the range asked for", () => {
    for (const [min, max, dice] of [
      [1, 100, 3],
      [12, 13, 1],
      [-4, 8, 5],
      [50, 50, 0],
    ] as const) {
      const solved = solveDice({ min, max }, dice);
      expect(diceRange(solved.base, solved.dice, solved.sides)).toEqual({ min, max });
    }
  });
});

describe("rangeEdits", () => {
  it("changes only the fields that differ", () => {
    // 12 - 20 from 11 + 1d2: the base stays, the sides become 9.
    expect(
      rangeEdits(footman, { min: 12, max: 20 }).map((edit) => [edit.field.id, edit.value]),
    ).toEqual([["ua1s", "9"]]);
  });

  it("changes nothing for the range it already rolls", () => {
    expect(rangeOf(footman)).toEqual({ min: 12, max: 13 });
    expect(rangeEdits(footman, { min: 12, max: 13 })).toEqual([]);
  });
});

describe("totalLabel", () => {
  it("drops Base from the base's name", () => {
    expect(totalLabel("Attack 1 - Damage Base")).toBe("Attack 1 - Damage");
    expect(totalLabel("Gold Bounty Awarded - Base")).toBe("Gold Bounty Awarded");
    expect(totalLabel("Damage")).toBe("Damage");
  });
});

describe("withTotals", () => {
  const cooldown = field("ua1c", "1.35", "Attack 1 - Cooldown Time");
  const fields = [cooldown, footman.base, footman.dice, footman.sides];

  it("gathers a total's fields into one row where the first was", () => {
    const rows = withTotals(fields.map(row), fields);
    expect(rows.map((each) => each.id)).toEqual(["ua1c", "dice:ua1b"]);
    expect(rows[1]?.label).toBe("Attack 1 - Damage");
    expect(rows[1]?.dice?.sides.id).toBe("ua1s");
  });

  it("holds all three fields when only one is shown", () => {
    const rows = withTotals([row(cooldown), row(footman.sides)], fields);
    expect(rows.map((each) => each.id)).toEqual(["ua1c", "dice:ua1b"]);
    expect(rows[1]?.dice?.base.value).toBe("11");
  });

  it("leaves the rows when a field of the total is missing", () => {
    const partial = [cooldown, footman.base, footman.dice];
    expect(withTotals(partial.map(row), partial).map((each) => each.id)).toEqual([
      "ua1c",
      "ua1b",
      "ua1d",
    ]);
  });
});
