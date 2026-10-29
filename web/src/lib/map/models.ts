/**
 * The models the map view draws: read with war3-model (after the editor's fixes for the game's version 1800 files,
 * mdx.ts), posed once in the first frame of their Stand animation, and cut down to what the view draws of them: the
 * solid, alpha-tested and blended geosets of the most detailed level, each with its diffuse texture. Glows, effect
 * sheets and particles are left out, so a model of nothing else is a marker (Review Focus 2). An HD model is drawn
 * with its diffuse texture only (plan's call).
 */
import { mat4 } from "gl-matrix";
import { ModelRenderer, parseMDX } from "war3-model";
import {
  groupSkin,
  posed,
  readSkins,
  type Skinnable,
  skinGeosets,
  standSequence,
  stripChunks,
  unreadChunks,
} from "../mdx.ts";
import type { PlacedThing } from "./scene.ts";

/** A model as war3-model parses it. */
type Model = ReturnType<typeof parseMDX>;

/** One geoset as the view draws it; `texture` is a path, or a replaceable texture's number. */
export type MapGeoset = {
  positions: Float32Array;
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
};

/** A model as the view draws it; no geosets for one that draws as a marker. `variations` is 1 for most. */
export type MapModel = { geosets: MapGeoset[]; variations: number };

/**
 * A geoset's variation by its name, for a model that holds several shapes in one (*claim*: the HD Northrend tree names
 * its geosets northrendTree_rig:nt0_main_geo to nt9_...; how the game picks one is not checked): the number after the
 * letters that start the name's last part.
 */
function variationOf(name: string | undefined): number | null {
  const match = /(?:^|:)[a-z]+(\d+)_/i.exec(name ?? "");
  return match ? Number(match[1]) : null;
}

/** A bone's matrix in the pose (16 numbers in columns); undefined leaves its vertices where they are. */
export type Pose = (bone: number) => ArrayLike<number> | undefined;

/** A geoset's alpha in the pose, from 0 (hidden) to 1. */
export type Shown = (geoset: number) => number;

/** The filter modes drawn: none, transparent (alpha-tested) and blend. */
const drawn = [0, 1, 2];
/** The replaceable textures a model leaves to the game: the team colour and the team glow. */
const teamColour = 1;
const teamGlow = 2;

/** A geoset's vertices in the pose: an HD geoset by its skin, a Classic one by its vertex groups. */
export function bakeGeoset(
  geoset: Pick<Skinnable, "Vertices" | "VertexGroup" | "Groups" | "SkinWeights">,
  matrix: Pose,
): Float32Array {
  const count = geoset.Vertices.length / 3;
  if (geoset.SkinWeights?.length === count * 8) return posed(geoset, matrix);
  const skin = groupSkin(geoset as Skinnable, count);
  return posed({ Vertices: geoset.Vertices, SkinWeights: skin }, matrix);
}

type Texture = { Image?: string; ReplaceableId?: number };
type Layer = { FilterMode?: number; TextureID?: unknown; ShaderTypeId?: number };

/** What the view draws of a parsed model, in `pose`, leaving out the geosets the pose hides. */
export function mapModel(model: Model, pose: Pose, shown: Shown = () => 1): MapModel {
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
    geosets.push({
      positions: bakeGeoset(geoset, pose),
      normals: Float32Array.from(geoset.Normals),
      uvs: Float32Array.from(
        geoset.TVertices[0] ?? new Float32Array((geoset.Vertices.length / 3) * 2),
      ),
      indices: Uint16Array.from(geoset.Faces),
      texture: texture.ReplaceableId ? texture.ReplaceableId : (texture.Image ?? ""),
      filter,
      teamUnder,
      variation: several ? (named[index] ?? null) : null,
      hd: layers[0]?.ShaderTypeId === 1 || layers.length === 6,
    });
  });
  return { geosets, variations: several ? variations : 1 };
}

/**
 * The bones' matrices and the geosets' alphas in the first frame of the model's Stand animation (a tree hides its
 * stump and lumber there); the bind pose with every geoset shown for a model without animations.
 */
function poseOf(model: Model): { pose: Pose; shown: Shown } {
  const still = { pose: () => undefined, shown: () => 1 };
  try {
    const stand = standSequence(model.Sequences.map((sequence) => sequence.Name));
    if (stand < 0) return still;
    const renderer = new ModelRenderer(model);
    renderer.setSequence(stand);
    renderer.update(0);
    const data = (
      renderer as unknown as {
        rendererData?: { nodes?: { matrix: Float32Array }[]; geosetAlpha?: number[] };
      }
    ).rendererData;
    return {
      pose: (bone) => data?.nodes?.[bone]?.matrix,
      shown: (geoset) => data?.geosetAlpha?.[geoset] ?? 1,
    };
  } catch {
    return still;
  }
}

/** The model at a game file's URL as the view draws it; null when it is missing or cannot be read. */
export async function loadModel(url: string): Promise<MapModel | null> {
  try {
    const response = await fetch(url);
    if (!response.ok) return null;
    const buffer = stripChunks(await response.arrayBuffer(), unreadChunks);
    const model = parseMDX(buffer);
    skinGeosets(model.Geosets as unknown as Skinnable[], readSkins(buffer));
    const { pose, shown } = poseOf(model);
    return mapModel(model, pose, shown);
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
