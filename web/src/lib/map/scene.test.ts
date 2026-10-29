import { describe, expect, it } from "vitest";
import { groundAt, readTerrain } from "./scene.ts";
import { terrainBytes } from "./testTerrain.ts";

describe("readTerrain", () => {
  it("reads each array at its place", () => {
    const terrain = readTerrain(
      terrainBytes(
        3,
        2,
        (i) => i,
        (i) => (i === 0 ? -89.6 : Number.NaN),
      ),
      -128,
      64,
    );

    expect([terrain.columns, terrain.rows, terrain.left, terrain.bottom]).toEqual([3, 2, -128, 64]);
    expect(Array.from(terrain.ground)).toEqual([0, 1, 2, 3, 4, 5]);
    expect(terrain.relief[4]).toBe(2);
    expect(terrain.water[0]).toBeCloseTo(-89.6, 3);
    expect(Number.isNaN(terrain.water[1])).toBe(true);
    expect(Array.from(terrain.layers.subarray(0, 4))).toEqual([3, 19, 255, 255]);
    expect(Array.from(terrain.tiles)).toEqual([1, 1]);
    expect(terrain.shadows.length).toBe(2 * 16);
    expect(terrain.shadows[0]).toBe(255);
    expect(terrain.shadows[1]).toBe(0);
  });

  it("refuses bytes of another size", () => {
    expect(() => readTerrain(new ArrayBuffer(12), 0, 0)).toThrow();
  });
});

describe("groundAt", () => {
  // The C# test's case: a tile whose right corners are 128 up.
  const terrain = readTerrain(
    terrainBytes(2, 2, (i) => (i % 2) * 128),
    0,
    0,
  );

  it("blends the four corners", () => {
    expect(groundAt(terrain, 64, 64)).toBeCloseTo(64, 3);
    expect(groundAt(terrain, 32, 100)).toBeCloseTo(32, 3);
  });

  it("reads the edge outside the grid", () => {
    expect(groundAt(terrain, 500, 500)).toBeCloseTo(128, 3);
    expect(groundAt(terrain, -500, 0)).toBeCloseTo(0, 3);
  });
});
