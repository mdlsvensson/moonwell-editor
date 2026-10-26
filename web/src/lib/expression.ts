/**
 * What the table's bar takes: a plain value or a small calculation (`50`, `+5`, `-10%`, `*1.1`, `/2`), read once
 * and then applied to each selected cell's current value.
 */
import type { FieldInfo } from "./api.ts";

/** What to do to a cell: set it to the operand, or change its number by it (`percent` is a change in percent). */
export type Expression = {
  op: "set" | "add" | "sub" | "mul" | "div" | "percent";
  operand: string;
};

// A plain decimal number: no exponent, hex, NaN or Infinity.
const plainNumber = /^[-+]?(\d+\.?\d*|\.\d+)$/;
const unsignedNumber = /^(\d+\.?\d*|\.\d+)$/;
const wholeNumber = /^[-+]?\d+$/;

const int32Min = -2147483648;
const int32Max = 2147483647;

const operators: Record<string, Expression["op"]> = {
  "+": "add",
  "-": "sub",
  "*": "mul",
  x: "mul",
  "×": "mul",
  "/": "div",
};

/**
 * Reads what was typed: `50` or `=50` sets, `+5` `-5` `*1.1` `x1.1` `×1.1` `/2` calculate, `+10%` and `-10%` change
 * by a percent. Text that is not one of these (such as `Footman`, or `+abc`) is a plain value to set; `=` in front
 * forces that, so `=-5` sets -5. The operand of a percent keeps its sign (`-10` for `-10%`). Empty text is an error.
 */
export function parseExpression(text: string): Expression | { error: string } {
  const typed = text.trim();
  if (typed === "") return { error: "Type a value or an expression such as +5, -10% or *1.1." };
  if (typed.startsWith("=")) return { op: "set", operand: typed.slice(1).trim() };

  const op = operators[typed.charAt(0)];
  if (op) {
    const rest = typed.slice(1).trim();
    if ((op === "add" || op === "sub") && rest.endsWith("%")) {
      const amount = rest.slice(0, -1).trim();
      if (unsignedNumber.test(amount)) {
        return { op: "percent", operand: op === "sub" ? `-${amount}` : amount };
      }
    }
    if (plainNumber.test(rest)) {
      return { op, operand: rest.startsWith("+") ? rest.slice(1) : rest };
    }
  }
  return { op: "set", operand: typed };
}

/** Rounds to the nearest whole number, halves away from zero, ignoring floating point noise. */
function roundWhole(value: number): number {
  const clean = Math.abs(Number(value.toPrecision(12)));
  return Math.sign(value) * Math.round(clean);
}

/** Rounds to 4 decimal places, halves away from zero, ignoring floating point noise. */
function roundFour(value: number): number {
  const scaled = Math.abs(Number((value * 10000).toPrecision(12)));
  return (Math.sign(value) * Math.round(scaled)) / 10000;
}

/**
 * The cell's new value as text. A number field (`int`, `float`) takes every kind of expression; other types take
 * only a plain value. A whole-number result is rounded to the nearest whole number (halves away from zero) and must
 * fit 32 bits; a decimal result is rounded to 4 places, trailing zeros dropped. Setting an `int` field takes a whole
 * number as typed (not a fraction), and a `float` field keeps the number as typed. Otherwise: what is wrong.
 */
export function applyExpression(
  e: Expression,
  current: string,
  type: FieldInfo["type"],
): string | { error: string } {
  const isInt = type === "int";
  if (!isInt && type !== "float") {
    if (e.op !== "set") {
      return { error: "Only number fields take a calculation; type a plain value." };
    }
    return e.operand;
  }

  if (e.op === "set") {
    if (isInt) {
      const number = Number(e.operand);
      if (!wholeNumber.test(e.operand) || number < int32Min || number > int32Max) {
        return { error: `"${e.operand}" is not a whole number in range.` };
      }
      return String(number);
    }
    if (!plainNumber.test(e.operand)) return { error: `"${e.operand}" is not a number.` };
    return e.operand;
  }

  const trimmed = current.trim();
  if (!plainNumber.test(trimmed)) return { error: `The value "${current}" is not a number.` };
  if (!plainNumber.test(e.operand)) return { error: `"${e.operand}" is not a number.` };
  const value = Number(trimmed);
  const operand = Number(e.operand);

  let result: number;
  switch (e.op) {
    case "add":
      result = value + operand;
      break;
    case "sub":
      result = value - operand;
      break;
    case "mul":
      result = value * operand;
      break;
    case "div":
      if (operand === 0) return { error: "Cannot divide by 0." };
      result = value / operand;
      break;
    default:
      result = value + (value * operand) / 100;
      break;
  }
  if (!Number.isFinite(result)) return { error: "The result is not a number." };

  if (isInt) {
    const whole = roundWhole(result);
    if (whole < int32Min || whole > int32Max) {
      return { error: `${whole} is outside the whole-number range.` };
    }
    return String(whole === 0 ? 0 : whole);
  }
  const rounded = roundFour(result);
  return String(rounded === 0 ? 0 : rounded);
}
