/**
 * The water: a sheet over every tile with water at any of its corners, at the water's surface (a corner without water
 * takes the highest surface of the tile's others), coloured by its depth over the ground between the tileset's shallow
 * and deep colours (*claim*, HiveWE: shallow from 10 to 64 deep, deep from 64 to 72), and drawn in the first frame of
 * the water texture.
 */
import type { mat4 } from "gl-matrix";
import type { Terrain, WaterLook } from "../scene.ts";
import { argb, floatTexture, plainTexture, program } from "./program.ts";

/** What the shader reads for a corner without water. */
const none = -1e9;

const vertex = `#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
layout(location = 0) in vec2 corner;
layout(location = 1) in uint tile;
uniform mat4 viewProjection;
uniform sampler2D surface;
uniform sampler2D heights;
uniform ivec2 size;
uniform vec2 origin;
uniform vec4 shallowMin;
uniform vec4 shallowMax;
uniform vec4 deepMin;
uniform vec4 deepMax;
out vec2 uv;
out vec4 tint;
void main() {
  int across = size.x - 1;
  ivec2 at = ivec2(int(tile) % across, int(tile) / across);
  ivec2 c = at + ivec2(corner);
  float z = texelFetch(surface, c, 0).r;
  if (z < -1e8) {
    z = max(max(texelFetch(surface, at, 0).r, texelFetch(surface, at + ivec2(1, 0), 0).r),
      max(texelFetch(surface, at + ivec2(0, 1), 0).r, texelFetch(surface, at + ivec2(1, 1), 0).r));
  }
  float depth = clamp(z - texelFetch(heights, c, 0).r, 0.0, 128.0);
  if (depth <= 64.0) {
    tint = mix(shallowMin, shallowMax, max(depth - 10.0, 0.0) / 54.0);
  } else {
    tint = mix(deepMin, deepMax, clamp(depth - 64.0, 0.0, 8.0) / 8.0);
  }
  uv = vec2(corner.x, 1.0 - corner.y);
  gl_Position = viewProjection * vec4(origin + vec2(c) * 128.0, z, 1.0);
}`;

const fragment = `#version 300 es
precision highp float;
in vec2 uv;
in vec4 tint;
uniform sampler2D frame;
out vec4 color;
void main() {
  color = texture(frame, uv) * tint;
}`;

export class WaterLayer {
  private readonly program: WebGLProgram;
  private readonly vao: WebGLVertexArrayObject;
  private readonly buffers: WebGLBuffer[] = [];
  private readonly surface: WebGLTexture;
  private readonly heights: WebGLTexture;
  private readonly count: number;
  private readonly uniforms: Record<string, WebGLUniformLocation | null>;
  private frame: WebGLTexture;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly terrain: Terrain,
    private readonly look: WaterLook,
  ) {
    this.program = program(gl, vertex, fragment);
    this.uniforms = Object.fromEntries(
      [
        "viewProjection",
        "surface",
        "heights",
        "size",
        "origin",
        "shallowMin",
        "shallowMax",
        "deepMin",
        "deepMax",
        "frame",
      ].map((name) => [name, gl.getUniformLocation(this.program, name)]),
    );
    const { columns, rows } = terrain;
    const surface = terrain.water.map((z) => (Number.isNaN(z) ? none : z));
    this.surface = floatTexture(gl, columns, rows, surface);
    this.heights = floatTexture(gl, columns, rows, terrain.ground);
    this.frame = plainTexture(gl, [255, 255, 255, 255]);

    const tiles: number[] = [];
    for (let row = 0; row < rows - 1; row++) {
      for (let column = 0; column < columns - 1; column++) {
        const at = row * columns + column;
        const wet = [at, at + 1, at + columns, at + columns + 1].some(
          (corner) => !Number.isNaN(terrain.water[corner] ?? Number.NaN),
        );
        if (wet) tiles.push(row * (columns - 1) + column);
      }
    }
    this.count = tiles.length;

    this.vao = gl.createVertexArray();
    gl.bindVertexArray(this.vao);
    const corners = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, corners);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 1, 1]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    const instances = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, instances);
    gl.bufferData(gl.ARRAY_BUFFER, new Uint32Array(tiles), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribIPointer(1, 1, gl.UNSIGNED_INT, 4, 0);
    gl.vertexAttribDivisor(1, 1);
    gl.bindVertexArray(null);
    this.buffers.push(corners, instances);
  }

  /** The path of the water texture's first frame (Water00). */
  get framePath(): string {
    return `${this.look.texture}00.blp`;
  }

  setFrame(frame: WebGLTexture): void {
    this.gl.deleteTexture(this.frame);
    this.frame = frame;
  }

  draw(viewProjection: mat4): void {
    if (this.count === 0) return;
    const { gl, terrain, look } = this;
    gl.useProgram(this.program);
    gl.bindVertexArray(this.vao);
    gl.uniformMatrix4fv(this.uniforms.viewProjection ?? null, false, viewProjection);
    gl.uniform2i(this.uniforms.size ?? null, terrain.columns, terrain.rows);
    gl.uniform2f(this.uniforms.origin ?? null, terrain.left, terrain.bottom);
    gl.uniform4fv(this.uniforms.shallowMin ?? null, argb(look.shallowMin));
    gl.uniform4fv(this.uniforms.shallowMax ?? null, argb(look.shallowMax));
    gl.uniform4fv(this.uniforms.deepMin ?? null, argb(look.deepMin));
    gl.uniform4fv(this.uniforms.deepMax ?? null, argb(look.deepMax));
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.surface);
    gl.uniform1i(this.uniforms.surface ?? null, 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.heights);
    gl.uniform1i(this.uniforms.heights ?? null, 1);
    gl.activeTexture(gl.TEXTURE2);
    gl.bindTexture(gl.TEXTURE_2D, this.frame);
    gl.uniform1i(this.uniforms.frame ?? null, 2);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, this.count);
    gl.depthMask(true);
    gl.bindVertexArray(null);
  }

  dispose(): void {
    const { gl } = this;
    for (const buffer of this.buffers) gl.deleteBuffer(buffer);
    gl.deleteTexture(this.surface);
    gl.deleteTexture(this.heights);
    gl.deleteTexture(this.frame);
    gl.deleteVertexArray(this.vao);
    gl.deleteProgram(this.program);
  }
}
