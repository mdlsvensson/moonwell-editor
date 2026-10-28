import { describe, expect, it } from "vitest";
import {
  cssColor,
  docOf,
  inlineOf,
  markupColor,
  markupOf,
  newlineOf,
  segmentsOf,
  segmentsOfDoc,
  tokenOf,
  tokenParts,
} from "./richText.ts";

describe("segmentsOf", () => {
  it("reads colours with their alpha, line breaks and placeholders", () => {
    expect(
      segmentsOf("Train |cffffcc00F|rootman|nCan <AHhb,Dur1> |c80ff0000<AHhb,DataA1,%>|r%"),
    ).toEqual([
      { type: "text", text: "Train ", color: null },
      { type: "text", text: "F", color: "ffffcc00" },
      { type: "text", text: "ootman", color: null },
      { type: "break" },
      { type: "text", text: "Can ", color: null },
      { type: "token", token: "<AHhb,Dur1>", color: null },
      { type: "text", text: " ", color: null },
      { type: "token", token: "<AHhb,DataA1,%>", color: "80ff0000" },
      { type: "text", text: "%", color: null },
    ]);
  });

  it("reads a newline as a break, a doubled bar as a bar, and other text with < as text", () => {
    expect(segmentsOf("a\r\nb||c <not one>")).toEqual([
      { type: "text", text: "a", color: null },
      { type: "break" },
      { type: "text", text: "b|c <not one>", color: null },
    ]);
  });
});

describe("markupOf", () => {
  it("writes back the markup it read", () => {
    for (const markup of [
      "Train |cffffcc00F|rootman",
      "Line one|nLine two",
      "|cffffcc00Level 1|r - <AHhb,Dur1> seconds.",
      "Ends coloured |cff00ff00green|r",
      "",
    ]) {
      expect(markupOf(segmentsOf(markup), newlineOf(markup))).toBe(markup);
    }
  });

  it("switches from one colour to another without ending the first", () => {
    expect(
      markupOf([
        { type: "text", text: "a", color: "ffff0000" },
        { type: "text", text: "b", color: "ff00ff00" },
      ]),
    ).toBe("|cffff0000a|cff00ff00b|r");
  });

  it("writes a bar that would read as a code twice, and keeps the newline the text used", () => {
    expect(markupOf([{ type: "text", text: "a|c b|", color: null }])).toBe("a||c b||");
    expect(markupOf([{ type: "text", text: "a | b", color: null }])).toBe("a | b");
    expect(
      markupOf(
        [
          { type: "text", text: "a", color: null },
          { type: "break" },
          { type: "text", text: "b", color: null },
        ],
        "\n",
      ),
    ).toBe("a\nb");
  });
});

describe("newlineOf", () => {
  it("keeps the text's own line breaks, else the game's |n", () => {
    expect(newlineOf("a|nb")).toBe("|n");
    expect(newlineOf("a\nb")).toBe("\n");
    expect(newlineOf("one line")).toBe("|n");
  });
});

describe("colours", () => {
  it("turn between the markup's and CSS's", () => {
    expect(cssColor("ffffcc00")).toBe("#ffcc00");
    expect(markupColor("#FFCC00")).toBe("ffffcc00");
    expect(markupColor("rgb(255, 204, 0)")).toBe("ffffcc00");
    expect(markupColor("red")).toBeNull();
  });
});

describe("tokenParts", () => {
  it("splits a placeholder into its id, key, level and percent", () => {
    expect(tokenParts("<AHhb,Dur2>")).toEqual({ id: "AHhb", key: "Dur", level: 2, percent: false });
    expect(tokenParts("<A000,DataA1,%>")).toEqual({
      id: "A000",
      key: "DataA",
      level: 1,
      percent: true,
    });
    expect(tokenParts("<AHhb,Area>")).toEqual({
      id: "AHhb",
      key: "Area",
      level: 0,
      percent: false,
    });
    expect(tokenParts("not one")).toBeNull();
  });

  it("puts a placeholder back together", () => {
    expect(tokenOf({ id: "AHhb", key: "DataB", level: 3, percent: true })).toBe("<AHhb,DataB3,%>");
    expect(tokenOf({ id: "AHhb", key: "Dur", level: 0, percent: false })).toBe("<AHhb,Dur>");
  });
});

describe("docOf and segmentsOfDoc", () => {
  it("make the editor's document of the pieces, and the pieces of it again", () => {
    const markup = "Train |cffffcc00F|rootman|nCan <AHhb,Dur1> |c80ff0000<AHhb,DataA1,%>|r%";
    const doc = docOf(segmentsOf(markup));
    expect(doc.content?.[0]?.content?.slice(0, 4)).toEqual([
      { type: "text", text: "Train ", marks: undefined },
      { type: "text", text: "F", marks: [{ type: "color", attrs: { color: "ffffcc00" } }] },
      { type: "text", text: "ootman", marks: undefined },
      { type: "hardBreak" },
    ]);
    expect(markupOf(segmentsOfDoc([doc]))).toBe(markup);
  });

  it("give an empty text an empty paragraph", () => {
    expect(docOf([])).toEqual({ type: "doc", content: [{ type: "paragraph" }] });
    expect(segmentsOfDoc([docOf([])])).toEqual([]);
  });

  it("join text of one colour split across nodes, and break between paragraphs", () => {
    expect(
      segmentsOfDoc([
        {
          type: "paragraph",
          content: [
            { type: "text", text: "a" },
            { type: "text", text: "b" },
          ],
        },
        { type: "paragraph", content: inlineOf([{ type: "text", text: "c", color: "ff00ff00" }]) },
      ]),
    ).toEqual([
      { type: "text", text: "ab", color: null },
      { type: "break" },
      { type: "text", text: "c", color: "ff00ff00" },
    ]);
  });
});
