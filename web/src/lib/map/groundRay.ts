/** Where a ray from the camera meets the ground: the point a click on the map picks. */
import { groundAt, type Terrain } from "./scene.ts";

/** How far a ray is followed, and the step it is followed in before the meeting point is narrowed down. */
const reach = 60000;
const step = 16;

/**
 * The first point where the ray from `origin` along `direction` (of length 1) goes below the ground inside the map,
 * found to within a hundredth; null when it meets none.
 */
export function groundPoint(
  terrain: Terrain,
  origin: ArrayLike<number>,
  direction: ArrayLike<number>,
): [number, number, number] | null {
  const right = terrain.left + (terrain.columns - 1) * 128;
  const top = terrain.bottom + (terrain.rows - 1) * 128;
  const at = (t: number): [number, number, number] => [
    (origin[0] ?? 0) + t * (direction[0] ?? 0),
    (origin[1] ?? 0) + t * (direction[1] ?? 0),
    (origin[2] ?? 0) + t * (direction[2] ?? 0),
  ];
  const inside = ([x, y]: [number, number, number]) =>
    x >= terrain.left && x <= right && y >= terrain.bottom && y <= top;
  const below = (point: [number, number, number]) =>
    point[2] <= groundAt(terrain, point[0], point[1]);

  let before = 0;
  for (let t = 0; t <= reach; t += step) {
    const point = at(t);
    if (inside(point) && below(point)) {
      // Narrow it down between the last step above the ground and this one.
      let low = before;
      let high = t;
      for (let i = 0; i < 12; i++) {
        const middle = (low + high) / 2;
        if (below(at(middle))) high = middle;
        else low = middle;
      }
      return at(high);
    }
    before = t;
  }
  return null;
}
