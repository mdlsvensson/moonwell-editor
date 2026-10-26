import { describe, expect, it } from "vitest";
import { formatTsv, parseTsv } from "./tsv.ts";

describe("parseTsv", () => {
  it("reads rows and cells, with \\n or \\r\\n", () => {
    expect(parseTsv("a\tb\nc\td")).toEqual([
      ["a", "b"],
      ["c", "d"],
    ]);
    expect(parseTsv("a\tb\r\nc\td")).toEqual([
      ["a", "b"],
      ["c", "d"],
    ]);
  });

  it("drops the one trailing newline a spreadsheet adds", () => {
    expect(parseTsv("a\tb\r\n")).toEqual([["a", "b"]]);
    expect(parseTsv("a\n")).toEqual([["a"]]);
  });

  it("keeps an empty cell, also at the end of a row and as the last row", () => {
    expect(parseTsv("a\t\tc")).toEqual([["a", "", "c"]]);
    expect(parseTsv("a\t\n")).toEqual([["a", ""]]);
    expect(parseTsv("a\n\n")).toEqual([["a"], [""]]);
  });

  it("reads quoted cells with a tab, a newline or a doubled quote", () => {
    expect(parseTsv('"a\tb"\t"c\nd"\n')).toEqual([["a\tb", "c\nd"]]);
    expect(parseTsv('"say ""hi"""\tx')).toEqual([['say "hi"', "x"]]);
    expect(parseTsv('"a\r\nb"\r\nc')).toEqual([["a\r\nb"], ["c"]]);
  });

  it("answers no rows for empty text", () => {
    expect(parseTsv("")).toEqual([]);
  });
});

describe("formatTsv", () => {
  it("writes tabs between cells and a newline after each row", () => {
    expect(
      formatTsv([
        ["a", "b"],
        ["c", "d"],
      ]),
    ).toBe("a\tb\nc\td\n");
  });

  it("quotes a cell with a tab, a newline or a quote", () => {
    expect(formatTsv([["a\tb", 'say "hi"', "x\ny"]])).toBe('"a\tb"\t"say ""hi"""\t"x\ny"\n');
  });

  it("round trips a block with an empty cell and cells in quotes", () => {
    const block = [
      ["1", "", "a\tb"],
      ["", "multi\nline", 'q"uote'],
      ["x", "y", ""],
    ];
    expect(parseTsv(formatTsv(block))).toEqual(block);
    expect(parseTsv(formatTsv([[""]]))).toEqual([[""]]);
  });
});
