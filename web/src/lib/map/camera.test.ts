import { vec3, vec4 } from "gl-matrix";
import { describe, expect, it } from "vitest";
import {
  framed,
  matrices,
  numpadView,
  orbited,
  panned,
  ray,
  turned,
  type View,
  zoomed,
} from "./camera.ts";

const down: View = { x: 100, y: 200, z: 0, distance: 1000, yaw: 0, pitch: Math.PI / 2 };

describe("ray", () => {
  it("from the middle of the screen passes over the target, straight down", () => {
    const { origin, direction } = ray(down, 800, 600, 400, 300);
    const t = (0 - (origin[2] ?? 0)) / (direction[2] ?? 1);
    expect((origin[0] ?? 0) + t * (direction[0] ?? 0)).toBeCloseTo(100, 1);
    expect((origin[1] ?? 0) + t * (direction[1] ?? 0)).toBeCloseTo(200, 1);
    expect(direction[2]).toBeCloseTo(-1, 3);
  });

  it("from the top of the screen looks further north when looking north", () => {
    const view: View = { ...down, pitch: Math.PI / 4 };
    const middle = ray(view, 800, 600, 400, 300).direction;
    const top = ray(view, 800, 600, 400, 0).direction;
    expect(top[1] ?? 0).toBeGreaterThan(middle[1] ?? 0);
  });
});

describe("matrices", () => {
  it("put the target in the middle of the screen", () => {
    const { view, projection } = matrices({ ...down, pitch: 1 }, 800, 600);
    const at = vec4.transformMat4(vec4.create(), [100, 200, 0, 1], view);
    vec4.transformMat4(at, at, projection);
    expect((at[0] ?? 0) / (at[3] ?? 1)).toBeCloseTo(0, 4);
    expect((at[1] ?? 0) / (at[3] ?? 1)).toBeCloseTo(0, 4);
    expect(vec3.length([1, 0, 0])).toBe(1);
  });
});

describe("moving the camera", () => {
  it("pans along the ground the way it looks", () => {
    const north = panned({ ...down, yaw: 0 }, 0, 50);
    expect(north.x).toBeCloseTo(100, 6);
    expect(north.y).toBeCloseTo(250, 6);
    const east = panned({ ...down, yaw: Math.PI / 2 }, 0, 50);
    expect(east.x).toBeCloseTo(150, 6);
    expect(east.y).toBeCloseTo(200, 6);
    const right = panned({ ...down, yaw: 0 }, 50, 0);
    expect(right.x).toBeCloseTo(150, 6);
  });

  it("zooms in and out within limits", () => {
    expect(zoomed(down, 1).distance).toBeLessThan(1000);
    expect(zoomed(down, -1).distance).toBeGreaterThan(1000);
    expect(zoomed(down, 1000).distance).toBeGreaterThan(0);
    expect(zoomed(down, -1000).distance).toBeLessThanOrEqual(20000);
  });

  it("turns and keeps the pitch between straight up and straight down", () => {
    expect(turned(down, 1, 0).yaw).toBeCloseTo(1, 6);
    expect(turned(down, 0, 5).pitch).toBeLessThanOrEqual(Math.PI / 2);
    expect(turned(down, 0, -5).pitch).toBeGreaterThan(-Math.PI / 2);
  });
});

describe("Blender's keys and mouse", () => {
  const view: View = { x: 0, y: 0, z: 0, distance: 1000, yaw: 0.3, pitch: 0.7 };
  const deg = (d: number) => (d * Math.PI) / 180;

  it("numpad 1, 3 and 7 look from the front, the right and the top; Ctrl from the opposite side", () => {
    expect(numpadView(view, "Numpad1", false)).toMatchObject({ yaw: 0, pitch: 0 });
    expect(numpadView(view, "Numpad1", true)).toMatchObject({ yaw: Math.PI, pitch: 0 });
    expect(numpadView(view, "Numpad3", false)).toMatchObject({ yaw: -Math.PI / 2, pitch: 0 });
    expect(numpadView(view, "Numpad3", true)).toMatchObject({ yaw: Math.PI / 2, pitch: 0 });
    expect(numpadView(view, "Numpad7", false)?.pitch).toBeCloseTo(Math.PI / 2, 6);
    expect(numpadView(view, "Numpad7", true)?.pitch ?? 0).toBeLessThan(-1.5);
  });

  it("numpad 4, 6, 8 and 2 orbit by 15 degrees, 9 turns to the other side, Ctrl with them pans", () => {
    expect(numpadView(view, "Numpad4", false)?.yaw).toBeCloseTo(0.3 - deg(15), 6);
    expect(numpadView(view, "Numpad6", false)?.yaw).toBeCloseTo(0.3 + deg(15), 6);
    expect(numpadView(view, "Numpad8", false)?.pitch).toBeCloseTo(0.7 + deg(15), 6);
    expect(numpadView(view, "Numpad2", false)?.pitch).toBeCloseTo(0.7 - deg(15), 6);
    expect(numpadView(view, "Numpad9", false)?.yaw).toBeCloseTo(0.3 + Math.PI, 6);
    const panned = numpadView(view, "Numpad6", true);
    expect(panned?.yaw).toBe(0.3);
    expect(panned?.x ?? 0).toBeGreaterThan(0);
  });

  it("numpad + and - zoom; other keys do nothing", () => {
    expect(numpadView(view, "NumpadAdd", false)?.distance ?? 0).toBeLessThan(1000);
    expect(numpadView(view, "NumpadSubtract", false)?.distance ?? 0).toBeGreaterThan(1000);
    expect(numpadView(view, "KeyA", false)).toBeNull();
  });

  it("a middle drag orbits: right turns the view one way, down tilts it toward the top", () => {
    const moved = orbited(view, 100, 50);
    expect(moved.yaw).toBeLessThan(view.yaw);
    expect(moved.pitch).toBeGreaterThan(view.pitch);
  });

  it("Home frames a rectangle: its middle, from far enough to see it all", () => {
    const all = framed(view, -1000, -500, 3000, 1500);
    expect([all.x, all.y]).toEqual([1000, 500]);
    // A 4000-wide area under a 60-degree field of view needs the camera at least 3464 away.
    expect(all.distance).toBeGreaterThan(3464);
    expect([all.yaw, all.pitch]).toEqual([view.yaw, view.pitch]);
  });
});
