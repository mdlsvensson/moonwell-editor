import { describe, expect, it } from "vitest";
import { ddsInfo, pieceRect } from "./textures.ts";

/** A DDS file's header with room for its largest image, as Lords_Dirt.dds has it: 512 by 256 DXT5. */
function dds(width: number, height: number, fourCC: string, mipmaps = 1): Uint8Array {
  const bytes = new Uint8Array(128 + width * height * 2);
  const view = new DataView(bytes.buffer);
  bytes.set([68, 68, 83, 32], 0); // "DDS "
  view.setUint32(4, 124, true);
  view.setUint32(8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000 | (mipmaps > 1 ? 0x20000 : 0), true);
  view.setUint32(12, height, true);
  view.setUint32(16, width, true);
  view.setUint32(20, width * height, true);
  view.setUint32(28, mipmaps, true);
  view.setUint32(76, 32, true);
  view.setUint32(80, 0x4, true);
  for (let i = 0; i < 4; i++) bytes[84 + i] = fourCC.charCodeAt(i);
  view.setUint32(108, 0x1000 | (mipmaps > 1 ? 0x400000 | 0x8 : 0), true);
  return bytes;
}

describe("ddsInfo", () => {
  it("reads the size and format", () => {
    expect(ddsInfo(dds(512, 256, "DXT5"))).toMatchObject({
      width: 512,
      height: 256,
      format: "dxt5",
    });
    expect(ddsInfo(dds(256, 256, "DXT1"))?.format).toBe("dxt1");
  });

  it("gives a wide sheet's last mipmaps a side of at least 1", () => {
    // dds-parser halves each side to 0 (512 by 256: its tenth image is 1 by 0), which WebGL refuses, and a texture
    // missing a mipmap draws black (Ice_RuneBricks, seen 2026-10-29).
    const images = ddsInfo(dds(512, 256, "DXT5", 10))?.images ?? [];
    expect(images).toHaveLength(10);
    expect(images.at(-1)).toMatchObject({ width: 1, height: 1 });
    expect(images.at(-2)).toMatchObject({ width: 2, height: 1 });
  });

  it("is null for bytes that are not a DDS", () => {
    expect(ddsInfo(new Uint8Array([66, 76, 80, 49, 0, 0, 0, 0]))).toBeNull();
  });
});

describe("pieceRect", () => {
  it("finds a piece of a 4 by 4 sheet", () => {
    expect(pieceRect(0, false)).toEqual([0, 0, 0.25, 0.25]);
    expect(pieceRect(15, false)).toEqual([0.75, 0.75, 1, 1]);
  });

  it("finds the variations in the right half of an 8 by 4 sheet", () => {
    expect(pieceRect(15, true)).toEqual([0.375, 0.75, 0.5, 1]);
    expect(pieceRect(16, true)).toEqual([0.5, 0, 0.625, 0.25]);
    expect(pieceRect(31, true)).toEqual([0.875, 0.75, 1, 1]);
  });
});
