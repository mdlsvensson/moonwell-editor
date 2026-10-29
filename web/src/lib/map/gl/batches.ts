/**
 * The copies of each model the view draws together (an instanced draw): a placed thing's place, tint, team colour and
 * number, or a cliff's tile and level. Each copy is `instanceFloats` numbers: its matrix (16, in columns), its tint
 * (3), its team colour (3) and its number (1, 0 for a cliff, which cannot be picked).
 */
import { mat4 } from "gl-matrix";
import { teamRgb } from "../../teamColors.ts";
import { byModel, instanceMatrix } from "../models.ts";
import type { MapScene, PlacedThing, ThingKind } from "../scene.ts";

export const instanceFloats = 23;

/** One model's copies. */
export type Batch = {
  key: string;
  model: string;
  /** The texture the model's replaceable texture takes (a cliff type's, a tree's), or null. */
  texture: string | null;
  /** Whether the copies are cliffs, whose vertices stand on the ground beneath them. */
  cliff: boolean;
  /** The copies' variation, which picks the geosets of a model that holds several shapes. */
  variation: number;
  /** What the copies are, for the view's layers; null for cliffs. */
  kind: ThingKind | null;
  instances: Float32Array;
  count: number;
  /** The things' numbers, in the order of their copies. */
  numbers: number[];
};

function write(
  floats: Float32Array,
  at: number,
  matrix: mat4,
  tint: number,
  team: [number, number, number],
  number: number,
): void {
  const base = at * instanceFloats;
  floats.set(matrix, base);
  floats.set(
    [((tint >>> 16) & 255) / 255, ((tint >>> 8) & 255) / 255, (tint & 255) / 255],
    base + 16,
  );
  floats.set(team, base + 19);
  floats[base + 22] = number;
}

/**
 * The cliff and ramp models, each at its tile's bottom-left corner and its lowest cliff level, turned a quarter
 * clockwise (*claim*, HiveWE: the game's cliff models lie so), in its cliff type's texture.
 */
export function cliffBatches(scene: MapScene): Batch[] {
  const groups = new Map<string, MapScene["cliffs"]>();
  for (const piece of scene.cliffs) {
    const key = `${piece.model}|${piece.cliffType}`;
    const group = groups.get(key);
    if (group) group.push(piece);
    else groups.set(key, [piece]);
  }
  return [...groups].map(([key, pieces]) => {
    const instances = new Float32Array(pieces.length * instanceFloats);
    pieces.forEach((piece, index) => {
      const matrix = mat4.fromTranslation(mat4.create(), [
        scene.left + piece.column * 128,
        scene.bottom + piece.row * 128,
        piece.baseZ,
      ]);
      mat4.rotateZ(matrix, matrix, -Math.PI / 2);
      write(instances, index, matrix, 0xffffff, [1, 1, 1], 0);
    });
    const first = pieces[0];
    return {
      key,
      model: first?.model ?? "",
      texture: scene.cliffTextures[first?.cliffType ?? 0] ?? null,
      cliff: true,
      variation: 0,
      kind: null,
      instances,
      count: pieces.length,
      numbers: pieces.map(() => 0),
    };
  });
}

/** The placed things with a model, a batch per model and texture. */
export function thingBatches(things: readonly PlacedThing[]): Batch[] {
  return [...byModel(things)].map(([key, group]) => {
    const instances = new Float32Array(group.length * instanceFloats);
    group.forEach((thing, index) => {
      write(
        instances,
        index,
        instanceMatrix(thing),
        thing.tint,
        teamRgb(thing.player),
        thing.number,
      );
    });
    return {
      key,
      model: group[0]?.model ?? "",
      texture: group[0]?.texture ?? null,
      cliff: false,
      variation: group[0]?.variation ?? 0,
      kind: group[0]?.kind ?? null,
      instances,
      count: group.length,
      numbers: group.map((thing) => thing.number),
    };
  });
}

/** A marker's colour by what it marks, red, green and blue from 0 to 1. */
export function markerColour(kind: ThingKind): [number, number, number] {
  switch (kind) {
    case "StartLocation":
      return [1, 0.85, 0.2];
    case "Item":
      return [0.3, 0.9, 1];
    case "Destructable":
    case "Doodad":
      return [0.6, 0.85, 0.4];
    default:
      return [1, 0.35, 0.35];
  }
}
