/**
 * Picking: what is under the mouse. Placed things are drawn a second time into a hidden picture, each in a colour that
 * is its number (0 for nothing), so the colour under the mouse names it whatever its shape; regions and cameras are
 * found flat on the map; the ground point comes from the ray from the mouse (groundRay.ts).
 */
import type { CameraMark, PlacedThing, RegionMark, ThingKind } from "./scene.ts";

/** What can be picked. */
export type PickKind =
  | "unit"
  | "item"
  | "destructable"
  | "doodad"
  | "startLocation"
  | "region"
  | "camera"
  | "point";

/** A pick: what it is, its name, the name triggers use for it, its type's id and its place. */
export type Pick = {
  kind: PickKind;
  name: string;
  ref: string | null;
  typeId: string | null;
  x: number;
  y: number;
  z: number;
};

/** What is under the mouse, each null when there is none. */
export type Found = {
  thing: PlacedThing | null;
  region: RegionMark | null;
  camera: CameraMark | null;
  point: [number, number, number] | null;
};

/** A number from 1 to 2^24 - 1 as the colour the pick pass draws it in, red, green, blue and alpha bytes. */
export function numberToColour(n: number): [number, number, number, number] {
  return [n & 255, (n >>> 8) & 255, (n >>> 16) & 255, 255];
}

/** The number a pick pass's colour names; 0 for nothing (the pass clears to no alpha). */
export function colourToNumber(r: number, g: number, b: number, a: number): number {
  return a === 255 ? r | (g << 8) | (b << 16) : 0;
}

/** The smallest region that holds the point; null for none. */
export function regionAt(regions: readonly RegionMark[], x: number, y: number): RegionMark | null {
  let best: RegionMark | null = null;
  let area = Number.POSITIVE_INFINITY;
  for (const region of regions) {
    if (x < region.left || x > region.right || y < region.bottom || y > region.top) continue;
    const size = (region.right - region.left) * (region.top - region.bottom);
    if (size < area) {
      best = region;
      area = size;
    }
  }
  return best;
}

/** The camera whose target is nearest the point, within `radius`; null for none. */
export function cameraAt(
  cameras: readonly CameraMark[],
  x: number,
  y: number,
  radius: number,
): CameraMark | null {
  let best: CameraMark | null = null;
  let nearest = radius;
  for (const camera of cameras) {
    const distance = Math.hypot(camera.x - x, camera.y - y);
    if (distance <= nearest) {
      best = camera;
      nearest = distance;
    }
  }
  return best;
}

const kinds: Record<ThingKind, PickKind> = {
  Unit: "unit",
  Item: "item",
  StartLocation: "startLocation",
  Destructable: "destructable",
  Doodad: "doodad",
};

/** A placed thing's pick kind. */
export function kindOf(thing: PlacedThing): PickKind {
  return kinds[thing.kind];
}

/**
 * What a click picks. In pick mode, the asked kind only (null when the click finds none of it); else a thing over a
 * camera, a camera over a region, a region over the ground.
 */
export function choose(found: Found, asked: PickKind | null): Pick | null {
  const { thing, region, camera, point } = found;
  const fromThing = (t: PlacedThing): Pick => ({
    kind: kindOf(t),
    name: t.name,
    ref: t.ref,
    typeId: t.typeId,
    x: t.x,
    y: t.y,
    z: t.z,
  });
  const fromRegion = (r: RegionMark): Pick => ({
    kind: "region",
    name: r.name,
    ref: r.ref,
    typeId: null,
    x: (r.left + r.right) / 2,
    y: (r.bottom + r.top) / 2,
    z: 0,
  });
  const fromCamera = (c: CameraMark): Pick => ({
    kind: "camera",
    name: c.name,
    ref: c.ref,
    typeId: null,
    x: c.x,
    y: c.y,
    z: c.z,
  });
  const fromPoint = ([x, y, z]: [number, number, number]): Pick => ({
    kind: "point",
    name: "",
    ref: null,
    typeId: null,
    x,
    y,
    z,
  });
  if (asked === null) {
    if (thing) return fromThing(thing);
    if (camera) return fromCamera(camera);
    if (region) return fromRegion(region);
    return point ? fromPoint(point) : null;
  }
  if (asked === "region") return region ? fromRegion(region) : null;
  if (asked === "camera") return camera ? fromCamera(camera) : null;
  if (asked === "point") return point ? fromPoint(point) : null;
  return thing && kindOf(thing) === asked ? fromThing(thing) : null;
}
