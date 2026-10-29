/**
 * The map view's textures: the game's DDS files go to the graphics card still compressed where WebGL takes their
 * compression (DXT1, DXT3, DXT5 through WEBGL_compressed_texture_s3tc), with the mipmaps the file holds; any other DDS
 * is decoded here, and so is a BLP (war3-model's decoder).
 */
import { decodeDds, parseHeaders } from "dds-parser";
import { decodeBLP, getBLPImageData } from "war3-model";

/** A DDS file's size, compression and images (the largest first). */
export type DdsInfo = {
  width: number;
  height: number;
  format: "dxt1" | "dxt3" | "dxt5" | "ati2" | "other";
  images: { offset: number; length: number; width: number; height: number }[];
};

/** The DDS header of `bytes`; null when they are not a DDS file dds-parser can read. */
export function ddsInfo(bytes: Uint8Array): DdsInfo | null {
  if (bytes.length < 128 || String.fromCharCode(...bytes.subarray(0, 4)) !== "DDS ") return null;
  try {
    const own = bytes.buffer.slice(
      bytes.byteOffset,
      bytes.byteOffset + bytes.byteLength,
    ) as ArrayBuffer;
    const info = parseHeaders(own);
    const format = ["dxt1", "dxt3", "dxt5", "ati2"].includes(info.format) ? info.format : "other";
    return {
      width: info.shape.width,
      height: info.shape.height,
      format,
      // dds-parser halves a side down to 0 on a sheet that is not square; WebGL wants every side at least 1.
      images: info.images.map((image) => ({
        offset: image.offset,
        length: image.length,
        width: Math.max(image.shape.width, 1),
        height: Math.max(image.shape.height, 1),
      })),
    };
  } catch {
    return null;
  }
}

/**
 * A piece of a ground texture's sheet as u0, v0, u1, v1 (v from the top): pieces 0 to 15 across the 4 by 4 grid row
 * by row from the top left, 16 to 31 the right half of an 8 by 4 sheet.
 */
export function pieceRect(piece: number, extended: boolean): [number, number, number, number] {
  const columns = extended ? 8 : 4;
  const right = piece >= 16 ? 4 : 0;
  const at = piece % 16;
  const column = right + (at % 4);
  const row = Math.floor(at / 4);
  return [column / columns, row / 4, (column + 1) / columns, (row + 1) / 4];
}

/** A texture made from a file's bytes, with mipmaps; null when they cannot be read. */
export function makeTexture(gl: WebGL2RenderingContext, data: ArrayBuffer): WebGLTexture | null {
  const bytes = new Uint8Array(data);
  const head = String.fromCharCode(...bytes.subarray(0, 4));
  if (head === "DDS ") return ddsTexture(gl, bytes);
  if (head.startsWith("BLP")) {
    try {
      const top = getBLPImageData(decodeBLP(data), 0);
      return rgbaTexture(gl, top.width, top.height, new Uint8Array(top.data.buffer));
    } catch {
      return null;
    }
  }
  return null;
}

function ddsTexture(gl: WebGL2RenderingContext, bytes: Uint8Array): WebGLTexture | null {
  const info = ddsInfo(bytes);
  const top = info?.images[0];
  if (!info || !top) return null;
  const s3tc = gl.getExtension("WEBGL_compressed_texture_s3tc");
  const format =
    s3tc &&
    {
      dxt1: s3tc.COMPRESSED_RGBA_S3TC_DXT1_EXT,
      dxt3: s3tc.COMPRESSED_RGBA_S3TC_DXT3_EXT,
      dxt5: s3tc.COMPRESSED_RGBA_S3TC_DXT5_EXT,
      ati2: undefined,
      other: undefined,
    }[info.format];
  if (format) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    // Each image the file holds is a mipmap; the chain may stop before 1 by 1.
    let levels = 0;
    for (const image of info.images) {
      gl.compressedTexImage2D(
        gl.TEXTURE_2D,
        levels++,
        format,
        image.width,
        image.height,
        0,
        bytes.subarray(image.offset, image.offset + image.length),
      );
    }
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAX_LEVEL, levels - 1);
    filter(gl);
    return texture;
  }
  if (info.format === "other") return null;
  try {
    const pixels = decodeDds(
      bytes.subarray(top.offset, top.offset + top.length),
      info.format,
      top.width,
      top.height,
    );
    return rgbaTexture(gl, top.width, top.height, new Uint8Array(pixels.buffer));
  } catch {
    return null;
  }
}

function rgbaTexture(
  gl: WebGL2RenderingContext,
  width: number,
  height: number,
  pixels: Uint8Array,
): WebGLTexture {
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
  gl.generateMipmap(gl.TEXTURE_2D);
  filter(gl);
  return texture;
}

function filter(gl: WebGL2RenderingContext): void {
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
}

/** The texture of a game file, fetched from the asset host; null when there is none or it cannot be read. */
export async function loadTexture(
  gl: WebGL2RenderingContext,
  url: string,
): Promise<WebGLTexture | null> {
  try {
    const response = await fetch(url);
    if (!response.ok) return null;
    return makeTexture(gl, await response.arrayBuffer());
  } catch {
    return null;
  }
}
