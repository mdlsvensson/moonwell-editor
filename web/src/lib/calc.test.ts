import { describe, expect, it } from "vitest";
import { calculate, isArithmetic } from "./calc.ts";

describe("calculate", () => {
  it("works out sums, products and brackets in their order", () => {
    expect(calculate("10*2+5")).toBe(25);
    expect(calculate("10+2*5")).toBe(20);
    expect(calculate("(3+4)/2")).toBe(3.5);
    expect(calculate("100 - 15 - 5")).toBe(80);
    expect(calculate("8/2/2")).toBe(2);
    expect(calculate("3 x 4")).toBe(12);
  });

  it("reads a sign in front as the number's", () => {
    expect(calculate("-5")).toBe(-5);
    expect(calculate("+5")).toBe(5);
    expect(calculate("2*-3")).toBe(-6);
  });

  it("works on the value held when it starts with * or /", () => {
    expect(calculate("*2", 420)).toBe(840);
    expect(calculate("/4", 420)).toBe(105);
  });

  it("refuses what is not arithmetic, or divides by 0", () => {
    expect(calculate("ten")).toBeNull();
    expect(calculate("5+")).toBeNull();
    expect(calculate("(5")).toBeNull();
    expect(calculate("5/0")).toBeNull();
    expect(calculate("")).toBeNull();
  });
});

describe("isArithmetic", () => {
  it("is true for a calculation, false for a plain number", () => {
    expect(isArithmetic("10*2")).toBe(true);
    expect(isArithmetic("*2")).toBe(true);
    expect(isArithmetic("-5")).toBe(false);
    expect(isArithmetic("1.5")).toBe(false);
    expect(isArithmetic("abc")).toBe(false);
  });
});
