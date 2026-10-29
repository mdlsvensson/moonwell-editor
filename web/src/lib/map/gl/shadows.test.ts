import { vec3, vec4 } from "gl-matrix";
import { describe, expect, it } from "vitest";
import { shadowReach, sunView } from "./shadows.ts";

/** Where `sunView` puts a world point, as x, y and depth from 0 to 1. */
function onMap(matrix: ReturnType<typeof sunView>, point: [number, number, number]): number[] {
  const out = vec4.transformMat4(vec4.create(), [...point, 1], matrix);
  return [0, 1, 2].map((axis) => ((out[axis] ?? 0) / (out[3] ?? 1)) * 0.5 + 0.5);
}

describe("sunView", () => {
  const toward = vec3.normalize(vec3.create(), [-0.24, -0.41, 0.88]) as [number, number, number];

  it("puts the camera's target in the middle of the shadow map", () => {
    const [x, y] = onMap(sunView([100, 200, 50], toward, 1000), [100, 200, 50]);
    expect(x).toBeCloseTo(0.5, 5);
    expect(y).toBeCloseTo(0.5, 5);
  });

  it("puts a point nearer the sun on the same spot of the map, nearer", () => {
    const matrix = sunView([0, 0, 0], toward, 1000);
    const ground = onMap(matrix, [300, -200, 0]);
    const above = onMap(matrix, [300 + toward[0] * 400, -200 + toward[1] * 400, toward[2] * 400]);
    expect(above[0]).toBeCloseTo(ground[0] ?? 0, 5);
    expect(above[1]).toBeCloseTo(ground[1] ?? 0, 5);
    expect(above[2]).toBeLessThan(ground[2] ?? 0);
  });

  it("covers `reach` either side of the target", () => {
    const [x] = onMap(sunView([0, 0, 0], [0, 0, 1], 1000), [1000, 0, 0]);
    expect(Math.abs((x ?? 0) - 0.5)).toBeCloseTo(0.5, 5);
  });

  it("works for a sun straight overhead", () => {
    const [x, y] = onMap(sunView([0, 0, 0], [0, 0, 1], 500), [0, 0, 0]);
    expect(x).toBeCloseTo(0.5, 5);
    expect(y).toBeCloseTo(0.5, 5);
  });
});

describe("shadowReach", () => {
  it("grows with the camera's distance, within bounds", () => {
    expect(shadowReach(1000)).toBeGreaterThan(shadowReach(500));
    expect(shadowReach(10)).toBe(shadowReach(100));
    expect(shadowReach(1e6)).toBe(shadowReach(1e7));
  });
});
