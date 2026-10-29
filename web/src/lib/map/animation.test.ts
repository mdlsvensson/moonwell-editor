import { mat4 } from "gl-matrix";
import { describe, expect, it } from "vitest";
import { isStill, type Poser, poseAt, sampleAnimation, sampleRate } from "./animation.ts";

/** A model of two bones and two geosets whose second bone moves 1 along x a millisecond; geoset 1 hidden from 50. */
function poser(start: number, end: number): Poser & { frames: number[] } {
  const frames: number[] = [];
  let frame = start;
  return {
    start,
    end,
    bones: 2,
    geosets: 2,
    frames,
    at(time) {
      frame = time;
      frames.push(time);
    },
    matrix: (bone) =>
      bone === 1 ? mat4.fromTranslation(mat4.create(), [frame - start, 0, 0]) : undefined,
    alpha: (geoset) => (geoset === 1 && frame - start >= 50 ? 0 : 1),
  };
}

describe("sampleAnimation", () => {
  it("samples the sequence at the view's rate, from its start", () => {
    const model = poser(1000, 1100);
    const animation = sampleAnimation(model);
    expect(animation.frames).toBe(Math.ceil((100 * sampleRate) / 1000));
    expect(model.frames[0]).toBe(1000);
    expect(model.frames[1]).toBeCloseTo(1000 + 1000 / sampleRate, 3);
  });

  it("keeps each bone's matrix as three rows a frame, a bone without one unmoved", () => {
    const animation = sampleAnimation(poser(0, 100));
    const row = (frame: number, bone: number, r: number) =>
      Array.from(
        animation.poses.subarray(
          ((frame * 2 + bone) * 3 + r) * 4,
          ((frame * 2 + bone) * 3 + r) * 4 + 4,
        ),
      );
    expect(row(0, 0, 0)).toEqual([1, 0, 0, 0]);
    expect(row(0, 0, 2)).toEqual([0, 0, 1, 0]);
    expect(row(1, 1, 0)[3]).toBeCloseTo(1000 / sampleRate, 3); // x moved by the time
    expect(row(1, 1, 1)).toEqual([0, 1, 0, 0]);
  });

  it("keeps each geoset's alpha a frame", () => {
    const animation = sampleAnimation(poser(0, 100));
    expect(animation.alphas[0 * 2 + 1]).toBe(1);
    expect(animation.alphas[(animation.frames - 1) * 2 + 1]).toBe(0);
    expect(animation.alphas[(animation.frames - 1) * 2]).toBe(1);
  });

  it("samples a sequence of no length once, and a long one at most 120 times, spread over all of it", () => {
    expect(sampleAnimation(poser(5, 5)).frames).toBe(1);
    const model = poser(0, 20000);
    const long = sampleAnimation(model);
    expect(long.frames).toBe(120);
    expect(long.rate).toBeCloseTo(6, 6); // 120 frames over 20 seconds
    expect(model.frames.at(-1)).toBeCloseTo(20000 - 1000 / 6, 3);
    expect(sampleAnimation(poser(0, 100)).rate).toBe(sampleRate);
  });
});

describe("isStill", () => {
  it("tells an animation whose frames are all the first's", () => {
    const still: Poser = {
      start: 0,
      end: 100,
      bones: 1,
      geosets: 1,
      at() {},
      matrix: () => mat4.fromTranslation(mat4.create(), [5, 0, 0]),
      alpha: () => 1,
    };
    expect(isStill(sampleAnimation(still))).toBe(true);
    expect(isStill(sampleAnimation(poser(0, 100)))).toBe(false);
  });
});

describe("poseAt", () => {
  it("gives a frame's bone matrix back in columns", () => {
    const animation = sampleAnimation(poser(0, 100));
    expect(Array.from(poseAt(animation, 0)(0))).toEqual(Array.from(mat4.create()));
    expect(poseAt(animation, 1)(1)[12]).toBeCloseTo(1000 / sampleRate, 3);
  });
});
