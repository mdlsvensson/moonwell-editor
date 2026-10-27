import { describe, expect, it } from "vitest";
import { frame, type Shape, solidExtent, standSequence, stripChunks } from "./mdx.ts";

/** An MDX file from the chunks given, each as its tag and its data bytes. */
function mdx(...chunks: [string, number[]][]): ArrayBuffer {
  const parts: number[] = [..."MDLX"].map((c) => c.charCodeAt(0));
  for (const [tag, data] of chunks) {
    const size = data.length;
    parts.push(...[...tag].map((c) => c.charCodeAt(0)));
    parts.push(size & 0xff, (size >> 8) & 0xff, (size >> 16) & 0xff, size >>> 24);
    parts.push(...data);
  }
  return new Uint8Array(parts).buffer;
}

describe("stripChunks", () => {
  it("drops the chunks with the tags given and keeps the rest in order", () => {
    const file = mdx(
      ["VERS", [0x08, 0x07, 0, 0]],
      ["CAMS", [1, 2, 3]],
      ["GEOS", [4, 5]],
      ["LITE", []],
    );
    const out = stripChunks(file, ["CAMS", "LITE"]);
    expect(new Uint8Array(out)).toEqual(
      new Uint8Array(mdx(["VERS", [0x08, 0x07, 0, 0]], ["GEOS", [4, 5]])),
    );
  });

  it("gives an equal copy when no chunk has a tag given", () => {
    const file = mdx(["VERS", [0x08, 0x07, 0, 0]], ["MODL", [9]]);
    const out = stripChunks(file, ["CAMS"]);
    expect(out).not.toBe(file);
    expect(new Uint8Array(out)).toEqual(new Uint8Array(file));
  });

  it("reads a chunk's size as little-endian, past 255 bytes", () => {
    const big = Array.from({ length: 300 }, (_, i) => i & 0xff);
    const out = stripChunks(mdx(["CAMS", big], ["TEXS", [7]]), ["CAMS"]);
    expect(new Uint8Array(out)).toEqual(new Uint8Array(mdx(["TEXS", [7]])));
  });

  it("refuses bytes that are not an MDX file", () => {
    expect(() => stripChunks(new TextEncoder().encode("Version 800").buffer, [])).toThrow(
      "Not an MDX model",
    );
  });

  it("refuses a file whose last chunk runs past the end", () => {
    const cut = mdx(["VERS", [1, 2, 3, 4]]).slice(0, 14);
    expect(() => stripChunks(cut, [])).toThrow("cut short");
    expect(() => stripChunks(mdx(["VERS", []]).slice(0, 10), [])).toThrow("cut short");
  });
});

describe("standSequence", () => {
  it("picks the first animation whose name starts with Stand", () => {
    expect(standSequence(["Walk", "Attack - 1", "Stand Work", "Stand - 1"])).toBe(2);
    expect(standSequence(["Birth", "stand"])).toBe(1);
  });

  it("falls back to the first animation, and to -1 when there is none", () => {
    expect(standSequence(["Birth", "Death"])).toBe(0);
    expect(standSequence([])).toBe(-1);
  });
});

describe("frame", () => {
  it("centres on the first extent with a size and reaches half its diagonal", () => {
    const empty = { min: [0, 0, 0], max: [0, 0, 0] };
    const box = { min: [-30, -40, 0], max: [30, 40, 120] };
    const framing = frame([undefined, empty, box]);
    expect(framing.centre).toEqual([0, 0, 60]);
    expect(framing.radius).toBeCloseTo(Math.hypot(60, 80, 120) / 2);
  });

  it("falls back to the origin when no extent has a size", () => {
    expect(frame([])).toEqual({ centre: [0, 0, 0], radius: 100 });
  });
});

describe("solidExtent", () => {
  /** A model with a body (alpha-tested), a team glow disc, an additive glow, and a body part hidden in the animation. */
  const model: Shape = {
    Geosets: [
      { Vertices: [-10, -10, 0, 10, 10, 100], MaterialID: 0 },
      { Vertices: [-200, -200, 0, 200, 200, 0], MaterialID: 1 },
      { Vertices: [0, 0, 0, 0, 0, 900], MaterialID: 2 },
      { Vertices: [0, 0, 0, 50, 0, 0], MaterialID: 0 },
    ],
    Materials: [
      { Layers: [{ FilterMode: 1, TextureID: 0 }] },
      { Layers: [{ FilterMode: 2, TextureID: 1 }] },
      { Layers: [{ FilterMode: 3, TextureID: 0 }] },
    ],
    Textures: [{ ReplaceableId: 0 }, { ReplaceableId: 2 }],
    GeosetAnims: [
      {
        GeosetId: 3,
        Alpha: {
          Keys: [
            { Frame: 0, Vector: [0] },
            { Frame: 2000, Vector: [1] },
          ],
        },
      },
    ],
  };

  it("leaves out glows, the team glow and geosets hidden in the animation", () => {
    expect(solidExtent(model, [0, 1000])).toEqual({ min: [-10, -10, 0], max: [10, 10, 100] });
  });

  it("counts a geoset the animation shows", () => {
    expect(solidExtent(model, [2000, 3000])).toEqual({ min: [-10, -10, 0], max: [50, 10, 100] });
  });

  it("is undefined when no geoset is solid", () => {
    expect(solidExtent({ ...model, Geosets: model.Geosets.slice(2, 3) })).toBeUndefined();
  });
});
