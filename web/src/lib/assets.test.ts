import { describe, expect, it } from "vitest";
import { matching } from "./assets.ts";

const paths = [
  String.raw`ReplaceableTextures\CommandButtons\BTNPeasant.blp`,
  String.raw`ReplaceableTextures\CommandButtons\BTNFootman.blp`,
  String.raw`units\human\Peasant\Peasant.mdl`,
];

describe("the picker's search", () => {
  it("keeps the paths that hold every word, in any case", () => {
    expect(matching(paths, "peasant", 10)).toEqual([paths[0], paths[2]]);
    expect(matching(paths, "btn PEAS", 10)).toEqual([paths[0]]);
  });

  it("takes either slash, and keeps everything for an empty query, up to the limit", () => {
    expect(matching(paths, "human/peasant", 10)).toEqual([paths[2]]);
    expect(matching(paths, "", 2)).toEqual(paths.slice(0, 2));
  });
});
