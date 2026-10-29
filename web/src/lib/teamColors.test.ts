import { describe, expect, it } from "vitest";
import { ownerRgb, playerIndex, teamColors, teamRgb } from "./teamColors.ts";

describe("teamColors", () => {
  it("has the game's 24 player colours in player order", () => {
    expect(teamColors).toHaveLength(24);
    expect(teamColors[0]).toEqual({ name: "Red", hex: "#ff0303" });
    expect(teamColors[1]).toEqual({ name: "Blue", hex: "#0042ff" });
    expect(teamColors[6]).toEqual({ name: "Green", hex: "#20c000" });
    expect(teamColors[23]).toEqual({ name: "Peanut", hex: "#a46f33" });
  });

  it("gives every colour a name of its own and a 6-digit hex code", () => {
    expect(new Set(teamColors.map((c) => c.name)).size).toBe(24);
    for (const { hex } of teamColors) expect(hex).toMatch(/^#[0-9a-f]{6}$/);
  });
});

describe("teamRgb", () => {
  it("gives the colour as 0-to-1 numbers", () => {
    expect(teamRgb(0)).toEqual([1, 3 / 255, 3 / 255]);
    expect(teamRgb(1)).toEqual([0, 0x42 / 255, 1]);
    expect(teamRgb(20)).toEqual([0x28 / 255, 0x28 / 255, 0x28 / 255]);
  });

  it("gives player 1's red for an index off the list", () => {
    for (const index of [-1, 24, 1.5, Number.NaN]) {
      expect(playerIndex(index)).toBe(0);
      expect(teamRgb(index)).toEqual(teamRgb(0));
    }
  });
});

describe("ownerRgb", () => {
  it("gives a player's own colour", () => {
    expect(ownerRgb(1)).toEqual(teamRgb(1));
    expect(ownerRgb(23)).toEqual(teamRgb(23));
  });

  it("gives the neutral players (25 to 28) the grey of the game's TeamColor24 to 27 files", () => {
    for (const player of [24, 25, 26, 27])
      expect(ownerRgb(player)).toEqual([0x2e / 255, 0x2e / 255, 0x2e / 255]);
  });
});
