/**
 * The water: a sheet over every tile with water at any of its corners, at the water's surface (a corner without water
 * takes the highest surface of the tile's others), coloured by its depth over the ground between the tileset's shallow
 * and deep colours (*claim*, HiveWE: shallow from 10 to 64 deep, deep from 64 to 72). Classic plays the water
 * texture's frames at the tileset's rate, one picture over its cells of tiles. HD and Definitive Edition have no such picture of their water (the view's own
 * look, by eye to World Editor's): the deep colour lit by the sky, the sky reflected the more the slanter it is seen,
 * the sun's glint, rippled by the game's water normal map drifting two ways, and more opaque.
 */
import type { mat4, vec3 } from "gl-matrix";
import type { Terrain, WaterLook } from "../scene.ts";
import { type Lighting, lightingUniforms, litGlsl, setLighting } from "./lighting.ts";
import { argb, floatTexture, plainTexture, program } from "./program.ts";

/** The game's water normal map (HD and Definitive Edition), two channels. */
export const ripplesPath = "ReplaceableTextures\\Water\\Water_Normal.blp";

/**
 * The water texture's frames the view fetches, Water00 first: Classic's all, for water it plays; none for a map
 * without water, or in HD and Definitive Edition (`pbr`), whose water draws no frame.
 */
export function waterFramePaths(look: WaterLook, wet: boolean, pbr: boolean): string[] {
  if (!wet || pbr) return [];
  return Array.from(
    { length: Math.max(look.frames, 1) },
    (_, index) => `${look.texture}${String(index).padStart(2, "0")}.blp`,
  );
}

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
uniform float cells;
out vec2 uv;
out vec4 tint;
out vec3 world;
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
  world = vec3(origin + vec2(c) * 128.0, z);
  uv = vec2(world.x, -world.y) / (128.0 * cells);
  gl_Position = viewProjection * vec4(world, 1.0);
}`;

const fragment = `#version 300 es
precision highp float;
in vec2 uv;
in vec4 tint;
in vec3 world;
uniform sampler2D frame;
uniform sampler2D ripples;
uniform vec3 eye;
uniform float time;
out vec4 color;
${litGlsl}
void main() {
  if (!pbr) {
    color = texture(frame, uv) * tint;
    return;
  }
  vec2 drift = vec2(time * 0.02, time * 0.013);
  vec2 slope = (texture(ripples, world.xy / 384.0 + drift).rg + texture(ripples, world.xy / 512.0 - drift.yx).rg) - 1.0;
  vec3 n = normalize(vec3(slope * 1.2, 1.0));
  vec3 v = normalize(eye - world);
  vec3 r = reflect(-v, n);
  float fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(n, v), 0.0), 5.0);
  float facing = max(dot(n, lightToward), 0.0) * sunlit(world);
  vec3 body = pow(tint.rgb, vec3(2.2)) * (skyAt(vec3(0.0, 0.0, 1.0)) * 0.6 + lightColour * facing * 1.4) * 0.1;
  vec3 glint = lightColour * pow(max(dot(r, lightToward), 0.0), 200.0) * 0.25;
  vec3 rgb = mix(body, skyAt(r) * 0.6, fresnel) + glint;
  color = vec4(pow(rgb, vec3(1.0 / 2.2)), clamp(0.3 + tint.a, 0.0, 0.93));
}`;

export class WaterLayer {
  private readonly program: WebGLProgram;
  private readonly vao: WebGLVertexArrayObject;
  private readonly buffers: WebGLBuffer[] = [];
  private readonly surface: WebGLTexture;
  private readonly heights: WebGLTexture;
  private readonly count: number;
  private readonly uniforms: Record<string, WebGLUniformLocation | null>;
  /** The water texture's frames, as they load; the plain white stands in for one not loaded. */
  private readonly frames: (WebGLTexture | null)[];
  private readonly white: WebGLTexture;
  private ripples: WebGLTexture;
  /** The clock the water plays by, in seconds; the renderer sets it before each frame. */
  time = 0;

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
        "ripples",
        "eye",
        "time",
        "cells",
        ...lightingUniforms,
      ].map((name) => [name, gl.getUniformLocation(this.program, name)]),
    );
    const { columns, rows } = terrain;
    const surface = terrain.water.map((z) => (Number.isNaN(z) ? none : z));
    this.surface = floatTexture(gl, columns, rows, surface);
    this.heights = floatTexture(gl, columns, rows, terrain.ground);
    this.white = plainTexture(gl, [255, 255, 255, 255]);
    this.frames = new Array(Math.max(look.frames, 1)).fill(null);
    this.ripples = plainTexture(gl, [128, 128, 255, 255]);

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

  /** The water texture's frames the view fetches (see waterFramePaths). */
  framePaths(pbr: boolean): string[] {
    return waterFramePaths(this.look, this.count > 0, pbr);
  }

  setFrame(index: number, frame: WebGLTexture): void {
    if (index < 0 || index >= this.frames.length) return;
    const old = this.frames[index];
    if (old) this.gl.deleteTexture(old);
    this.frames[index] = frame;
  }

  /** Whether the water changes from frame to frame (there is water, and it plays frames or ripples). */
  get moving(): boolean {
    return this.count > 0 && this.frames.length > 1;
  }

  // The frame to show now: the one the clock is at, else the nearest loaded before it, else white.
  private frameNow(): WebGLTexture {
    const at = Math.floor(this.time * this.look.rate) % this.frames.length;
    for (let back = 0; back < this.frames.length; back++) {
      const frame = this.frames[(at - back + this.frames.length) % this.frames.length];
      if (frame) return frame;
    }
    return this.white;
  }

  /** The water normal map, once it has loaded (HD and Definitive Edition). */
  setRipples(ripples: WebGLTexture): void {
    this.gl.deleteTexture(this.ripples);
    this.ripples = ripples;
  }

  /** Draws the water lit by the terrain's light, seen from `eye` (the camera's place). */
  draw(viewProjection: mat4, light: Lighting, eye: vec3): void {
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
    gl.uniform3fv(this.uniforms.eye ?? null, eye);
    gl.uniform1f(this.uniforms.time ?? null, this.time);
    gl.uniform1f(this.uniforms.cells ?? null, Math.max(look.cells, 1));
    setLighting(gl, this.uniforms, light);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.surface);
    gl.uniform1i(this.uniforms.surface ?? null, 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.heights);
    gl.uniform1i(this.uniforms.heights ?? null, 1);
    gl.activeTexture(gl.TEXTURE2);
    gl.bindTexture(gl.TEXTURE_2D, this.frameNow());
    gl.uniform1i(this.uniforms.frame ?? null, 2);
    gl.activeTexture(gl.TEXTURE3);
    gl.bindTexture(gl.TEXTURE_2D, this.ripples);
    gl.uniform1i(this.uniforms.ripples ?? null, 3);
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
    for (const frame of this.frames) if (frame) gl.deleteTexture(frame);
    gl.deleteTexture(this.white);
    gl.deleteTexture(this.ripples);
    gl.deleteVertexArray(this.vao);
    gl.deleteProgram(this.program);
  }
}
