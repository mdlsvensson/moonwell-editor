/**
 * Helpers for reading the game's MDX models before war3-model parses them. The game's models are all MDX version
 * 1800, newer than war3-model knows; its parser fails on their camera (CAMS) and light (LITE) chunks, and parses the
 * rest once those two are taken out. A preview needs neither.
 */

import { t } from "./text.ts";

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
  if (bytes.length < 4 || tagAt(bytes, 0) !== "MDLX") throw new Error(t("model.notMdx"));
  const kept: Uint8Array[] = [bytes.subarray(0, 4)];
  let at = 4;
  while (at < bytes.length) {
    if (at + 8 > bytes.length) throw new Error(t("model.cutShort"));
    const end = at + 8 + view.getUint32(at + 4, true);
    if (end > bytes.length) throw new Error(t("model.cutShort"));
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

/**
 * Each geoset's skin in an MDX file, as war3-model's renderer takes it: per vertex four bone numbers, then four weights
 * out of 255, a byte each; undefined for a geoset without one (every Classic geoset). The Reforged (HD) and Definitive
 * Edition geosets have a skin chunk (SKIN) instead of vertex groups. In the game's version 1800 files each of its
 * numbers is two bytes, where war3-model reads one: it reads half the chunk, as bone and weight bytes out of step,
 * and the vertices fly off to the wrong bones. Bytes it cannot follow give no skins.
 */
export function readSkins(buffer: ArrayBuffer): (Uint8Array | undefined)[] {
  const bytes = new Uint8Array(buffer);
  const view = new DataView(buffer);
  const skins: (Uint8Array | undefined)[] = [];
  if (bytes.length < 4 || tagAt(bytes, 0) !== "MDLX") return skins;
  let version = 800;
  let geosets: [number, number] | undefined;
  for (let at = 4; at + 8 <= bytes.length; ) {
    const end = at + 8 + view.getUint32(at + 4, true);
    if (end > bytes.length) return skins;
    const tag = tagAt(bytes, at);
    if (tag === "VERS" && end - at >= 12) version = view.getUint32(at + 8, true);
    if (tag === "GEOS") geosets = [at + 8, end];
    at = end;
  }
  if (!geosets) return skins;
  for (let at = geosets[0]; at + 4 <= geosets[1]; ) {
    const end = at + view.getUint32(at, true);
    if (end <= at || end > geosets[1]) break;
    skins.push(geosetSkin(bytes, view, at + 4, end, version));
    at = end;
  }
  return skins;
}

/** The skin of the geoset whose data runs from `at` to `end`, after its size; see `readSkins`. */
function geosetSkin(
  bytes: Uint8Array,
  view: DataView,
  start: number,
  end: number,
  version: number,
): Uint8Array | undefined {
  let at = start;
  /** Steps over a chunk with this tag of `count` items `width` bytes wide; false when the bytes say otherwise. */
  const skip = (tag: string, width: number): boolean => {
    if (at + 8 > end || tagAt(bytes, at) !== tag) return false;
    at += 8 + view.getUint32(at + 4, true) * width;
    return at <= end;
  };
  const parts: [string, number][] = [
    ["VRTX", 12],
    ["NRMS", 12],
    ["PTYP", 4],
    ["PCNT", 4],
    ["PVTX", 2],
    ["GNDX", 1],
    ["MTGC", 4],
    ["MATS", 4],
  ];
  const vertices = at + 8 <= end ? view.getUint32(at + 4, true) : 0;
  for (const [tag, width] of parts) if (!skip(tag, width)) return undefined;
  // The material, selection group and flags; from version 900 the level of detail and an 80-byte name; the extent.
  at += 12 + (version >= 900 ? 84 : 0) + 28;
  if (at + 4 > end) return undefined;
  at += 4 + view.getUint32(at, true) * 28;
  while (at + 8 <= end) {
    const tag = tagAt(bytes, at);
    if (tag === "TANG") {
      if (!skip("TANG", 16)) return undefined;
      continue;
    }
    if (tag !== "SKIN") return undefined;
    const count = view.getUint32(at + 4, true);
    if (count !== vertices * 8) return undefined;
    const data = at + 8;
    const next = (width: number) => {
      const after = data + count * width;
      return after === end || (after + 4 <= end && ["UVAS", "TANG"].includes(tagAt(bytes, after)));
    };
    const width = next(2) ? 2 : next(1) ? 1 : 0;
    if (width === 0) return undefined;
    const skin = new Uint8Array(count);
    for (let i = 0; i < count; i++) {
      const value = width === 2 ? view.getUint16(data + i * 2, true) : (bytes[data + i] ?? 0);
      skin[i] = Math.min(255, value);
    }
    return skin;
  }
  return undefined;
}

/** The parts of a parsed geoset (war3-model's) that `skinGeosets` reads and sets. */
export type Skinnable = {
  Vertices: ArrayLike<number>;
  Normals: ArrayLike<number>;
  VertexGroup: ArrayLike<number>;
  Groups: readonly (readonly number[])[];
  SkinWeights?: Uint8Array;
  Tangents?: Float32Array;
};

/**
 * Gives the geosets the skins `readSkins` read from their file, in place of war3-model's, with each bone number turned
 * from the geoset's own list of bones (its matrix groups) into the model's. When any geoset has a skin, war3-model
 * draws every geoset of the model as HD, so a geoset without one (a Classic part in an HD model) gets one made from its
 * vertex groups, and tangents when it has none. Returns whether the model has skins.
 */
export function skinGeosets(
  geosets: Skinnable[],
  skins: readonly (Uint8Array | undefined)[],
): boolean {
  geosets.forEach((geoset, index) => {
    const skin = skins[index];
    if (!skin || skin.length !== (geoset.Vertices.length / 3) * 8) return;
    const turned = new Uint8Array(skin);
    for (let i = 0; i < turned.length; i += 8) {
      for (let j = 0; j < 4; j++) {
        const bone = geoset.Groups[turned[i + j] ?? 0]?.[0];
        if (bone !== undefined) turned[i + j] = Math.min(255, bone);
      }
    }
    geoset.SkinWeights = turned;
  });
  if (!geosets.some((geoset) => geoset.SkinWeights?.length)) return false;
  for (const geoset of geosets) {
    const count = geoset.Vertices.length / 3;
    if (geoset.SkinWeights?.length !== count * 8) geoset.SkinWeights = groupSkin(geoset, count);
    if (geoset.Tangents?.length !== count * 4) geoset.Tangents = tangents(geoset.Normals, count);
  }
  return true;
}

/** A skin from a Classic geoset's vertex groups: up to four of the group's bones, weighted alike. */
function groupSkin(geoset: Skinnable, count: number): Uint8Array {
  const skin = new Uint8Array(count * 8);
  for (let v = 0; v < count; v++) {
    const bones = (geoset.Groups[geoset.VertexGroup[v] ?? 0] ?? [0]).slice(0, 4);
    const share = Math.floor(255 / bones.length);
    bones.forEach((bone, j) => {
      skin[v * 8 + j] = Math.min(255, bone);
      skin[v * 8 + 4 + j] = j === 0 ? 255 - share * (bones.length - 1) : share;
    });
  }
  return skin;
}

/** A tangent across each normal, for a geoset that has none: the HD shader needs one to light it. */
function tangents(normals: ArrayLike<number>, count: number): Float32Array {
  const out = new Float32Array(count * 4);
  for (let v = 0; v < count; v++) {
    const x = normals[v * 3] ?? 0;
    // The axis least along the normal, so the shader can bend it square to the normal.
    const along = Math.abs(x) < 0.9;
    out.set(along ? [1, 0, 0, 1] : [0, 1, 0, 1], v * 4);
  }
  return out;
}

/**
 * A skinned geoset's vertices moved by its bones, as the HD shader moves them: each vertex by the weighted sum of its
 * bones' matrices (`matrix` gives a bone's, 16 numbers in columns; a bone without one counts as not moved).
 */
export function posed(
  geoset: Pick<Skinnable, "Vertices" | "SkinWeights">,
  matrix: (bone: number) => ArrayLike<number> | undefined,
): Float32Array {
  const v = geoset.Vertices;
  const skin = geoset.SkinWeights;
  const out = Float32Array.from(v);
  if (!skin) return out;
  const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  for (let i = 0; i * 3 + 2 < v.length; i++) {
    const x = v[i * 3] ?? 0;
    const y = v[i * 3 + 1] ?? 0;
    const z = v[i * 3 + 2] ?? 0;
    let total = 0;
    const sum = [0, 0, 0];
    for (let j = 0; j < 4; j++) {
      const weight = (skin[i * 8 + 4 + j] ?? 0) / 255;
      if (weight === 0) continue;
      const m = matrix(skin[i * 8 + j] ?? 0) ?? identity;
      total += weight;
      for (let axis = 0; axis < 3; axis++) {
        const moved =
          (m[axis] ?? 0) * x +
          (m[4 + axis] ?? 0) * y +
          (m[8 + axis] ?? 0) * z +
          (m[12 + axis] ?? 0);
        sum[axis] = (sum[axis] ?? 0) + weight * moved;
      }
    }
    if (total > 0)
      out.set(
        sum.map((value) => value / total),
        i * 3,
      );
  }
  return out;
}

/** The parts of a parsed model (war3-model's) that `hdTextureUse` reads. */
export type Materials = {
  Materials: readonly {
    Layers: readonly {
      ShaderTypeId?: number;
      TextureID?: unknown;
      NormalTextureID?: unknown;
      ORMTextureID?: unknown;
    }[];
  }[];
  Textures: readonly { Image?: string }[];
};

/**
 * What an HD material uses the texture at `image` for, when it is the normal map or the ORM map (occlusion, roughness,
 * metalness and the team colour's share): a material of the HD shader names them, or has them as its second and
 * third layers in older files. Undefined for any other texture.
 */
export function hdTextureUse(model: Materials, image: string): "normal" | "orm" | undefined {
  const named = (id: unknown) => typeof id === "number" && model.Textures[id]?.Image === image;
  for (const { Layers: layers } of model.Materials) {
    const first = layers[0];
    if (first?.ShaderTypeId === 1) {
      if (named(first.NormalTextureID)) return "normal";
      if (named(first.ORMTextureID)) return "orm";
    } else if (layers.length === 6) {
      if (named(layers[1]?.TextureID)) return "normal";
      if (named(layers[2]?.TextureID)) return "orm";
    }
  }
  return undefined;
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
  Geosets: readonly { Vertices: ArrayLike<number>; MaterialID: number; LevelOfDetail?: number }[];
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
 * team glow, the geosets the animation keeps hidden (from `interval`, the animation's first and last frame), and the
 * HD models' less detailed copies. The animation's own extent often counts the hidden ones, so a hero looks small in
 * it. `pose` gives a geoset's vertices where the animation puts them, for an HD model, whose bind pose stands with its
 * arms out. Undefined when no geoset is solid.
 */
export function solidExtent(
  model: Shape,
  interval?: ArrayLike<number>,
  pose?: (geoset: number) => ArrayLike<number> | undefined,
): Extent | undefined {
  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];
  let any = false;
  model.Geosets.forEach((geoset, index) => {
    const layer = model.Materials[geoset.MaterialID]?.Layers[0];
    if (!layer || !solidModes.includes(layer.FilterMode ?? 0)) return;
    const texture =
      typeof layer.TextureID === "number" ? model.Textures[layer.TextureID] : undefined;
    if (texture?.ReplaceableId === teamGlow) return;
    if ((geoset.LevelOfDetail ?? 0) !== 0) return;
    const anim = model.GeosetAnims.find((a) => a.GeosetId === index);
    if (anim && !shown(anim.Alpha, interval)) return;
    const v = pose?.(index) ?? geoset.Vertices;
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
