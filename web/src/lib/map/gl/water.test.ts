import { describe, expect, it } from "vitest";
import type { WaterLook } from "../scene.ts";
import { waterFramePaths } from "./water.ts";

const look: WaterLook = {
  texture: "ReplaceableTextures\\Water\\Water",
  frames: 45,
  rate: 15,
  cells: 2,
  shallowMin: 0,
  shallowMax: 0,
  deepMin: 0,
  deepMax: 0,
};

describe("waterFramePaths", () => {
  it("gives Classic water's frames, the first first", () => {
    const paths = waterFramePaths(look, true, false);
    expect(paths).toHaveLength(45);
    expect(paths[0]).toBe("ReplaceableTextures\\Water\\Water00.blp");
    expect(paths[44]).toBe("ReplaceableTextures\\Water\\Water44.blp");
  });

  it("gives none for a map without water, or for HD and Definitive Edition, whose water draws no frames", () => {
    expect(waterFramePaths(look, false, false)).toEqual([]);
    expect(waterFramePaths(look, true, true)).toEqual([]);
  });
});
