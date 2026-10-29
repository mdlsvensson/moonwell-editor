import { mat4 } from "gl-matrix";
import { describe, expect, it } from "vitest";
import { instanceFloats } from "./batches.ts";
import { modelReach, reliefReach, seen } from "./culling.ts";

/** Instance data for copies at these places, unscaled. */
function copies(places: [number, number, number][]): Float32Array {
  const out = new Float32Array(places.length * instanceFloats);
  places.forEach(([x, y, z], index) => {
    out.set(mat4.fromTranslation(mat4.create(), [x, y, z]), index * instanceFloats);
    out[index * instanceFloats + 22] = index + 1;
  });
  return out;
}

describe("seen", () => {
  // Looking straight down from 1000 above the origin, 60 degrees across.
  const look = mat4.lookAt(mat4.create(), [0, 0, 1000], [0, 0, 0], [0, 1, 0]);
  const projection = mat4.perspective(mat4.create(), Math.PI / 3, 1, 1, 5000);
  const viewProjection = mat4.multiply(mat4.create(), projection, look);

  it("keeps the copies in view and those within reach of its edge, in order", () => {
    const instances = copies([
      [0, 0, 0],
      [5000, 0, 0],
      [400, 0, 0],
      [-700, 0, 0],
    ]);
    const kept = seen(instances, 4, viewProjection, 200);
    expect(kept.count).toBe(3);
    expect([0, 1, 2].map((i) => kept.instances[i * instanceFloats + 22])).toEqual([1, 3, 4]);
  });

  it("counts a scaled copy's reach as scaled", () => {
    const instances = copies([[800, 0, 0]]);
    expect(seen(instances, 1, viewProjection, 100).count).toBe(0);
    instances.set(
      mat4.fromRotationTranslationScale(mat4.create(), [0, 0, 0, 1], [800, 0, 0], [4, 4, 4]),
    );
    expect(seen(instances, 1, viewProjection, 100).count).toBe(1);
  });
});

describe("modelReach", () => {
  const positions = [new Float32Array([100, 0, 0, 0, 50, 0])];

  it("is the farthest vertex from the model's origin, a little more", () => {
    const reach = modelReach(positions, null);
    expect(reach).toBeGreaterThanOrEqual(100);
    expect(reach).toBeLessThan(150);
  });

  it("adds the farthest any bone moves in its animation (a bird circling far from its origin)", () => {
    const poses = new Float32Array(2 * 12);
    poses.set([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]);
    poses.set([1, 0, 0, 500, 0, 1, 0, 0, 0, 0, 1, 0], 12);
    const animation = {
      frames: 2,
      rate: 30,
      bones: 1,
      geosets: 1,
      poses,
      alphas: new Float32Array(2),
    };
    expect(modelReach(positions, animation)).toBeGreaterThanOrEqual(600);
  });
});

describe("reliefReach", () => {
  it("is the ground's greatest height either way, which a cliff stands on beyond its copy's place", () => {
    expect(reliefReach(new Float32Array([-300, 50, 2000]))).toBe(2000);
    expect(reliefReach(new Float32Array([-3000, 50]))).toBe(3000);
  });
});
