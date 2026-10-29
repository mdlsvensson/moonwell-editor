/**
 * The ground: every tile a square between its four corners at their heights, drawn in one pass per ground texture,
 * lowest first, each pass only the tiles whose layers use that texture (an instanced draw). A tile's first layer is
 * drawn whole, the layers after it in their pieces' alpha over it. A tile under a cliff model is not drawn; one outside
 * the playable area is dimmed. WebGL2 cannot pick one of several samplers by a tile's texture, so each sheet keeps its
 * own texture: an array of its pieces (see cutPieces), compressed as the game's file has it, so a tile never samples
 * its neighbouring pieces (the seams between tiles, Sven 2026-10-29).
 */
import type { mat4 } from "gl-matrix";
import type { Terrain, TerrainTexture } from "../scene.ts";
import { byteTexture, floatTexture, program } from "./program.ts";
import { plainArray } from "./textures.ts";

const vertex = `#version 300 es
precision highp float;
precision highp int;
precision highp usampler2D;
precision highp sampler2D;
layout(location = 0) in vec2 corner;
layout(location = 1) in uint tile;
layout(location = 2) in uint piece;
uniform mat4 viewProjection;
uniform sampler2D heights;
uniform usampler2D flags;
uniform ivec2 size;
uniform vec2 origin;
out vec2 uv;
out vec3 normal;
flat out float layer;
flat out float whole;
flat out float dim;
void main() {
  int across = size.x - 1;
  ivec2 at = ivec2(int(tile) % across, int(tile) / across);
  ivec2 c = at + ivec2(corner);
  float z = texelFetch(heights, c, 0).r;
  float l = texelFetch(heights, ivec2(max(c.x - 1, 0), c.y), 0).r;
  float r = texelFetch(heights, ivec2(min(c.x + 1, size.x - 1), c.y), 0).r;
  float d = texelFetch(heights, ivec2(c.x, max(c.y - 1, 0)), 0).r;
  float u = texelFetch(heights, ivec2(c.x, min(c.y + 1, size.y - 1)), 0).r;
  normal = normalize(vec3(l - r, d - u, 256.0));
  // The piece is its own layer; its top row is the tile's north edge.
  layer = float(piece & 255u);
  uv = vec2(corner.x, 1.0 - corner.y);
  whole = (piece >> 8) != 0u ? 1.0 : 0.0;
  dim = (texelFetch(flags, at, 0).r & 2u) != 0u ? 0.55 : 1.0;
  gl_Position = viewProjection * vec4(origin + vec2(c) * 128.0, z, 1.0);
}`;

const fragment = `#version 300 es
precision highp float;
precision highp sampler2DArray;
in vec2 uv;
in vec3 normal;
flat in float layer;
flat in float whole;
flat in float dim;
uniform sampler2DArray sheet;
uniform vec3 light;
out vec4 color;
void main() {
  vec4 texel = texture(sheet, vec3(uv, layer));
  float shade = clamp(dot(normalize(normal), light) * 0.45 + 0.65, 0.0, 1.15);
  color = vec4(texel.rgb * shade * dim, whole > 0.5 ? 1.0 : texel.a);
}`;

/** One texture's pass: its sheet and the tiles that use it, two numbers each (the tile, its piece and wholeness). */
type Pass = { texture: TerrainTexture; sheet: WebGLTexture; instances: WebGLBuffer; count: number };

export class GroundLayer {
  private readonly program: WebGLProgram;
  private readonly vao: WebGLVertexArrayObject;
  private readonly corners: WebGLBuffer;
  private readonly heights: WebGLTexture;
  private readonly flags: WebGLTexture;
  private readonly passes: Pass[];
  private readonly uniforms: Record<string, WebGLUniformLocation | null>;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly terrain: Terrain,
    textures: TerrainTexture[],
  ) {
    this.program = program(gl, vertex, fragment);
    this.uniforms = Object.fromEntries(
      ["viewProjection", "heights", "flags", "size", "origin", "sheet", "light"].map((name) => [
        name,
        gl.getUniformLocation(this.program, name),
      ]),
    );
    const across = Math.max(terrain.columns - 1, 0);
    const down = Math.max(terrain.rows - 1, 0);
    this.heights = floatTexture(gl, terrain.columns, terrain.rows, terrain.ground);
    this.flags = byteTexture(gl, across, down, 1, terrain.tiles);
    this.vao = gl.createVertexArray();
    gl.bindVertexArray(this.vao);
    this.corners = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, this.corners);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 1, 1]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    gl.bindVertexArray(null);

    // Each texture's tiles: the tile's number and, packed, its piece and whether it is the tile's first layer.
    const lists = textures.map(() => [] as number[]);
    for (let tile = 0; tile < across * down; tile++) {
      if (((terrain.tiles[tile] ?? 0) & 1) === 0) continue; // under a cliff model
      for (let k = 0; k < 4; k++) {
        const texture = terrain.layers[tile * 8 + k * 2] ?? 255;
        if (texture === 255) break;
        const piece = terrain.layers[tile * 8 + k * 2 + 1] ?? 0;
        lists[texture]?.push(tile, piece | (k === 0 ? 256 : 0));
      }
    }
    const grey = plainArray(gl, [128, 128, 128, 255]);
    this.passes = textures.map((texture, index) => {
      const instances = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, instances);
      gl.bufferData(gl.ARRAY_BUFFER, new Uint32Array(lists[index] ?? []), gl.STATIC_DRAW);
      return { texture, sheet: grey, instances, count: (lists[index]?.length ?? 0) / 2 };
    });
  }

  /** Gives texture `index` its sheet once loaded. */
  setSheet(index: number, sheet: WebGLTexture): void {
    const pass = this.passes[index];
    if (pass) pass.sheet = sheet;
  }

  /** The sheets the passes are waiting for, by texture number. */
  get wanted(): TerrainTexture[] {
    return this.passes.map((pass) => pass.texture);
  }

  draw(viewProjection: mat4, light: [number, number, number]): void {
    const { gl, terrain } = this;
    gl.useProgram(this.program);
    gl.bindVertexArray(this.vao);
    gl.uniformMatrix4fv(this.uniforms.viewProjection ?? null, false, viewProjection);
    gl.uniform2i(this.uniforms.size ?? null, terrain.columns, terrain.rows);
    gl.uniform2f(this.uniforms.origin ?? null, terrain.left, terrain.bottom);
    gl.uniform3fv(this.uniforms.light ?? null, light);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.heights);
    gl.uniform1i(this.uniforms.heights ?? null, 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.flags);
    gl.uniform1i(this.uniforms.flags ?? null, 1);
    gl.uniform1i(this.uniforms.sheet ?? null, 2);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthFunc(gl.LEQUAL);
    for (const pass of this.passes) {
      if (pass.count === 0) continue;
      gl.activeTexture(gl.TEXTURE2);
      gl.bindTexture(gl.TEXTURE_2D_ARRAY, pass.sheet);
      gl.bindBuffer(gl.ARRAY_BUFFER, pass.instances);
      gl.enableVertexAttribArray(1);
      gl.vertexAttribIPointer(1, 1, gl.UNSIGNED_INT, 8, 0);
      gl.vertexAttribDivisor(1, 1);
      gl.enableVertexAttribArray(2);
      gl.vertexAttribIPointer(2, 1, gl.UNSIGNED_INT, 8, 4);
      gl.vertexAttribDivisor(2, 1);
      gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, pass.count);
    }
    gl.bindVertexArray(null);
  }

  dispose(): void {
    const { gl } = this;
    for (const pass of this.passes) {
      gl.deleteBuffer(pass.instances);
      gl.deleteTexture(pass.sheet);
    }
    gl.deleteBuffer(this.corners);
    gl.deleteTexture(this.heights);
    gl.deleteTexture(this.flags);
    gl.deleteVertexArray(this.vao);
    gl.deleteProgram(this.program);
  }
}
