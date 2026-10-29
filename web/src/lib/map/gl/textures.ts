/**
 * The map view's textures: the game's DDS files go to the graphics card still compressed where WebGL takes their
 * compression (DXT1, DXT3, DXT5 through WEBGL_compressed_texture_s3tc; ATI2, an HD normal map's two channels, through
 * EXT_texture_compression_rgtc), with the mipmaps the file holds; any other DDS is decoded here, and so is a BLP
 * (war3-model's decoder).
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

/**
 * The pieces of one image of a ground texture's sheet, each piece's blocks in a run of its own, pieces in order (0 to 15
 * across the left 4 by 4 grid row by row from the top left, 16 to 31 the right half of an 8 by 4 sheet): the layers of a
 * texture array, so a tile's piece cannot borrow its neighbours' texels at its edges or in smaller mipmaps (the seams
 * between tiles, Sven 2026-10-29). A block is `blockSize` texels square (4 for DXT, 1 for decoded pixels) and
 * `blockBytes` long; a piece must be at least a block.
 */
export function cutPieces(
  image: Uint8Array,
  width: number,
  height: number,
  blockBytes: number,
  extended: boolean,
  blockSize = 4,
): Uint8Array {
  const columns = extended ? 8 : 4;
  const pieces = columns * 4;
  const side = Math.max(1, height / 4 / blockSize);
  const perRow = Math.max(1, width / blockSize);
  const run = side * blockBytes;
  const out = new Uint8Array(pieces * side * run);
  let at = 0;
  for (let piece = 0; piece < pieces; piece++) {
    const column = (piece >= 16 ? 4 : 0) + ((piece % 16) % 4);
    const row = Math.floor((piece % 16) / 4);
    for (let line = 0; line < side; line++) {
      const from = ((row * side + line) * perRow + column * side) * blockBytes;
      out.set(image.subarray(from, from + run), at);
      at += run;
    }
  }
  return out;
}

/**
 * A ground texture as a texture array of its pieces (see `cutPieces`), with mipmaps: a DDS's own mipmaps still
 * compressed, down to the last whose pieces are a block; a texture WebGL cannot take compressed decoded, its mipmaps
 * made. Null when the bytes cannot be read.
 */
export function makeGroundTexture(
  gl: WebGL2RenderingContext,
  data: ArrayBuffer,
): WebGLTexture | null {
  const bytes = new Uint8Array(data);
  const head = String.fromCharCode(...bytes.subarray(0, 4));
  const info = head === "DDS " ? ddsInfo(bytes) : null;
  const s3tc = gl.getExtension("WEBGL_compressed_texture_s3tc");
  const format =
    info &&
    s3tc &&
    {
      dxt1: s3tc.COMPRESSED_RGBA_S3TC_DXT1_EXT,
      dxt3: s3tc.COMPRESSED_RGBA_S3TC_DXT3_EXT,
      dxt5: s3tc.COMPRESSED_RGBA_S3TC_DXT5_EXT,
      ati2: undefined,
      other: undefined,
    }[info.format];
  if (info && format) {
    const extended = info.width === info.height * 2;
    const layers = extended ? 32 : 16;
    const blockBytes = info.format === "dxt1" ? 8 : 16;
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D_ARRAY, texture);
    let levels = 0;
    for (const image of info.images) {
      const side = image.height / 4;
      if (side < 4) break;
      const cut = cutPieces(
        bytes.subarray(image.offset, image.offset + image.length),
        image.width,
        image.height,
        blockBytes,
        extended,
      );
      gl.compressedTexImage3D(gl.TEXTURE_2D_ARRAY, levels++, format, side, side, layers, 0, cut);
    }
    if (levels === 0) return null;
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MAX_LEVEL, levels - 1);
    arrayFilter(gl);
    return texture;
  }
  const pixels = decoded(bytes, data, info);
  if (!pixels) return null;
  const extended = pixels.width === pixels.height * 2;
  const side = pixels.height / 4;
  const cut = cutPieces(pixels.data, pixels.width, pixels.height, 4, extended, 1);
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D_ARRAY, texture);
  gl.texImage3D(
    gl.TEXTURE_2D_ARRAY,
    0,
    gl.RGBA,
    side,
    side,
    extended ? 32 : 16,
    0,
    gl.RGBA,
    gl.UNSIGNED_BYTE,
    cut,
  );
  gl.generateMipmap(gl.TEXTURE_2D_ARRAY);
  arrayFilter(gl);
  return texture;
}

/** A texture array of one texel in one layer, for a ground texture still loading or missing. */
export function plainArray(
  gl: WebGL2RenderingContext,
  rgba: [number, number, number, number],
): WebGLTexture {
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D_ARRAY, texture);
  gl.texImage3D(
    gl.TEXTURE_2D_ARRAY,
    0,
    gl.RGBA,
    1,
    1,
    1,
    0,
    gl.RGBA,
    gl.UNSIGNED_BYTE,
    new Uint8Array(rgba),
  );
  gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
  return texture;
}

// The largest image of a DDS WebGL cannot take compressed, or of a BLP, as pixels; null when neither reads.
function decoded(
  bytes: Uint8Array,
  data: ArrayBuffer,
  info: DdsInfo | null,
): { width: number; height: number; data: Uint8Array } | null {
  try {
    const top = info?.images[0];
    if (info && top && info.format !== "other") {
      const pixels = decodeDds(
        bytes.subarray(top.offset, top.offset + top.length),
        info.format,
        top.width,
        top.height,
      );
      return { width: top.width, height: top.height, data: new Uint8Array(pixels.buffer) };
    }
    if (String.fromCharCode(...bytes.subarray(0, 3)) === "BLP") {
      const top = getBLPImageData(decodeBLP(data), 0);
      return { width: top.width, height: top.height, data: new Uint8Array(top.data.buffer) };
    }
  } catch {
    return null;
  }
  return null;
}

// Mipmapped, clamped at a piece's edges, and as sharp as the card allows when the ground is seen at a slant.
function arrayFilter(gl: WebGL2RenderingContext): void {
  gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
  gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  const anisotropic = gl.getExtension("EXT_texture_filter_anisotropic");
  if (anisotropic) {
    const most = gl.getParameter(anisotropic.MAX_TEXTURE_MAX_ANISOTROPY_EXT) as number;
    gl.texParameterf(
      gl.TEXTURE_2D_ARRAY,
      anisotropic.TEXTURE_MAX_ANISOTROPY_EXT,
      Math.min(8, most),
    );
  }
}

/**
 * A two-channel image as dds-parser decodes it (each pixel's red and green bytes packed at the front of a buffer of
 * four bytes a pixel) spread to red, green, a full blue and a full alpha.
 */
export function twoChannels(packed: Uint8Array, pixels: number): Uint8Array {
  const out = new Uint8Array(pixels * 4).fill(255);
  for (let pixel = 0; pixel < pixels; pixel++) {
    out[pixel * 4] = packed[pixel * 2] ?? 0;
    out[pixel * 4 + 1] = packed[pixel * 2 + 1] ?? 0;
  }
  return out;
}

/**
 * A texture made from a file's bytes, with mipmaps; null when they cannot be read. It repeats past its edges when
 * `repeat` (a texture tiled across the world, the water's), else stops at them.
 */
export function makeTexture(
  gl: WebGL2RenderingContext,
  data: ArrayBuffer,
  repeat = false,
): WebGLTexture | null {
  const bytes = new Uint8Array(data);
  const head = String.fromCharCode(...bytes.subarray(0, 4));
  if (head === "DDS ") return ddsTexture(gl, bytes, repeat);
  if (head.startsWith("BLP")) {
    try {
      const top = getBLPImageData(decodeBLP(data), 0);
      return rgbaTexture(gl, top.width, top.height, new Uint8Array(top.data.buffer), repeat);
    } catch {
      return null;
    }
  }
  return null;
}

function ddsTexture(
  gl: WebGL2RenderingContext,
  bytes: Uint8Array,
  repeat: boolean,
): WebGLTexture | null {
  const info = ddsInfo(bytes);
  const top = info?.images[0];
  if (!info || !top) return null;
  const s3tc = gl.getExtension("WEBGL_compressed_texture_s3tc");
  const rgtc = gl.getExtension("EXT_texture_compression_rgtc");
  const format = {
    dxt1: s3tc?.COMPRESSED_RGBA_S3TC_DXT1_EXT,
    dxt3: s3tc?.COMPRESSED_RGBA_S3TC_DXT3_EXT,
    dxt5: s3tc?.COMPRESSED_RGBA_S3TC_DXT5_EXT,
    ati2: rgtc?.COMPRESSED_RED_GREEN_RGTC2_EXT,
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
    filter(gl, repeat);
    return texture;
  }
  if (info.format === "other") return null;
  try {
    const pixels = new Uint8Array(
      decodeDds(
        bytes.subarray(top.offset, top.offset + top.length),
        info.format,
        top.width,
        top.height,
      ).buffer,
    );
    const rgba = info.format === "ati2" ? twoChannels(pixels, top.width * top.height) : pixels;
    return rgbaTexture(gl, top.width, top.height, rgba, repeat);
  } catch {
    return null;
  }
}

function rgbaTexture(
  gl: WebGL2RenderingContext,
  width: number,
  height: number,
  pixels: Uint8Array,
  repeat: boolean,
): WebGLTexture {
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
  gl.generateMipmap(gl.TEXTURE_2D);
  filter(gl, repeat);
  return texture;
}

function filter(gl: WebGL2RenderingContext, repeat: boolean): void {
  const wrap = repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE;
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, wrap);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, wrap);
}

/**
 * The texture of a game file, fetched from the asset host (repeating past its edges when `repeat`); null when there is
 * none or it cannot be read.
 */
export async function loadTexture(
  gl: WebGL2RenderingContext,
  url: string,
  repeat = false,
): Promise<WebGLTexture | null> {
  try {
    const response = await fetch(url);
    if (!response.ok) return null;
    return makeTexture(gl, await response.arrayBuffer(), repeat);
  } catch {
    return null;
  }
}
