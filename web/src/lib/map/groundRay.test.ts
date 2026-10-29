import { describe, expect, it } from "vitest";
import { groundPoint } from "./groundRay.ts";
import { readTerrain } from "./scene.ts";
import { terrainBytes } from "./testTerrain.ts";

// A 4 by 4 corner slope rising 128 a tile toward the east: the ground's z is its x.
const slope = readTerrain(
  terrainBytes(4, 4, (i) => (i % 4) * 128),
  0,
  0,
);

describe("groundPoint", () => {
  it("finds the point on a slope within 1", () => {
    // From x 0, z 384, down and east at 45 degrees: z = 384 - x meets z = x at x 192.
    const point = groundPoint(slope, [0, 192, 384], [Math.SQRT1_2, 0, -Math.SQRT1_2]);
    expect(point).not.toBeNull();
    const [x, y, z] = point ?? [0, 0, 0];
    expect(Math.abs(x - 192)).toBeLessThan(1);
    expect(y).toBeCloseTo(192, 3);
    expect(Math.abs(z - x)).toBeLessThan(1);
  });

  it("is null for a ray that misses the map", () => {
    expect(groundPoint(slope, [0, 0, 500], [0, 0, 1])).toBeNull();
    expect(groundPoint(slope, [-1000, -1000, 100], [-1, 0, 0])).toBeNull();
  });
});
