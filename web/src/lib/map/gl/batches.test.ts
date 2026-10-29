import { vec3 } from "gl-matrix";
import { describe, expect, it } from "vitest";
import type { MapScene, PlacedThing } from "../scene.ts";
import { cliffBatches, instanceFloats, markerColour, thingBatches } from "./batches.ts";

const scene = {
  left: -1024,
  bottom: -512,
  cliffTextures: ["TerrainArt\\Icecrown\\Ice_Cliff0.blp", "TerrainArt\\Icecrown\\Ice_Cliff1.blp"],
  cliffs: [
    {
      column: 2,
      row: 3,
      model: "Doodads\\Terrain\\Cliffs\\CliffsBAAA0.mdx",
      cliffType: 1,
      baseZ: 128,
    },
    {
      column: 4,
      row: 3,
      model: "Doodads\\Terrain\\Cliffs\\CliffsBAAA0.mdx",
      cliffType: 1,
      baseZ: 0,
    },
  ],
} as unknown as MapScene;

function at(floats: Float32Array, instance: number, point: [number, number, number]): vec3 {
  const m = floats.subarray(instance * instanceFloats, instance * instanceFloats + 16);
  return vec3.transformMat4(vec3.create(), point, m);
}

describe("cliffBatches", () => {
  it("puts each cliff model at its tile's corner and level, turned a quarter clockwise, in its type's texture", () => {
    const [batch] = cliffBatches(scene);
    expect(batch).toMatchObject({
      model: scene.cliffs[0]?.model,
      texture: scene.cliffTextures[1],
      cliff: true,
    });
    expect(batch?.count).toBe(2);
    const origin = at(batch?.instances ?? new Float32Array(), 0, [0, 0, 0]);
    expect(Array.from(origin)).toEqual([-1024 + 256, -512 + 384, 128]);
    // The model's x runs south (*claim*, HiveWE turns cliff models so).
    const east = at(batch?.instances ?? new Float32Array(), 0, [1, 0, 0]);
    expect(east[0]).toBeCloseTo(-768, 5);
    expect(east[1]).toBeCloseTo(-129, 5);
  });
});

describe("thingBatches", () => {
  const thing = (change: Partial<PlacedThing>): PlacedThing => ({
    number: 7,
    kind: "Unit",
    typeId: "hfoo",
    name: "Footman",
    ref: null,
    x: 10,
    y: 20,
    z: 30,
    facing: 0,
    scaleX: 1,
    scaleY: 1,
    scaleZ: 1,
    model: "units\\human\\Footman\\Footman.mdl",
    texture: null,
    player: 1,
    tint: 0xff8000,
    variation: 4,
    ...change,
  });

  it("gives each copy its tint, team colour and number", () => {
    const [batch] = thingBatches([thing({})]);
    expect(batch?.variation).toBe(4);
    const floats = batch?.instances ?? new Float32Array();
    expect(Array.from(floats.subarray(16, 19))).toEqual(
      [1, 128 / 255, 0].map((v) => Math.fround(v)),
    );
    expect(floats[19]).toBeCloseTo(0, 3); // player 2 is blue: no red
    expect(floats[22]).toBe(7);
  });

  it("gives markers their own colour by kind", () => {
    expect(markerColour("StartLocation")).not.toEqual(markerColour("Unit"));
  });
});
