import { describe, expect, it } from "vitest";
import { GlHost } from "./host.ts";

/** A canvas that hands out a stand-in context and can lose it; `released` counts the contexts handed back. */
class FakeCanvas extends EventTarget {
  released = 0;

  getContext(): object {
    return { getExtension: () => ({ loseContext: () => this.released++ }) };
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
    expect(canvas.released).toBe(0); // the canvas stays for the next mode's host
  });

  it("hands the context back when the view goes, so contexts do not pile up", () => {
    // Chromium keeps 16 live contexts and then drops the oldest, which may be the model preview's.
    const canvas = new FakeCanvas();
    const host = new GlHost(canvas as unknown as HTMLCanvasElement, () => () => {});
    host.stop(true);
    expect(canvas.released).toBe(1);
  });
});
