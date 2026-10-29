import { vec3 } from "gl-matrix";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  byModel,
  instanceMatrix,
  loadModel,
  mapModel,
  ownGeosets,
  settle,
  skinOf,
  texturePaths,
} from "./models.ts";
import type { PlacedThing } from "./scene.ts";

function thing(change: Partial<PlacedThing>): PlacedThing {
  return {
    number: 1,
    kind: "Unit",
    typeId: "hfoo",
    name: "Footman",
    ref: "gg_unit_hfoo_0001",
    x: 0,
    y: 0,
    z: 0,
    facing: 0,
    scaleX: 1,
    scaleY: 1,
    scaleZ: 1,
    model: "units\\human\\Footman\\Footman.mdl",
    texture: null,
    player: 0,
    tint: 0xffffff,
    variation: 0,
    ...change,
  };
}

describe("skinOf", () => {
  it("gives a Classic vertex its group's bones, shared", () => {
    const geoset = {
      Vertices: new Float32Array([1, 2, 3, 4, 5, 6]),
      Normals: new Float32Array(6),
      VertexGroup: new Uint8Array([0, 1]),
      Groups: [[0], [1, 2]],
    };
    expect(Array.from(skinOf(geoset))).toEqual([
      0, 0, 0, 0, 255, 0, 0, 0, 1, 2, 0, 0, 128, 127, 0, 0,
    ]);
  });

  it("gives an HD vertex its skin", () => {
    const skin = new Uint8Array([1, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 255, 0, 0, 0]);
    const geoset = {
      Vertices: new Float32Array([1, 2, 3, 4, 5, 6]),
      Normals: new Float32Array(6),
      VertexGroup: new Uint8Array([0, 0]),
      Groups: [[0]],
      SkinWeights: skin,
    };
    expect(skinOf(geoset)).toBe(skin);
  });
});

describe("instanceMatrix", () => {
  it("puts a model at its place, turned to its facing and scaled", () => {
    const matrix = instanceMatrix(
      thing({ x: 100, y: 200, z: 30, facing: Math.PI / 2, scaleX: 2, scaleY: 2, scaleZ: 2 }),
    );
    const ahead = vec3.transformMat4(vec3.create(), [1, 0, 0], matrix);
    expect(ahead[0]).toBeCloseTo(100, 5);
    expect(ahead[1]).toBeCloseTo(202, 5);
    expect(ahead[2]).toBeCloseTo(30, 5);
  });
});

describe("byModel", () => {
  it("groups by model, texture and variation, and leaves markers out", () => {
    const groups = byModel([
      thing({ number: 1 }),
      thing({ number: 2 }),
      thing({ number: 3, model: "Doodads\\Tree.mdl", texture: "Ashen.blp" }),
      thing({ number: 4, model: "Doodads\\Tree.mdl", texture: "Summer.blp" }),
      thing({ number: 5, model: null }),
      thing({ number: 6, variation: 2 }),
    ]);
    expect([...groups.values()].map((group) => group.map((each) => each.number))).toEqual([
      [1, 2],
      [3],
      [4],
      [6],
    ]);
  });
});

/** The parts of a parsed model `mapModel` reads, with one geoset of `filter` and its layers. */
function parsed(filter: number, layers: object[] = [{ FilterMode: filter, TextureID: 0 }]) {
  return {
    Geosets: [
      {
        Vertices: new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0]),
        Normals: new Float32Array([0, 0, 1, 0, 0, 1, 0, 0, 1]),
        TVertices: [new Float32Array([0, 0, 1, 0, 0, 1])],
        VertexGroup: new Uint8Array([0, 0, 0]),
        Groups: [[0]],
        Faces: new Uint16Array([0, 1, 2]),
        MaterialID: 0,
      },
    ],
    Materials: [{ Layers: layers }],
    Textures: [{ Image: "Textures\\Footman.blp" }, { ReplaceableId: 1 }],
    GeosetAnims: [],
  };
}

describe("mapModel", () => {
  it("keeps a solid geoset with its texture", () => {
    const model = mapModel(parsed(0) as never);
    expect(model.geosets).toHaveLength(1);
    expect(model.geosets[0]).toMatchObject({
      texture: "Textures\\Footman.blp",
      filter: 0,
      teamUnder: false,
    });
  });

  it("draws a texture over the team colour when the material puts it there", () => {
    const layers = [
      { FilterMode: 0, TextureID: 1 },
      { FilterMode: 1, TextureID: 0 },
    ];
    expect(mapModel(parsed(0, layers) as never).geosets[0]).toMatchObject({
      texture: "Textures\\Footman.blp",
      filter: 0,
      teamUnder: true,
    });
  });

  it("leaves out a geoset no frame of Stand shows (a tree's stump and lumber)", () => {
    expect(mapModel(parsed(0) as never, () => 0).geosets).toEqual([]);
    expect(mapModel(parsed(0) as never, () => 0.5).geosets).toHaveLength(1);
  });

  it("keeps each geoset in its bind pose with its number in the model and its skin", () => {
    const geoset = mapModel(parsed(0) as never).geosets[0];
    expect(Array.from(geoset?.positions ?? [])).toEqual([0, 0, 0, 1, 0, 0, 0, 1, 0]);
    expect(geoset?.index).toBe(0);
    expect(Array.from(geoset?.skin.subarray(0, 8) ?? [])).toEqual([0, 0, 0, 0, 255, 0, 0, 0]);
  });

  it("tells each geoset's variation in a model that holds several (an HD tree's nt0 to nt9)", () => {
    const two = parsed(0);
    const [geoset] = two.Geosets;
    two.Geosets = [
      { ...geoset, Name: "northrendTree_rig:nt0_main_geo" },
      { ...geoset, Name: "northrendTree_rig:nt1_main_geo" },
      { ...geoset, Name: "northrendTree_rig:shadow" },
    ] as unknown as typeof two.Geosets;
    const model = mapModel(two as never);
    expect(model.variations).toBe(2);
    expect(model.geosets.map((each) => each.variation)).toEqual([0, 1, null]);
    expect(mapModel(parsed(0) as never).variations).toBe(1);
  });

  it("takes numbers in names for shapes only when they run 0, 1, 2 ... (a cape2 is not a second shape)", () => {
    const named = parsed(0);
    const [geoset] = named.Geosets;
    named.Geosets = [
      { ...geoset, Name: "hero_rig:body1_geo" },
      { ...geoset, Name: "hero_rig:cape2_geo" },
    ] as unknown as typeof named.Geosets;
    const model = mapModel(named as never);
    expect(model.variations).toBe(1);
    expect(model.geosets.map((each) => each.variation)).toEqual([null, null]);
  });

  it("tells an HD geoset by its material's shader", () => {
    const hd = parsed(0, [{ FilterMode: 0, TextureID: 0, ShaderTypeId: 1 }]);
    expect(mapModel(hd as never).geosets[0]?.hd).toBe(true);
    expect(mapModel(parsed(0) as never).geosets[0]?.hd).toBe(false);
  });

  it("gives an HD geoset its ORM texture, whose alpha marks the team colour (a 1100 layer's ORMTextureID, a 1000 material's third layer)", () => {
    const textures = [
      { Image: "Troll_diffuse.dds" },
      { Image: "Troll_normal.dds" },
      { Image: "Troll_orm.dds" },
      { ReplaceableId: 1 },
    ];
    const newer = {
      ...parsed(0, [
        { FilterMode: 0, TextureID: 0, NormalTextureID: 1, ORMTextureID: 2, ShaderTypeId: 1 },
      ]),
      Textures: textures,
    };
    const older = {
      ...parsed(
        0,
        [0, 1, 2, 0, 3, 0].map((id) => ({ FilterMode: 0, TextureID: id })),
      ),
      Textures: textures,
    };
    expect(mapModel(newer as never).geosets[0]?.orm).toBe("Troll_orm.dds");
    expect(mapModel(older as never).geosets[0]).toMatchObject({
      texture: "Troll_diffuse.dds",
      orm: "Troll_orm.dds",
    });
    expect(mapModel(parsed(0) as never).geosets[0]?.orm).toBeNull();
  });

  it("gives an HD geoset its normal map, emissive map and tangents, and a Classic one none", () => {
    const textures = [
      { Image: "Troll_diffuse.dds" },
      { Image: "Troll_normal.dds" },
      { Image: "Troll_orm.dds" },
      { Image: "Troll_emissive.dds" },
      { ReplaceableId: 1 },
    ];
    const tangents = new Float32Array([1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 0, -1]);
    const newer = {
      ...parsed(0, [
        {
          FilterMode: 0,
          TextureID: 0,
          NormalTextureID: 1,
          ORMTextureID: 2,
          EmissiveTextureID: 3,
          ShaderTypeId: 1,
        },
      ]),
      Textures: textures,
    };
    (newer.Geosets[0] as { Tangents?: Float32Array }).Tangents = tangents;
    const older = {
      ...parsed(
        0,
        [0, 1, 2, 3, 4, 0].map((id) => ({ FilterMode: 0, TextureID: id })),
      ),
      Textures: textures,
    };
    expect(mapModel(newer as never).geosets[0]).toMatchObject({
      normalMap: "Troll_normal.dds",
      emissive: "Troll_emissive.dds",
      tangents,
    });
    expect(mapModel(older as never).geosets[0]).toMatchObject({
      normalMap: "Troll_normal.dds",
      emissive: "Troll_emissive.dds",
      tangents: null,
    });
    expect(mapModel(parsed(0) as never).geosets[0]).toMatchObject({
      normalMap: null,
      emissive: null,
      tangents: null,
    });
  });

  it("gives an HD geoset its emissive gain, the first key of an animated one, 1 without", () => {
    const layer = { FilterMode: 0, TextureID: 0, EmissiveTextureID: 0, ShaderTypeId: 1 };
    const gain = (EmissiveGain: unknown) =>
      mapModel(parsed(0, [{ ...layer, EmissiveGain }]) as never).geosets[0]?.emissiveGain;
    expect(gain(0.25)).toBe(0.25);
    expect(gain({ Keys: [{ Frame: 0, Vector: new Float32Array([0.5]) }] })).toBe(0.5);
    expect(gain(undefined)).toBe(1);
  });

  it("leaves glows out, so a model of glows and particles only is a marker", () => {
    expect(mapModel(parsed(3) as never).geosets).toEqual([]);
    expect(mapModel({ ...parsed(0), Geosets: [] } as never).geosets).toEqual([]);
  });
});

describe("loadModel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("is null for a file that is not a model", async () => {
    vi.stubGlobal("fetch", async () => new Response(new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8])));
    expect(await loadModel("https://game.moonwelleditor/file?path=x")).toBeNull();
  });

  it("is null when the file is missing", async () => {
    vi.stubGlobal("fetch", async () => new Response(null, { status: 404 }));
    expect(await loadModel("https://game.moonwelleditor/file?path=x")).toBeNull();
  });
});

describe("texturePaths", () => {
  it("gives an HD geoset the HD version of a tree's texture first", () => {
    const tree = String.raw`ReplaceableTextures\LordaeronTree\LordaeronSnowTree`;
    expect(texturePaths(31, tree, true)).toEqual([`${tree}_diffuse.blp`, `${tree}.blp`]);
    expect(texturePaths(31, tree, false)).toEqual([`${tree}.blp`]);
    expect(texturePaths(11, String.raw`TerrainArt\Icecrown\Ice_Cliff0.blp`, true)).toEqual([
      String.raw`TerrainArt\Icecrown\Ice_Cliff0_diffuse.blp`,
      String.raw`TerrainArt\Icecrown\Ice_Cliff0.blp`,
    ]);
  });

  it("takes a file the model names as it is, and nothing for the team colour", () => {
    expect(texturePaths(String.raw`Textures\Footman.blp`, null, true)).toEqual([
      String.raw`Textures\Footman.blp`,
    ]);
    expect(texturePaths(1, null, false)).toEqual([]);
    expect(texturePaths(31, null, false)).toEqual([]);
  });

  it("gives an HD cliff model's own cliff texture file the cliff type's texture (CliffTypes.slk overrideTexture)", () => {
    const cliff = String.raw`TerrainArt\Dungeon\Dungeon_Cliff1.blp`;
    expect(texturePaths("ReplaceableTextures/Cliff/Cliff1_Diffuse.tif", cliff, true)).toEqual([
      String.raw`TerrainArt\Dungeon\Dungeon_Cliff1_diffuse.blp`,
      cliff,
    ]);
    // A ramp's HD model names it in another folder (Ekrezem's Maze, 2026-10-29).
    expect(texturePaths("Doodads/Terrain/#ignore_Cliffs/Cliff1_Diffuse.tif", cliff, false)).toEqual(
      [cliff],
    );
    // Not for a model that is not a cliff (no cliff texture on its batch).
    expect(texturePaths("ReplaceableTextures/Cliff/Cliff1_Diffuse.tif", null, true)).toEqual([
      "ReplaceableTextures/Cliff/Cliff1_Diffuse.tif",
    ]);
  });
});

describe("ownGeosets", () => {
  const geoset = (variation: number | null) =>
    ({ variation }) as unknown as import("./models.ts").MapGeoset;

  it("gives a thing its own shape's geosets and those of every shape", () => {
    const model = { variations: 2, geosets: [geoset(0), geoset(1), geoset(null)] };
    expect(ownGeosets(model, 3).map((each) => each.variation)).toEqual([1, null]);
    expect(ownGeosets(model, 0).map((each) => each.variation)).toEqual([0, null]);
  });

  it("gives none for a shape whose geosets all draw nothing, so the thing is a marker", () => {
    expect(ownGeosets({ variations: 2, geosets: [geoset(0)] }, 1)).toEqual([]);
  });
});

describe("settle", () => {
  /** A one-frame animation of two bones, bone 1 moved 10 along x and turned a quarter about z. */
  const animation = {
    frames: 1,
    rate: 30,
    bones: 2,
    geosets: 1,
    poses: new Float32Array([
      1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 10, 1, 0, 0, 0, 0, 0, 1, 0,
    ]),
    alphas: new Float32Array([1]),
  };

  it("bakes a model whose animation never moves into its pose, and drops the animation", () => {
    const model = mapModel(parsed(0) as never);
    const geoset = model.geosets[0];
    if (!geoset) throw new Error("no geoset");
    geoset.skin = new Uint8Array(24).fill(0);
    for (let v = 0; v < 3; v++) geoset.skin.set([1, 0, 0, 0, 255, 0, 0, 0], v * 8);
    const settled = settle({ ...model, animation });
    expect(settled.animation).toBeNull();
    expect(Array.from(settled.geosets[0]?.positions ?? [])).toEqual([10, 0, 0, 10, 1, 0, 9, 0, 0]);
    expect(Array.from(settled.geosets[0]?.normals.subarray(0, 3) ?? [])).toEqual([0, 0, 1]);
  });

  it("keeps a model whose animation moves as it is", () => {
    const model = mapModel(parsed(0) as never);
    const moving = {
      ...animation,
      frames: 2,
      poses: new Float32Array(48),
      alphas: new Float32Array([1, 0]),
    };
    expect(settle({ ...model, animation: moving }).animation).toBe(moving);
  });
});
