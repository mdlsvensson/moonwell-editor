/**
 * Which copies of a model a view can see: each copy's place tested against the view's six planes, `reach` (the model's
 * size, times the copy's scale) beyond them still counted in, so a pass draws only what it can show (the view's call:
 * a map's thousands of HD trees drawn twice a frame, for the screen and the sun, were too slow). Copies keep their order.
 */
import type { mat4 } from "gl-matrix";
import type { Animation } from "../animation.ts";
import { instanceFloats } from "./batches.ts";

/**
 * How far a model reaches from its origin: its farthest vertex, plus the farthest any bone moves in its animation (a
 * bird circling far from where it stands), a little more.
 */
export function modelReach(
  positions: readonly ArrayLike<number>[],
  animation: Animation | null,
): number {
  let reach = 0;
  for (const list of positions) {
    for (let i = 0; i + 2 < list.length; i += 3) {
      reach = Math.max(reach, Math.hypot(list[i] ?? 0, list[i + 1] ?? 0, list[i + 2] ?? 0));
    }
  }
  let travel = 0;
  if (animation) {
    for (let at = 0; at + 11 < animation.poses.length; at += 12) {
      const p = animation.poses;
      travel = Math.max(travel, Math.hypot(p[at + 3] ?? 0, p[at + 7] ?? 0, p[at + 11] ?? 0));
    }
  }
  return reach + travel + 16;
}

/** The ground's greatest height either way: a cliff stands on it, beyond its copy's place. */
export function reliefReach(relief: Float32Array): number {
  let most = 0;
  for (const height of relief) most = Math.max(most, Math.abs(height));
  return most;
}

// The frustum's planes, a, b, c, d each, the normal a, b, c of unit length pointing inward (Gribb and Hartmann).
function planes(m: mat4): number[][] {
  const row = (r: number) => [m[r] ?? 0, m[4 + r] ?? 0, m[8 + r] ?? 0, m[12 + r] ?? 0];
  const [r0, r1, r2, r3] = [row(0), row(1), row(2), row(3)];
  const combine = (a: number[], b: number[], sign: number) => {
    const plane = a.map((value, i) => value + sign * (b[i] ?? 0));
    const length = Math.hypot(plane[0] ?? 0, plane[1] ?? 0, plane[2] ?? 0) || 1;
    return plane.map((value) => value / length);
  };
  return [
    combine(r3, r0, 1),
    combine(r3, r0, -1),
    combine(r3, r1, 1),
    combine(r3, r1, -1),
    combine(r3, r2, 1),
    combine(r3, r2, -1),
  ];
}

/**
 * The copies among the first `count` of `instances` that `viewProjection` can see, their instance data packed in order
 * into `out` (made when not given or too small), and how many.
 */
export function seen(
  instances: Float32Array,
  count: number,
  viewProjection: mat4,
  reach: number,
  out?: Float32Array,
): { instances: Float32Array; count: number } {
  const target =
    out && out.length >= count * instanceFloats ? out : new Float32Array(count * instanceFloats);
  const sides = planes(viewProjection);
  let kept = 0;
  for (let i = 0; i < count; i++) {
    const at = i * instanceFloats;
    const x = instances[at + 12] ?? 0;
    const y = instances[at + 13] ?? 0;
    const z = instances[at + 14] ?? 0;
    const scale = Math.max(
      Math.hypot(instances[at] ?? 0, instances[at + 1] ?? 0, instances[at + 2] ?? 0),
      Math.hypot(instances[at + 4] ?? 0, instances[at + 5] ?? 0, instances[at + 6] ?? 0),
      Math.hypot(instances[at + 8] ?? 0, instances[at + 9] ?? 0, instances[at + 10] ?? 0),
    );
    const r = reach * scale;
    const inside = sides.every(([a = 0, b = 0, c = 0, d = 0]) => a * x + b * y + c * z + d >= -r);
    if (!inside) continue;
    // Number by number: a view of each copy would be garbage made every frame.
    const to = kept * instanceFloats;
    for (let k = 0; k < instanceFloats; k++) target[to + k] = instances[at + k] ?? 0;
    kept++;
  }
  return { instances: target, count: kept };
}
