import { describe, expect, it } from "vitest";
import type { SceneLight } from "../scene.ts";
import { lighting, scales } from "./lighting.ts";

const sun: SceneLight = { toward: [0, 0, 2], colour: [1.3, 1.3, 1.3], ambient: [0.2, 0.1, 0.1] };

describe("lighting", () => {
  it("lights Classic as the game's fixed-function lighting: the light's own colours, no sky", () => {
    const classic = lighting(sun, [], "terrain");
    expect(classic.pbr).toBe(false);
    expect(classic.toward).toEqual([0, 0, 1]);
    expect(classic.colour).toEqual([1.3, 1.3, 1.3]);
    expect(classic.ambient).toEqual([0.2, 0.1, 0.1]);
    expect(Array.from(classic.sky)).toEqual(new Array(18).fill(0));
  });

  it("lights HD and Definitive Edition in linear colour, the sky's sRGB made linear, both scaled", () => {
    const sky = [1, 1, 1, 0.5, 0.5, 0.5, 0, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0];
    const hd = lighting(sun, sky, "terrain");
    const { sun: sunScale, sky: skyScale } = scales.terrain;
    expect(hd.pbr).toBe(true);
    expect(hd.colour[0]).toBeCloseTo(1.3 * sunScale, 6);
    expect(hd.sky[0]).toBeCloseTo(skyScale, 6);
    expect(hd.sky[3]).toBeCloseTo(0.2176 * skyScale, 3); // 0.5 to the power 2.2, as the shaders take sRGB
    expect(hd.sky[6]).toBe(0);
    expect(hd.ambient).toEqual([0.2, 0.1, 0.1]);
  });

  it("lights models more than the terrain in HD and Definitive Edition, as World Editor does", () => {
    const sky = new Array(18).fill(0.5);
    expect(lighting(sun, sky, "units").colour[0]).toBeGreaterThan(
      lighting(sun, sky, "terrain").colour[0],
    );
    expect(lighting(sun, sky, "units").sky[0]).toBeGreaterThan(
      lighting(sun, sky, "terrain").sky[0] ?? 0,
    );
    expect(lighting(sun, [], "units").colour).toEqual(lighting(sun, [], "terrain").colour);
  });

  it("takes a sky of the wrong length as none", () => {
    expect(lighting(sun, [1, 2, 3], "units").pbr).toBe(false);
  });
});
