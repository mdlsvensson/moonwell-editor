/**
 * Helpers for reading the game's MDX models before war3-model parses them. The game's models are all MDX version
 * 1800, newer than war3-model knows; its parser fails on their camera (CAMS) and light (LITE) chunks, and parses the
 * rest once those two are taken out. A preview needs neither.
 */

/** The chunks a preview drops before parsing: cameras and lights. */
export const unreadChunks = ["CAMS", "LITE"];

/**
 * A copy of an MDX file without the top-level chunks whose tags are given. An MDX file is the tag `MDLX`, then chunks:
 * a 4-letter tag, the size of its data as a little-endian 32-bit number, then the data. Throws when the bytes are not
 * an MDX file or a chunk runs past the end.
 */
export function stripChunks(buffer: ArrayBuffer, tags: readonly string[]): ArrayBuffer {
  const bytes = new Uint8Array(buffer);
  const view = new DataView(buffer);
  if (bytes.length < 4 || tagAt(bytes, 0) !== "MDLX") throw new Error("Not an MDX model");
  const kept: Uint8Array[] = [bytes.subarray(0, 4)];
  let at = 4;
  while (at < bytes.length) {
    if (at + 8 > bytes.length) throw new Error("The model file is cut short");
    const end = at + 8 + view.getUint32(at + 4, true);
    if (end > bytes.length) throw new Error("The model file is cut short");
    if (!tags.includes(tagAt(bytes, at))) kept.push(bytes.subarray(at, end));
    at = end;
  }
  const out = new Uint8Array(kept.reduce((sum, part) => sum + part.length, 0));
  let to = 0;
  for (const part of kept) {
    out.set(part, to);
    to += part.length;
  }
  return out.buffer;
}

function tagAt(bytes: Uint8Array, at: number): string {
  return String.fromCharCode(
    bytes[at] ?? 0,
    bytes[at + 1] ?? 0,
    bytes[at + 2] ?? 0,
    bytes[at + 3] ?? 0,
  );
}

/** The animation a preview plays: the first whose name starts with "Stand", else the first; -1 when there are none. */
export function standSequence(names: readonly string[]): number {
  if (names.length === 0) return -1;
  const stand = names.findIndex((name) => name.trim().toLowerCase().startsWith("stand"));
  return stand >= 0 ? stand : 0;
}

/** A box, as the model gives its extents: the smallest and largest corner. */
export type Extent = { min: ArrayLike<number>; max: ArrayLike<number> };

/** The parts of a parsed model (war3-model's) that `solidExtent` reads. */
export type Shape = {
  Geosets: readonly { Vertices: ArrayLike<number>; MaterialID: number }[];
  Materials: readonly { Layers: readonly { FilterMode?: number; TextureID?: unknown }[] }[];
  Textures: readonly { ReplaceableId?: number }[];
  GeosetAnims: readonly { GeosetId: number; Alpha: unknown }[];
};

/** The layer blend modes that draw a solid surface: none, transparent (alpha-tested) and blend. */
const solidModes = [0, 1, 2];
/** The texture number the game fills with the hero's team glow, the disc under a hero. */
const teamGlow = 2;

/**
 * The box round the model's solid geosets as they stand in the bind pose, leaving out glows, effect sheets and the
 * team glow, and the geosets the animation keeps hidden (from `interval`, the animation's first and last frame). The
 * animation's own extent often counts those, so a hero looks small in it. Undefined when no geoset is solid.
 */
export function solidExtent(model: Shape, interval?: ArrayLike<number>): Extent | undefined {
  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];
  let any = false;
  model.Geosets.forEach((geoset, index) => {
    const layer = model.Materials[geoset.MaterialID]?.Layers[0];
    if (!layer || !solidModes.includes(layer.FilterMode ?? 0)) return;
    const texture =
      typeof layer.TextureID === "number" ? model.Textures[layer.TextureID] : undefined;
    if (texture?.ReplaceableId === teamGlow) return;
    const anim = model.GeosetAnims.find((a) => a.GeosetId === index);
    if (anim && !shown(anim.Alpha, interval)) return;
    const v = geoset.Vertices;
    for (let i = 0; i + 2 < v.length; i += 3) {
      for (let axis = 0; axis < 3; axis++) {
        const value = v[i + axis] ?? 0;
        if (value < (min[axis] ?? 0)) min[axis] = value;
        if (value > (max[axis] ?? 0)) max[axis] = value;
      }
      any = true;
    }
  });
  return any ? { min, max } : undefined;
}

/** Whether a geoset's alpha lets it show at some point in the interval: a key in it, or the one before it, above 0. */
function shown(alpha: unknown, interval?: ArrayLike<number>): boolean {
  if (typeof alpha === "number") return alpha > 0;
  const keys = (alpha as { Keys?: { Frame: number; Vector: ArrayLike<number> }[] } | null)?.Keys;
  if (!keys || keys.length === 0) return true;
  const start = interval?.[0] ?? -Infinity;
  const end = interval?.[1] ?? Infinity;
  const inside = keys.filter((key) => key.Frame >= start && key.Frame <= end);
  const before = keys.filter((key) => key.Frame < start).at(-1);
  const seen = before ? [before, ...inside] : inside;
  if (seen.length === 0) return true;
  return seen.some((key) => (key.Vector[0] ?? 0) > 0);
}

/** Where to point the camera and how far the model reaches from there. */
export type Framing = { centre: [number, number, number]; radius: number };

/**
 * The framing for the first extent that has a size (the animation's, then the model's), or a unit-sized one around
 * the origin with a radius of 100 when none has.
 */
export function frame(extents: readonly (Extent | undefined)[]): Framing {
  for (const extent of extents) {
    if (!extent) continue;
    const size = [0, 1, 2].map((i) => (extent.max[i] ?? 0) - (extent.min[i] ?? 0));
    const radius = Math.hypot(...size) / 2;
    if (!(radius > 0) || !Number.isFinite(radius)) continue;
    const mid = (i: number) => ((extent.min[i] ?? 0) + (extent.max[i] ?? 0)) / 2;
    return { centre: [mid(0), mid(1), mid(2)], radius };
  }
  return { centre: [0, 0, 0], radius: 100 };
}
