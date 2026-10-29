import { describe, expect, it } from "vitest";
import { queryParts, type SlashSource, slashQuery, suggestions } from "./slash.ts";

describe("slashQuery", () => {
  it("finds a / that starts a word at the cursor", () => {
    expect(slashQuery("/")).toBe("");
    expect(slashQuery("Deals /dur")).toBe("dur");
    expect(slashQuery("Lasts\n/data%")).toBe("data%");
    expect(slashQuery("Lasts /data.")).toBe("data.");
    expect(slashQuery("\uFFFC/co")).toBe("co");
  });

  it("leaves a / inside a word, or one the cursor has left", () => {
    expect(slashQuery("1/2")).toBeNull();
    expect(slashQuery("/dur ")).toBeNull();
    expect(slashQuery("no slash")).toBeNull();
  });
});

describe("queryParts", () => {
  it("reads a level and a . or % at the end", () => {
    expect(queryParts("dur2")).toEqual({ words: "dur", level: 2, form: null });
    expect(queryParts("DataA%")).toEqual({ words: "dataa", level: null, form: "%" });
    expect(queryParts("dataa1.")).toEqual({ words: "dataa", level: 1, form: "." });
    expect(queryParts("")).toEqual({ words: "", level: null, form: null });
  });
});

describe("suggestions", () => {
  const own: SlashSource = {
    id: "A000",
    name: "",
    levels: 3,
    keys: [
      { key: "Dur", name: "Duration - Normal" },
      { key: "DataA", name: "Damage" },
    ],
    values: { Dur1: "5", Dur2: "7", DataA2: "40", DataA1: "0.25" },
  };
  const other: SlashSource = {
    id: "AIat",
    name: "Attack Bonus",
    levels: 1,
    keys: [{ key: "DataA", name: "Attack Bonus" }],
    values: { DataA1: "3" },
  };

  it("lists every value at the level shown", () => {
    expect(suggestions([own, other], "", 2)).toEqual([
      { token: "<A000,Dur2>", name: "Duration - Normal", owner: "", value: "7" },
      { token: "<A000,DataA2>", name: "Damage", owner: "", value: "40" },
      { token: "<AIat,DataA1>", name: "Attack Bonus", owner: "Attack Bonus", value: "3" },
    ]);
  });

  it("lists a value that is not whole with one decimal and as a percentage", () => {
    expect(suggestions([own], "dam", 1)).toEqual([
      { token: "<A000,DataA1,.>", name: "Damage", owner: "", value: "0.2" },
      { token: "<A000,DataA1,%>", name: "Damage", owner: "", value: "25" },
    ]);
  });

  it("lists a unit's keys without a level", () => {
    const unit: SlashSource = {
      id: "h000",
      name: "",
      levels: 0,
      keys: [
        { key: "realHP", name: "Hit Points", whole: true },
        { key: "mindmg1", name: "Damage - Lowest", whole: true },
      ],
      values: { realHP: "420", mindmg1: "12" },
    };
    expect(suggestions([unit], "", 3)).toEqual([
      { token: "<h000,realHP>", name: "Hit Points", owner: "", value: "420" },
      { token: "<h000,mindmg1>", name: "Damage - Lowest", owner: "", value: "12" },
    ]);
  });

  it("narrows by key, name or object, and takes the level and form typed", () => {
    expect(suggestions([own, other], "dam%", 1).map((each) => each.token)).toEqual([
      "<A000,DataA1,%>",
    ]);
    expect(suggestions([own, other], "dam2.", 1).map((each) => each.token)).toEqual([
      "<A000,DataA2,.>",
    ]);
    expect(suggestions([own, other], "dur3", 1).map((each) => each.token)).toEqual(["<A000,Dur3>"]);
    expect(suggestions([own, other], "aiat", 1).map((each) => each.token)).toEqual([
      "<AIat,DataA1>",
    ]);
    expect(suggestions([own, other], "zzz", 1)).toEqual([]);
  });
});
