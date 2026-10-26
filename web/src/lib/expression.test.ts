import { describe, expect, it } from "vitest";
import { applyExpression, parseExpression } from "./expression.ts";

describe("parseExpression", () => {
  it("reads a plain value and = as set", () => {
    expect(parseExpression("50")).toEqual({ op: "set", operand: "50" });
    expect(parseExpression("=50")).toEqual({ op: "set", operand: "50" });
    expect(parseExpression("  = 2.5 ")).toEqual({ op: "set", operand: "2.5" });
    expect(parseExpression("Footman")).toEqual({ op: "set", operand: "Footman" });
    expect(parseExpression("=-5")).toEqual({ op: "set", operand: "-5" });
    expect(parseExpression("=")).toEqual({ op: "set", operand: "" });
  });

  it("reads +, -, *, x, × and / with a number", () => {
    expect(parseExpression("+5")).toEqual({ op: "add", operand: "5" });
    expect(parseExpression("-5")).toEqual({ op: "sub", operand: "5" });
    expect(parseExpression("*1.1")).toEqual({ op: "mul", operand: "1.1" });
    expect(parseExpression("x1.1")).toEqual({ op: "mul", operand: "1.1" });
    expect(parseExpression("×1.1")).toEqual({ op: "mul", operand: "1.1" });
    expect(parseExpression("/2")).toEqual({ op: "div", operand: "2" });
    expect(parseExpression("+.5")).toEqual({ op: "add", operand: ".5" });
  });

  it("reads a percent change with its sign", () => {
    expect(parseExpression("+10%")).toEqual({ op: "percent", operand: "10" });
    expect(parseExpression("-10%")).toEqual({ op: "percent", operand: "-10" });
  });

  it("keeps text that is not an expression as a plain value", () => {
    expect(parseExpression("xavier")).toEqual({ op: "set", operand: "xavier" });
    expect(parseExpression("+abc")).toEqual({ op: "set", operand: "+abc" });
    expect(parseExpression("10%")).toEqual({ op: "set", operand: "10%" });
  });

  it("refuses empty text", () => {
    expect(parseExpression("")).toHaveProperty("error");
    expect(parseExpression("   ")).toHaveProperty("error");
  });
});

describe("applyExpression", () => {
  it("sets a number field to a plain number", () => {
    expect(applyExpression({ op: "set", operand: "50" }, "10", "int")).toBe("50");
    expect(applyExpression({ op: "set", operand: "+50" }, "10", "int")).toBe("50");
    expect(applyExpression({ op: "set", operand: "1.5" }, "10", "float")).toBe("1.5");
  });

  it("refuses text, exponent, hex and non-finite forms in a number field", () => {
    for (const operand of ["abc", "1e3", "0x10", "NaN", "Infinity", "", "1,5"]) {
      expect(applyExpression({ op: "set", operand }, "10", "float")).toHaveProperty("error");
    }
  });

  it("refuses a fraction for a whole-number field when setting", () => {
    expect(applyExpression({ op: "set", operand: "1.5" }, "10", "int")).toHaveProperty("error");
  });

  it("adds, subtracts, multiplies and divides", () => {
    expect(applyExpression({ op: "add", operand: "5" }, "10", "int")).toBe("15");
    expect(applyExpression({ op: "sub", operand: "5" }, "10", "int")).toBe("5");
    expect(applyExpression({ op: "mul", operand: "1.1" }, "10", "float")).toBe("11");
    expect(applyExpression({ op: "div", operand: "2" }, "10", "int")).toBe("5");
  });

  it("rounds a whole-number result to the nearest, halves away from zero", () => {
    expect(applyExpression({ op: "div", operand: "2" }, "5", "int")).toBe("3");
    expect(applyExpression({ op: "div", operand: "2" }, "-5", "int")).toBe("-3");
    expect(applyExpression({ op: "percent", operand: "10" }, "135", "int")).toBe("149");
    expect(applyExpression({ op: "percent", operand: "-10" }, "135", "int")).toBe("122");
  });

  it("rounds a decimal result to 4 places and drops trailing zeros", () => {
    expect(applyExpression({ op: "percent", operand: "10" }, "1.35", "float")).toBe("1.485");
    expect(applyExpression({ op: "mul", operand: "1.1" }, "1.35", "float")).toBe("1.485");
    expect(applyExpression({ op: "div", operand: "3" }, "1", "float")).toBe("0.3333");
    expect(applyExpression({ op: "add", operand: "0.5" }, "1.5", "float")).toBe("2");
    expect(applyExpression({ op: "sub", operand: "1" }, "1", "float")).toBe("0");
    expect(applyExpression({ op: "mul", operand: "0" }, "-1.5", "float")).toBe("0");
  });

  it("refuses dividing by zero", () => {
    expect(applyExpression({ op: "div", operand: "0" }, "10", "int")).toHaveProperty("error");
    expect(applyExpression({ op: "div", operand: "0.0" }, "10", "float")).toHaveProperty("error");
  });

  it("refuses a whole-number result outside the 32-bit range", () => {
    expect(applyExpression({ op: "add", operand: "1" }, "2147483647", "int")).toHaveProperty(
      "error",
    );
    expect(applyExpression({ op: "sub", operand: "1" }, "-2147483648", "int")).toHaveProperty(
      "error",
    );
    expect(applyExpression({ op: "add", operand: "1" }, "2147483646", "int")).toBe("2147483647");
  });

  it("refuses a current value that is not a plain number", () => {
    expect(applyExpression({ op: "add", operand: "1" }, "abc", "int")).toHaveProperty("error");
    expect(applyExpression({ op: "add", operand: "1" }, "", "float")).toHaveProperty("error");
  });

  it("refuses expressions on text and choice fields", () => {
    for (const type of ["bool", "string", "choice", "objectList", "objectRef"]) {
      expect(applyExpression({ op: "add", operand: "5" }, "10", type)).toHaveProperty("error");
      expect(applyExpression({ op: "percent", operand: "5" }, "10", type)).toHaveProperty("error");
    }
  });

  it("sets a text, choice or list field to the plain value", () => {
    for (const type of ["bool", "string", "choice", "objectList", "objectRef"]) {
      expect(applyExpression({ op: "set", operand: "hfoo" }, "old", type)).toBe("hfoo");
    }
    expect(applyExpression({ op: "set", operand: "" }, "old", "string")).toBe("");
  });
});
