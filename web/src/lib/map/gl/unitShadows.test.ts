import { describe, expect, it } from "vitest";
import type { PlacedThing } from "../scene.ts";
import { readTerrain } from "../scene.ts";
import { terrainBytes } from "../testTerrain.ts";
import { shadowQuads } from "./unitShadows.ts";

const flat = readTerrain(
  terrainBytes(5, 5, () => 64),
  0,
  0,
);

function unit(change: Partial<PlacedThing>): PlacedThing {
  return {
    number: 1,
    kind: "Unit",
    typeId: "hfoo",
    name: "Footman",
    ref: "gg_unit_hfoo_0001",
    x: 200,
    y: 300,
    z: 64,
    facing: 1,
    scaleX: 1,
    scaleY: 1,
    scaleZ: 1,
    model: "units\\human\\Footman\\Footman.mdl",
    texture: null,
    player: 0,
    tint: 0xffffff,
    variation: 0,
    shadow: {
      texture: "ReplaceableTextures\\Shadows\\Shadow.blp",
      x: 50,
      y: 30,
      width: 140,
      height: 120,
    },
    ...change,
  };
}

describe("shadowQuads", () => {
  it("lays a unit's picture flat on the ground, the unit at its centre point, unturned", () => {
    const quads = shadowQuads([unit({})], flat);
    const vertices = quads.get("ReplaceableTextures\\Shadows\\Shadow.blp");
    expect(vertices).toBeDefined();
    const corners = Array.from({ length: 6 }, (_, i) =>
      Array.from(vertices?.subarray(i * 5, i * 5 + 5) ?? []),
    );
    const xs = corners.map((corner) => corner[0] ?? Number.NaN);
    const ys = corners.map((corner) => corner[1] ?? Number.NaN);
    expect(Math.min(...xs)).toBe(150);
    expect(Math.max(...xs)).toBe(290);
    expect(Math.min(...ys)).toBe(270);
    expect(Math.max(...ys)).toBe(390);
    for (const corner of corners) expect(corner[2]).toBeCloseTo(66, 3); // two above the ground
    // The picture's top row (v 0) is its north edge.
    const northWest = corners.find((corner) => corner[0] === 150 && corner[1] === 390);
    expect(northWest?.slice(3)).toEqual([0, 0]);
  });

  it("lays a flyer's shadow on the ground, not at its height, and groups by picture", () => {
    const quads = shadowQuads(
      [
        unit({ z: 300 }),
        unit({ shadow: { texture: "Flyer.blp", x: 0, y: 0, width: 10, height: 10 } }),
        unit({ shadow: null }),
        unit({ kind: "Item", shadow: undefined }),
      ],
      flat,
    );
    expect([...quads.keys()].sort()).toEqual([
      "Flyer.blp",
      "ReplaceableTextures\\Shadows\\Shadow.blp",
    ]);
    expect(quads.get("ReplaceableTextures\\Shadows\\Shadow.blp")?.[2]).toBeCloseTo(66, 3);
  });
});
