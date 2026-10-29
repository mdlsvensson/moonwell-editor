/** Small WebGL2 helpers the map view's layers share. */

/** A shader program from its two sources; throws with the compiler's log when one does not compile. */
export function program(
  gl: WebGL2RenderingContext,
  vertex: string,
  fragment: string,
): WebGLProgram {
  const made = gl.createProgram();
  for (const [type, source] of [
    [gl.VERTEX_SHADER, vertex],
    [gl.FRAGMENT_SHADER, fragment],
  ] as const) {
    const shader = gl.createShader(type);
    if (!shader) throw new Error("No shader");
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      throw new Error(gl.getShaderInfoLog(shader) ?? "The shader does not compile");
    }
    gl.attachShader(made, shader);
    gl.deleteShader(shader);
  }
  gl.linkProgram(made);
  if (!gl.getProgramParameter(made, gl.LINK_STATUS)) {
    throw new Error(gl.getProgramInfoLog(made) ?? "The program does not link");
  }
  return made;
}

/** A float texture of one channel a texel, read with texelFetch (no filtering). */
export function floatTexture(
  gl: WebGL2RenderingContext,
  width: number,
  height: number,
  values: Float32Array,
): WebGLTexture {
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32F, width, height, 0, gl.RED, gl.FLOAT, values);
  nearest(gl);
  return texture;
}

/** A texture of whole numbers, `channels` bytes a texel (1 or 4), read with texelFetch. */
export function byteTexture(
  gl: WebGL2RenderingContext,
  width: number,
  height: number,
  channels: 1 | 4,
  values: Uint8Array,
): WebGLTexture {
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  if (channels === 1) {
    gl.texImage2D(
      gl.TEXTURE_2D,
      0,
      gl.R8UI,
      width,
      height,
      0,
      gl.RED_INTEGER,
      gl.UNSIGNED_BYTE,
      values,
    );
  } else {
    gl.texImage2D(
      gl.TEXTURE_2D,
      0,
      gl.RGBA8UI,
      width,
      height,
      0,
      gl.RGBA_INTEGER,
      gl.UNSIGNED_BYTE,
      values,
    );
  }
  nearest(gl);
  return texture;
}

/** A 1 by 1 texture of one colour, for a texture still loading or missing. */
export function plainTexture(
  gl: WebGL2RenderingContext,
  rgba: [number, number, number, number],
): WebGLTexture {
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.texImage2D(
    gl.TEXTURE_2D,
    0,
    gl.RGBA,
    1,
    1,
    0,
    gl.RGBA,
    gl.UNSIGNED_BYTE,
    new Uint8Array(rgba),
  );
  nearest(gl);
  return texture;
}

/** A colour 0xAARRGGBB as red, green, blue and alpha from 0 to 1. */
export function argb(colour: number): [number, number, number, number] {
  return [
    ((colour >>> 16) & 255) / 255,
    ((colour >>> 8) & 255) / 255,
    (colour & 255) / 255,
    ((colour >>> 24) & 255) / 255,
  ];
}

function nearest(gl: WebGL2RenderingContext): void {
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
}
