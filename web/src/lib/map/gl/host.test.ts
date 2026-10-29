import { describe, expect, it } from "vitest";
import { GlHost } from "./host.ts";

/** A canvas that hands out a stand-in context and can lose it. */
class FakeCanvas extends EventTarget {
  getContext(): object {
    return {};
  }
}

describe("GlHost", () => {
  it("makes the layers again after a lost context comes back", () => {
    const canvas = new FakeCanvas();
    const made: number[] = [];
    const dropped: number[] = [];
    let count = 0;
    new GlHost(canvas as unknown as HTMLCanvasElement, () => {
      const n = ++count;
      made.push(n);
      return () => dropped.push(n);
    });

    const lost = new Event("webglcontextlost", { cancelable: true });
    canvas.dispatchEvent(lost);
    canvas.dispatchEvent(new Event("webglcontextrestored"));

    expect(lost.defaultPrevented).toBe(true); // so the browser gives it back
    expect(made).toEqual([1, 2]);
    expect(dropped).toEqual([1]);
  });

  it("drops the layers when it stops, and makes none after", () => {
    const canvas = new FakeCanvas();
    let made = 0;
    let dropped = 0;
    const host = new GlHost(canvas as unknown as HTMLCanvasElement, () => {
      made++;
      return () => dropped++;
    });
    host.stop();
    canvas.dispatchEvent(new Event("webglcontextrestored"));
    expect([made, dropped]).toEqual([1, 1]);
  });
});
