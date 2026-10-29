import { describe, expect, it } from "vitest";
import { hexOf, hsvOf, parseHex } from "./colour.ts";

describe("parseHex", () => {
  it("reads a colour with or without its #, in any case", () => {
    expect(parseHex("#FFCC00")).toBe("#ffcc00");
    expect(parseHex(" 40da00 ")).toBe("#40da00");
  });

  it("refuses anything else", () => {
    expect(parseHex("#fc0")).toBeNull();
    expect(parseHex("gold")).toBeNull();
    expect(parseHex("#ffcc0g")).toBeNull();
  });
});

describe("hexOf and hsvOf", () => {
  it("turn the primaries and greys", () => {
    expect(hexOf({ h: 0, s: 1, v: 1 })).toBe("#ff0000");
    expect(hexOf({ h: 120, s: 1, v: 1 })).toBe("#00ff00");
    expect(hexOf({ h: 240, s: 1, v: 1 })).toBe("#0000ff");
    expect(hexOf({ h: 200, s: 0, v: 1 })).toBe("#ffffff");
    expect(hexOf({ h: 200, s: 0.5, v: 0 })).toBe("#000000");
  });

  it("give back the colour they were given", () => {
    for (const hex of [
      "#ffcc00",
      "#c3dbff",
      "#aeaeae",
      "#40da00",
      "#6295e3",
      "#7c2abb",
      "#f28302",
      "#87ceeb",
    ]) {
      expect(hexOf(hsvOf(hex))).toBe(hex);
    }
  });

  it("keep the hue they were given for a grey", () => {
    expect(hsvOf("#808080", 210).h).toBe(210);
  });
});
