/**
 * Fields that roll dice, shown as one total (Sven, 2026-10-27): a unit's damage per attack and its gold and lumber
 * bounties are a base plus a number of dice with a number of sides each, which the game shows as a range (Damage 12 -
 * 13). The range is worked out from the three fields, and a lowest and highest typed in set them.
 */
import type { FieldView } from "./api.ts";
import type { Row } from "./fields.ts";

/** The three fields of a total: the base, the number of dice and the sides per die. */
export type DiceFields = { base: FieldView; dice: FieldView; sides: FieldView };

/** The ids of each total's fields, in the order the details panel shows the totals. */
export const diceGroups: { base: string; dice: string; sides: string }[] = [
  { base: "ua1b", dice: "ua1d", sides: "ua1s" },
  { base: "ua2b", dice: "ua2d", sides: "ua2s" },
  { base: "ubba", dice: "ubdi", sides: "ubsi" },
  { base: "ulba", dice: "ulbd", sides: "ulbs" },
];

/** A range of values, lowest and highest. */
export type Range = { min: number; max: number };

function whole(value: string): number {
  const number = Number.parseInt(value.trim(), 10);
  return Number.isNaN(number) ? 0 : number;
}

/**
 * The range the game rolls: the base plus each die at least 1 and at most its sides. No dice, or dice of no sides,
 * roll nothing: the base alone.
 */
export function diceRange(base: number, dice: number, sides: number): Range {
  if (dice <= 0 || sides <= 0) return { min: base, max: base };
  return { min: base + dice, max: base + dice * sides };
}

/** The range of a total's fields as they stand. */
export function rangeOf(fields: DiceFields): Range {
  return diceRange(whole(fields.base.value), whole(fields.dice.value), whole(fields.sides.value));
}

/** A range as the game writes it: `12 - 13`, or `12` when it is one number. */
export function rangeText(range: Range): string {
  return range.min === range.max ? String(range.min) : `${range.min} - ${range.max}`;
}

/** A range as typed: `12 - 13`, `12-13`, `12 to 13`, or one number; null when it is not one, or runs backwards. */
export function parseRange(text: string): Range | null {
  const match = text.trim().match(/^([+-]?\d+)(?:\s*(?:-|–|—|to)\s*([+-]?\d+))?$/i);
  if (!match) return null;
  const min = Number.parseInt(match[1] ?? "", 10);
  const max = match[2] === undefined ? min : Number.parseInt(match[2], 10);
  return min <= max ? { min, max } : null;
}

/**
 * The base, dice and sides that roll a range, keeping the number of dice there is when it can: a range of one number
 * is that many dice of 1 side; otherwise the most dice (up to the number there is) that split the range evenly, so a
 * Footman's 1 die of 2 sides stays 1 die. A total with no dice gets 1.
 */
export function solveDice(
  range: Range,
  dice: number,
): { base: number; dice: number; sides: number } {
  const keep = Math.max(1, dice);
  const spread = range.max - range.min;
  if (spread === 0) return { base: range.min - keep, dice: keep, sides: 1 };
  let count = Math.min(keep, spread);
  while (spread % count !== 0) count--;
  return { base: range.min - count, dice: count, sides: spread / count + 1 };
}

/**
 * The edits that make a total roll a range: only the fields whose value changes. Empty when the range is already
 * what it rolls.
 */
export function rangeEdits(
  fields: DiceFields,
  range: Range,
): { field: FieldView; value: string }[] {
  const now = rangeOf(fields);
  if (now.min === range.min && now.max === range.max) return [];
  const solved = solveDice(range, whole(fields.dice.value));
  return (
    [
      [fields.base, solved.base],
      [fields.dice, solved.dice],
      [fields.sides, solved.sides],
    ] as const
  )
    .filter(([field, value]) => whole(field.value) !== value || field.value.trim() === "")
    .map(([field, value]) => ({ field, value: String(value) }));
}

/** A total's name from its base's: `Attack 1 - Damage Base` is `Attack 1 - Damage`. */
export function totalLabel(baseLabel: string): string {
  return baseLabel.replace(/\s*-?\s*base$/i, "").trim() || baseLabel;
}

/** A row of the details panel that is a total: its fields; absent on any other row. */
export type DiceRow = Row & { dice?: DiceFields };

/**
 * A category's rows with each total's fields (any of them shown) gathered into one row where the first of them was,
 * holding all three from `fields` (also those the filter or "Changed only" left out). A total missing a field stays as
 * its rows.
 */
export function withTotals(rows: Row[], fields: readonly FieldView[]): DiceRow[] {
  let result: DiceRow[] = rows;
  for (const group of diceGroups) {
    const ids = [group.base, group.dice, group.sides];
    const inGroup = (row: Row) => row.levels === null && ids.includes(row.id);
    const first = result.find(inGroup);
    const find = (id: string) => fields.find((field) => field.id === id && field.level === 0);
    const base = find(group.base);
    const dice = find(group.dice);
    const sides = find(group.sides);
    if (!first || !base || !dice || !sides) continue;
    const total: DiceRow = {
      id: `dice:${group.base}`,
      label: totalLabel(base.label),
      sort: first.sort,
      field: base,
      levels: null,
      dice: { base, dice, sides },
    };
    result = result.flatMap((row) => (row === first ? [total] : inGroup(row) ? [] : [row]));
  }
  return result;
}
