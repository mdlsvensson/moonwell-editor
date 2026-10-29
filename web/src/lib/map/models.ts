/**
 * The models the map view draws: read with war3-model (after the editor's fixes for the game's version 1800 files,
 * mdx.ts), kept in their bind pose with each vertex's bones, their Stand animation sampled for the graphics card to play
 * (animation.ts), and cut down to what the view draws of them: the solid, alpha-tested and blended geosets of the most
 * detailed level that Stand shows, each with its diffuse texture. Glows, effect
 * sheets and particles are left out, so a model of nothing else is a marker (Review Focus 2). An HD model is drawn
 * with its diffuse texture, its ORM texture for where the team colour goes, its normal map and its emissive map
 * (plan's call; no roughness or metal).
 */
import { mat4 } from "gl-matrix";
import { ModelRenderer, parseMDX } from "war3-model";
import {
  groupSkin,
  readSkins,
  type Skinnable,
  skinGeosets,
  standSequence,
  stripChunks,
  unreadChunks,
} from "../mdx.ts";
import { type Animation, isStill, poseAt, sampleAnimation } from "./animation.ts";
import type { PlacedThing } from "./scene.ts";

/** A model as war3-model parses it. */
type Model = ReturnType<typeof parseMDX>;

/** One geoset as the view draws it; `texture` is a path, or a replaceable texture's number. */
export type MapGeoset = {
  /** Its number among the model's geosets (its column of the animation's alphas). */
  index: number;
  /** Its vertices in the bind pose. */
  positions: Float32Array;
  /** Each vertex's four bones and their weights (0 to 255), eight bytes a vertex. */
  skin: Uint8Array;
  normals: Float32Array;
  uvs: Float32Array;
  indices: Uint16Array;
  texture: string | number;
  /** war3-model's FilterMode: 0 solid, 1 alpha-tested, 2 blended. */
  filter: number;
  /** Whether the texture is drawn over the player's team colour (a Classic unit's first layer is its team colour). */
  teamUnder: boolean;
  /** The variation the geoset belongs to in a model that holds several; null for one drawn in every variation. */
  variation: number | null;
  /** Whether its material is the HD shader's, whose replaceable textures are the `_diffuse` files. */
  hd: boolean;
  /**
   * An HD geoset's ORM texture, whose alpha marks where the diffuse texture takes the team colour (war3-model's HD
   * shader); null for none.
   */
  orm: string | null;
  /** An HD geoset's normal map (two channels), null for none. */
  normalMap: string | null;
  /** An HD geoset's emissive map, the light it gives off itself; null for none. */
  emissive: string | null;
  /** How much of its emissive map's light it gives off (its layer's EmissiveGain; the first key of an animated one). */
  emissiveGain: number;
  /** An HD geoset's tangents, four numbers a vertex (the fourth the bitangent's side), as its file has them. */
  tangents: Float32Array | null;
};

/**
 * A model as the view draws it; no geosets for one that draws as a marker. `variations` is 1 for most; `animation` is
 * its Stand, null for a model without sequences (it stands in its bind pose).
 */
export type MapModel = { geosets: MapGeoset[]; variations: number; animation: Animation | null };

/**
 * A geoset's variation by its name, for a model that holds several shapes in one (*claim*: the HD Northrend tree names
 * its geosets northrendTree_rig:nt0_main_geo to nt9_...; how the game picks one is not checked): the number after the
 * letters that start the name's last part.
 */
function variationOf(name: string | undefined): number | null {
  const match = /(?:^|:)[a-z]+(\d+)_/i.exec(name ?? "");
  return match ? Number(match[1]) : null;
}

/** The most a geoset's alpha is in the Stand animation, from 0 (hidden throughout) to 1. */
export type Shown = (geoset: number) => number;

/** The filter modes drawn: none, transparent (alpha-tested) and blend. */
const drawn = [0, 1, 2];
/** The replaceable textures a model leaves to the game: the team colour and the team glow. */
const teamColour = 1;
const teamGlow = 2;

/** A geoset's bones and weights a vertex: an HD geoset's skin, a Classic one's made from its vertex groups. */
export function skinOf(geoset: Skinnable): Uint8Array {
  const count = geoset.Vertices.length / 3;
  return geoset.SkinWeights?.length === count * 8 ? geoset.SkinWeights : groupSkin(geoset, count);
}

type Texture = { Image?: string; ReplaceableId?: number };
type Layer = {
  FilterMode?: number;
  TextureID?: unknown;
  NormalTextureID?: unknown;
  ORMTextureID?: unknown;
  EmissiveTextureID?: unknown;
  EmissiveGain?: unknown;
  ShaderTypeId?: number;
};

// A layer's number that may be keyed: the number, or its first key's value; `fallback` without one.
function firstValue(value: unknown, fallback: number): number {
  if (typeof value === "number") return value;
  const key = (value as { Keys?: { Vector: ArrayLike<number> }[] } | undefined)?.Keys?.[0];
  return key?.Vector[0] ?? fallback;
}

/** What the view draws of a parsed model, leaving out the geosets its Stand never shows. */
export function mapModel(model: Model, shown: Shown = () => 1): MapModel {
  const textures = model.Textures as Texture[];
  const textureOf = (layer: Layer | undefined): Texture | undefined => {
    const id = layer?.TextureID;
    const index =
      typeof id === "number"
        ? id
        : ((id as { Keys?: { Vector: ArrayLike<number> }[] } | undefined)?.Keys?.[0]?.Vector[0] ??
          -1);
    return textures[index];
  };
  const named = model.Geosets.map((geoset) => variationOf((geoset as { Name?: string }).Name));
  const numbers = new Set(named.filter((each) => each !== null));
  const variations = numbers.size;
  // Shapes only when the numbers run 0, 1, 2 ... with none missing (a body1 and a cape2 are not shapes).
  const several =
    variations > 1 && [...numbers].every((each) => each !== null && each < variations);
  const geosets: MapGeoset[] = [];
  model.Geosets.forEach((geoset, index) => {
    if ((geoset.LevelOfDetail ?? 0) !== 0) return;
    if (shown(index) < 0.01) return;
    const layers = (model.Materials[geoset.MaterialID]?.Layers ?? []) as Layer[];
    const teamUnder = textureOf(layers[0])?.ReplaceableId === teamColour && layers.length > 1;
    const diffuse = layers.find((layer) => {
      const id = textureOf(layer)?.ReplaceableId;
      return id !== teamColour && id !== teamGlow;
    });
    const texture = textureOf(diffuse);
    const filter = (teamUnder ? layers[0]?.FilterMode : diffuse?.FilterMode) ?? 0;
    if (!diffuse || !texture || !drawn.includes(filter)) return;
    // An HD material is one layer of the HD shader with its textures named in it (version 1100), or six layers of one
    // texture each: the normal map second, the ORM third, the emissive map fourth (version 1000).
    const shader = layers[0]?.ShaderTypeId === 1;
    const hd = shader || layers.length === 6;
    const hdTexture = (field: keyof Layer, layer: number) =>
      shader
        ? textureOf({ TextureID: layers[0]?.[field] })
        : hd
          ? textureOf(layers[layer])
          : undefined;
    const orm = hdTexture("ORMTextureID", 2);
    const normalMap = hdTexture("NormalTextureID", 1);
    const emissive = hdTexture("EmissiveTextureID", 3);
    const tangents = (geoset as { Tangents?: Float32Array }).Tangents;
    geosets.push({
      index,
      positions: Float32Array.from(geoset.Vertices),
      skin: skinOf(geoset as unknown as Skinnable),
      normals: Float32Array.from(geoset.Normals),
      uvs: Float32Array.from(
        geoset.TVertices[0] ?? new Float32Array((geoset.Vertices.length / 3) * 2),
      ),
      indices: Uint16Array.from(geoset.Faces),
      texture: texture.ReplaceableId ? texture.ReplaceableId : (texture.Image ?? ""),
      filter,
      teamUnder,
      variation: several ? (named[index] ?? null) : null,
      hd,
      orm: orm?.Image || null,
      normalMap: normalMap?.Image || null,
      emissive: emissive?.Image || null,
      emissiveGain: firstValue(layers[0]?.EmissiveGain, 1),
      tangents: hd && tangents?.length === (geoset.Vertices.length / 3) * 4 ? tangents : null,
    });
  });
  return { geosets, variations: several ? variations : 1, animation: null };
}

/** The model's Stand animation, sampled by war3-model's renderer; null for a model without sequences. */
function animationOf(model: Model): Animation | null {
  try {
    const stand = standSequence(model.Sequences.map((sequence) => sequence.Name));
    const sequence = model.Sequences[stand];
    if (stand < 0 || !sequence) return null;
    const renderer = new ModelRenderer(model);
    renderer.setSequence(stand);
    const data = (
      renderer as unknown as {
        rendererData: {
          frame: number;
          nodes?: { matrix: Float32Array }[];
          geosetAlpha?: number[];
        };
      }
    ).rendererData;
    const [start = 0, end = 0] = Array.from(sequence.Interval);
    return sampleAnimation({
      start,
      end,
      bones: Math.min(data.nodes?.length ?? 0, 256),
      geosets: model.Geosets.length,
      at: (time) => {
        renderer.update(time - data.frame);
      },
      matrix: (bone) => data.nodes?.[bone]?.matrix,
      alpha: (geoset) => data.geosetAlpha?.[geoset] ?? 1,
    });
  } catch {
    return null;
  }
}

/** The most each geoset's alpha is over an animation's frames; every geoset shown without one. */
function shownIn(animation: Animation | null): Shown {
  if (!animation) return () => 1;
  return (geoset) => {
    let most = 0;
    for (let frame = 0; frame < animation.frames; frame++) {
      most = Math.max(most, animation.alphas[frame * animation.geosets + geoset] ?? 1);
    }
    return most;
  };
}

/**
 * A model whose animation never moves, baked into its one pose on the CPU so the graphics card need not move it each
 * frame (the view's call: a map's thousands of trees mostly stand still); a moving one as it is.
 */
export function settle(model: MapModel): MapModel {
  const animation = model.animation;
  if (!animation || !isStill(animation)) return model;
  const pose = poseAt(animation, 0);
  const geosets = model.geosets.map((geoset) => {
    const count = geoset.positions.length / 3;
    const positions = new Float32Array(geoset.positions.length);
    const normals = new Float32Array(geoset.normals.length);
    const tangents = geoset.tangents ? Float32Array.from(geoset.tangents) : null;
    for (let v = 0; v < count; v++) {
      // The vertex's bones' matrices, weighted; a vertex without weights stays where it is.
      const m = new Array<number>(16).fill(0);
      let total = 0;
      for (let j = 0; j < 4; j++) {
        const weight = (geoset.skin[v * 8 + 4 + j] ?? 0) / 255;
        if (weight === 0) continue;
        const bone = pose(geoset.skin[v * 8 + j] ?? 0);
        for (let k = 0; k < 16; k++) m[k] = (m[k] ?? 0) + (bone[k] ?? 0) * weight;
        total += weight;
      }
      const moved = (from: ArrayLike<number>, to: Float32Array, at: number, w: number) => {
        const [x, y, z] = [from[at] ?? 0, from[at + 1] ?? 0, from[at + 2] ?? 0];
        for (let axis = 0; axis < 3; axis++) {
          to[at + axis] =
            total > 0
              ? ((m[axis] ?? 0) * x +
                  (m[4 + axis] ?? 0) * y +
                  (m[8 + axis] ?? 0) * z +
                  (m[12 + axis] ?? 0) * w) /
                total
              : (from[at + axis] ?? 0);
        }
      };
      moved(geoset.positions, positions, v * 3, 1);
      moved(geoset.normals, normals, v * 3, 0);
      if (tangents && geoset.tangents) moved(geoset.tangents, tangents, v * 4, 0);
    }
    return { ...geoset, positions, normals, tangents };
  });
  return { ...model, geosets, animation: null };
}

/** The model at a game file's URL as the view draws it; null when it is missing or cannot be read. */
export async function loadModel(url: string): Promise<MapModel | null> {
  try {
    const response = await fetch(url);
    if (!response.ok) return null;
    const buffer = stripChunks(await response.arrayBuffer(), unreadChunks);
    const model = parseMDX(buffer);
    skinGeosets(model.Geosets as unknown as Skinnable[], readSkins(buffer));
    const animation = animationOf(model);
    return settle({ ...mapModel(model, shownIn(animation)), animation });
  } catch {
    return null;
  }
}

/** Where a placed thing's model goes: moved to its place, turned to its facing, scaled. */
export function instanceMatrix(thing: PlacedThing): mat4 {
  const matrix = mat4.fromTranslation(mat4.create(), [thing.x, thing.y, thing.z]);
  mat4.rotateZ(matrix, matrix, thing.facing);
  return mat4.scale(matrix, matrix, [thing.scaleX, thing.scaleY, thing.scaleZ]);
}

/** The placed things with a model, grouped by model, texture and variation, in order; markers left out. */
export function byModel(things: readonly PlacedThing[]): Map<string, PlacedThing[]> {
  const groups = new Map<string, PlacedThing[]>();
  for (const thing of things) {
    if (!thing.model) continue;
    const key = `${thing.model}|${thing.texture ?? ""}|${thing.variation}`;
    const group = groups.get(key);
    if (group) group.push(thing);
    else groups.set(key, [thing]);
  }
  return groups;
}

/**
 * The files to try, in order, for a geoset's texture: a file the model names as it is; for the replaceable textures
 * the game fills in (11 a cliff's, 31 to 37 a tree's) the batch's texture, and before it, for an HD geoset, its
 * `_diffuse` version (an HD tree's diffuse map is LordaeronSnowTree_diffuse, 2048 by 1024, where the Classic texture
 * is 256 by 256 and laid out for the Classic model; seen in the install 2026-10-29). None for any other number.
 *
 * An HD cliff model names its texture as a file the install does not have, Cliff0 or Cliff1 (_Diffuse.tif), in
 * ReplaceableTextures\Cliff\ or, for a ramp, Doodads\Terrain\#ignore_Cliffs\; CliffTypes.slk's overrideTexture names
 * that file for the cliff type, so the cliff type's texture takes its place (*claim*, seen as white cliffs on WarChasers
 * and Ekrezem's Maze 2026-10-29).
 */
export function texturePaths(
  texture: string | number,
  batchTexture: string | null,
  hd: boolean,
): string[] {
  const cliffFile =
    typeof texture === "string" &&
    !!batchTexture &&
    /(^|[\\/])cliff\d(_diffuse)?\.[a-z0-9]+$/i.test(texture);
  if (typeof texture === "string" && !cliffFile) return texture ? [texture] : [];
  const replaced = cliffFile || texture === 11 || (Number(texture) >= 31 && Number(texture) <= 37);
  if (!replaced || !batchTexture) return [];
  // A destructable's texture field names its texture without the extension.
  const extension = /\.[a-z0-9]+$/i.exec(batchTexture)?.[0] ?? "";
  const stem = extension ? batchTexture.slice(0, -extension.length) : batchTexture;
  const file = `${stem}${extension || ".blp"}`;
  return hd ? [`${stem}_diffuse${extension || ".blp"}`, file] : [file];
}

/**
 * The geosets a thing of `variation` draws: those of its own shape and those of every shape. None when its shape's
 * geosets all draw nothing, and then the thing is a marker (Review Focus 2).
 */
export function ownGeosets(
  model: Pick<MapModel, "geosets" | "variations">,
  variation: number,
): MapGeoset[] {
  const shape = variation % model.variations;
  return model.geosets.filter((geoset) => geoset.variation === null || geoset.variation === shape);
}
