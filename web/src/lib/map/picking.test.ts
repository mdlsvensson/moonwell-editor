import { describe, expect, it } from "vitest";
import { cameraAt, choose, colourToNumber, numberToColour, regionAt } from "./picking.ts";
import type { CameraMark, PlacedThing, RegionMark } from "./scene.ts";

const region = (
  name: string,
  left: number,
  bottom: number,
  right: number,
  top: number,
): RegionMark => ({
  name,
  ref: `gg_rct_${name}`,
  left,
  bottom,
  right,
  top,
  color: 0xff0000,
});

const camera = (name: string, x: number, y: number): CameraMark => ({
  name,
  ref: `gg_cam_${name}`,
  x,
  y,
  z: 0,
  rotation: 90,
  angleOfAttack: 304,
  distance: 1650,
  fieldOfView: 70,
});

const unit: PlacedThing = {
  number: 3,
  kind: "Unit",
  typeId: "hfoo",
  name: "Footman",
  ref: "gg_unit_hfoo_0001",
  x: 10,
  y: 10,
  z: 0,
  facing: 0,
  scaleX: 1,
  scaleY: 1,
  scaleZ: 1,
  model: "Footman.mdl",
  texture: null,
  player: 0,
  tint: 0xffffff,
  variation: 0,
};

describe("numbers as colours", () => {
  it("round-trip, and 0 is nothing", () => {
    for (const n of [1, 255, 256, 70000, 2 ** 24 - 1]) {
      const [r, g, b, a] = numberToColour(n);
      expect(colourToNumber(r, g, b, a)).toBe(n);
    }
    expect(colourToNumber(0, 0, 0, 0)).toBe(0);
    expect(colourToNumber(5, 0, 0, 0)).toBe(0); // not drawn by the pick pass
  });
});

describe("regionAt", () => {
  it("gives the smallest region that holds the point", () => {
    const outer = region("Outer", 0, 0, 1000, 1000);
    const inner = region("Inner", 100, 100, 300, 300);
    expect(regionAt([outer, inner], 200, 200)?.name).toBe("Inner");
    expect(regionAt([outer, inner], 900, 900)?.name).toBe("Outer");
    expect(regionAt([outer, inner], 2000, 0)).toBeNull();
  });
});

describe("cameraAt", () => {
  it("gives the nearest camera within the radius only", () => {
    const cameras = [camera("Near", 50, 0), camera("Far", 400, 0)];
    expect(cameraAt(cameras, 0, 0, 100)?.name).toBe("Near");
    expect(cameraAt(cameras, 0, 0, 20)).toBeNull();
  });
});

describe("choose", () => {
  const point: [number, number, number] = [200, 200, 5];
  const found = { thing: unit, region: region("Spawn", 0, 0, 500, 500), camera: null, point };

  it("outside pick mode, a thing wins over a region, a region over the ground", () => {
    expect(choose(found, null)?.kind).toBe("unit");
    expect(choose({ ...found, thing: null }, null)?.kind).toBe("region");
    expect(choose({ ...found, thing: null, region: null }, null)?.kind).toBe("point");
  });

  it("in pick mode for a region, a click over a unit inside a region gives the region", () => {
    const pick = choose(found, "region");
    expect(pick).toMatchObject({ kind: "region", name: "Spawn", ref: "gg_rct_Spawn" });
  });

  it("in pick mode, a click on something else gives nothing", () => {
    expect(choose({ ...found, thing: null }, "unit")).toBeNull();
    expect(choose(found, "item")).toBeNull();
  });

  it("gives a unit's name, type and trigger name, and a point's place", () => {
    expect(choose(found, "unit")).toMatchObject({
      kind: "unit",
      name: "Footman",
      typeId: "hfoo",
      ref: "gg_unit_hfoo_0001",
    });
    expect(choose(found, "point")).toMatchObject({
      kind: "point",
      ref: null,
      x: 200,
      y: 200,
      z: 5,
    });
  });
});
