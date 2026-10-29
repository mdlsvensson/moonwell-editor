/**
 * Classic's unit shadows: each unit's shadow picture (black, its alpha the shadow) laid flat on the ground under it,
 * unturned, the unit at the picture's centre point (see UnitShadow on the app's side); one draw a picture. Flat at the
 * ground's height where the unit stands, a little above it, so it does not dip into the ground on a gentle slope.
 */
import type { mat4 } from "gl-matrix";
import { groundAt, type PlacedThing, type Terrain } from "../scene.ts";
import { program } from "./program.ts";

/** How far above the ground the pictures lie. */
const lift = 2;

/**
 * The units' shadow pictures as triangles, by picture path: five numbers a corner (x, y, z, u, v), six corners a
 * unit; the picture's top row (v 0) at its north edge.
 */
export function shadowQuads(
  things: readonly PlacedThing[],
  terrain: Terrain,
): Map<string, Float32Array> {
  const byPicture = new Map<string, number[]>();
  for (const thing of things) {
    const shadow = thing.shadow;
    if (thing.kind !== "Unit" || !shadow) continue;
    const left = thing.x - shadow.x;
    const bottom = thing.y - shadow.y;
    const right = left + shadow.width;
    const top = bottom + shadow.height;
    const z = groundAt(terrain, thing.x, thing.y) + lift;
    const corners = [
      [left, bottom, 0, 1],
      [right, bottom, 1, 1],
      [right, top, 1, 0],
      [left, bottom, 0, 1],
      [right, top, 1, 0],
      [left, top, 0, 0],
    ];
    const list = byPicture.get(shadow.texture) ?? [];
    for (const [x, y, u, v] of corners) list.push(x ?? 0, y ?? 0, z, u ?? 0, v ?? 0);
    byPicture.set(shadow.texture, list);
  }
  return new Map([...byPicture].map(([path, list]) => [path, Float32Array.from(list)]));
}

const vertex = `#version 300 es
layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;
uniform mat4 viewProjection;
out vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = viewProjection * vec4(position, 1.0);
}`;

const fragment = `#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D picture;
out vec4 color;
void main() {
  color = texture(picture, vUv);
}`;

type Picture = {
  path: string;
  vao: WebGLVertexArrayObject;
  count: number;
  texture: WebGLTexture | null;
};

export class UnitShadowLayer {
  private readonly program: WebGLProgram;
  private readonly pictures: Picture[] = [];
  private readonly buffers: WebGLBuffer[] = [];
  private readonly uniforms: Record<string, WebGLUniformLocation | null>;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    things: readonly PlacedThing[],
    terrain: Terrain,
  ) {
    this.program = program(gl, vertex, fragment);
    this.uniforms = Object.fromEntries(
      ["viewProjection", "picture"].map((name) => [
        name,
        gl.getUniformLocation(this.program, name),
      ]),
    );
    for (const [path, vertices] of shadowQuads(things, terrain)) {
      const vao = gl.createVertexArray();
      gl.bindVertexArray(vao);
      const buffer = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
      gl.enableVertexAttribArray(1);
      gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
      gl.bindVertexArray(null);
      this.buffers.push(buffer);
      this.pictures.push({ path, vao, count: vertices.length / 5, texture: null });
    }
  }

  /** The pictures the layer waits for. */
  get wanted(): string[] {
    return this.pictures.map((picture) => picture.path);
  }

  setPicture(path: string, texture: WebGLTexture): void {
    const picture = this.pictures.find((each) => each.path === path);
    if (picture) picture.texture = texture;
  }

  draw(viewProjection: mat4): void {
    const { gl } = this;
    if (this.pictures.length === 0) return;
    gl.useProgram(this.program);
    gl.uniformMatrix4fv(this.uniforms.viewProjection ?? null, false, viewProjection);
    gl.uniform1i(this.uniforms.picture ?? null, 0);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    for (const picture of this.pictures) {
      if (!picture.texture) continue;
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, picture.texture);
      gl.bindVertexArray(picture.vao);
      gl.drawArrays(gl.TRIANGLES, 0, picture.count);
    }
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(null);
  }

  dispose(): void {
    const { gl } = this;
    for (const picture of this.pictures) {
      gl.deleteVertexArray(picture.vao);
      if (picture.texture) gl.deleteTexture(picture.texture);
    }
    for (const buffer of this.buffers) gl.deleteBuffer(buffer);
    gl.deleteProgram(this.program);
  }
}
