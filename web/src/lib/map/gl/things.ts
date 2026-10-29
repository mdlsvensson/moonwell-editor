/**
 * Draws models: each batch's copies in one instanced draw a geoset, the solid and alpha-tested geosets first, the
 * blended ones after with the depth left alone. A cliff's vertices stand on the ground beneath them: each is raised by
 * the height (without cliff levels) of the corner at the bottom left of the tile it falls in (*claim*, HiveWE). Models
 * load in the background, nearest the camera first; a model that is missing, cannot be read or draws nothing is told
 * to `failed`, and its things become markers.
 */
import type { mat4 } from "gl-matrix";
import { loadModel, type MapGeoset, type MapModel, ownGeosets, texturePaths } from "../models.ts";
import type { Terrain } from "../scene.ts";
import { type Batch, instanceFloats } from "./batches.ts";
import { floatTexture, plainTexture, program } from "./program.ts";
import { makeTexture } from "./textures.ts";

const vertex = `#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
layout(location = 0) in vec3 position;
layout(location = 1) in vec3 normal;
layout(location = 2) in vec2 uv;
layout(location = 3) in vec4 m0;
layout(location = 4) in vec4 m1;
layout(location = 5) in vec4 m2;
layout(location = 6) in vec4 m3;
layout(location = 7) in vec3 tint;
layout(location = 8) in vec3 team;
layout(location = 9) in float number;
uniform mat4 viewProjection;
uniform bool cliff;
uniform sampler2D relief;
uniform ivec2 size;
uniform vec2 origin;
out vec2 vUv;
out vec3 vNormal;
out vec3 vTint;
out vec3 vTeam;
flat out float vNumber;
void main() {
  mat4 m = mat4(m0, m1, m2, m3);
  vec4 world = m * vec4(position, 1.0);
  if (cliff) {
    ivec2 c = clamp(ivec2(floor((world.xy - origin) / 128.0)), ivec2(0), size - 1);
    world.z += texelFetch(relief, c, 0).r;
  }
  vUv = uv;
  vNormal = mat3(m) * normal;
  vTint = tint;
  vTeam = team;
  vNumber = number;
  gl_Position = viewProjection * world;
}`;

const fragment = `#version 300 es
precision highp float;
in vec2 vUv;
in vec3 vNormal;
in vec3 vTint;
in vec3 vTeam;
flat in float vNumber;
uniform sampler2D diffuse;
uniform int filterMode;
uniform bool teamUnder;
uniform bool picking;
uniform vec3 light;
out vec4 color;
void main() {
  vec4 texel = texture(diffuse, vUv);
  if (filterMode == 1 && texel.a < 0.75) discard;
  if (picking) {
    if (filterMode == 2 && texel.a < 0.5) discard;
    uint n = uint(vNumber + 0.5);
    color = vec4(float(n & 255u), float((n >> 8) & 255u), float((n >> 16) & 255u), 255.0) / 255.0;
    return;
  }
  vec3 rgb = teamUnder ? mix(vTeam, texel.rgb, texel.a) : texel.rgb;
  float shade = clamp(dot(normalize(vNormal), light) * 0.4 + 0.7, 0.35, 1.2);
  color = vec4(rgb * vTint * shade, filterMode == 2 ? texel.a : 1.0);
}`;

/** A geoset on the card: its vertex array (the model's buffers and the batch's copies), and how it draws. */
type Drawn = {
  vao: WebGLVertexArrayObject;
  count: number;
  texture: WebGLTexture;
  filter: number;
  teamUnder: boolean;
};

type Loaded = { batch: Batch; geosets: Drawn[] };

export class ModelLayer {
  private readonly program: WebGLProgram;
  private readonly relief: WebGLTexture;
  private readonly white: WebGLTexture;
  private readonly uniforms: Record<string, WebGLUniformLocation | null>;
  private readonly instances = new Map<Batch, WebGLBuffer>();
  private readonly loaded: Loaded[] = [];
  private readonly models = new Map<
    string,
    Promise<{ model: MapModel; buffers: WebGLBuffer[][] } | null>
  >();
  private readonly textures = new Map<string, Promise<WebGLTexture | null>>();
  private readonly owned: WebGLBuffer[] = [];
  private stopped = false;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly terrain: Terrain,
    private readonly batches: Batch[],
    /** The asset host's URL of a game or map file. */
    private readonly url: (path: string) => string,
    private readonly changed: () => void,
    private readonly failed: (batch: Batch) => void,
  ) {
    this.program = program(gl, vertex, fragment);
    this.uniforms = Object.fromEntries(
      [
        "viewProjection",
        "cliff",
        "relief",
        "size",
        "origin",
        "diffuse",
        "filterMode",
        "teamUnder",
        "picking",
        "light",
      ].map((name) => [name, gl.getUniformLocation(this.program, name)]),
    );
    this.relief = floatTexture(gl, terrain.columns, terrain.rows, terrain.relief);
    this.white = plainTexture(gl, [255, 255, 255, 255]);
    for (const batch of batches) {
      const buffer = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      gl.bufferData(gl.ARRAY_BUFFER, batch.instances, gl.STATIC_DRAW);
      this.instances.set(batch, buffer);
    }
  }

  /** Loads the batches' models, those nearest `near` first, a few at a time; resolves when all are tried. */
  async load(near: [number, number]): Promise<void> {
    const distance = (batch: Batch) =>
      Math.hypot((batch.instances[12] ?? 0) - near[0], (batch.instances[13] ?? 0) - near[1]);
    const queue = [...this.batches].sort((a, b) => distance(a) - distance(b));
    const worker = async () => {
      for (let batch = queue.shift(); batch; batch = queue.shift()) {
        if (this.stopped) return;
        await this.loadBatch(batch);
      }
    };
    await Promise.all(Array.from({ length: 6 }, worker));
  }

  draw(
    viewProjection: mat4,
    light: [number, number, number],
    picking = false,
    shown: (batch: Batch) => boolean = () => true,
  ): void {
    const { gl, terrain } = this;
    gl.useProgram(this.program);
    gl.uniformMatrix4fv(this.uniforms.viewProjection ?? null, false, viewProjection);
    gl.uniform2i(this.uniforms.size ?? null, terrain.columns, terrain.rows);
    gl.uniform2f(this.uniforms.origin ?? null, terrain.left, terrain.bottom);
    gl.uniform3fv(this.uniforms.light ?? null, light);
    gl.uniform1i(this.uniforms.picking ?? null, picking ? 1 : 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.relief);
    gl.uniform1i(this.uniforms.relief ?? null, 1);
    gl.uniform1i(this.uniforms.diffuse ?? null, 0);
    gl.disable(gl.CULL_FACE);
    // Solid and alpha-tested first; blended after, over them, without writing depth (not when picking).
    for (const blended of [false, true]) {
      if (blended && picking) break;
      if (blended) {
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
        gl.depthMask(false);
      } else {
        gl.disable(gl.BLEND);
      }
      for (const { batch, geosets } of this.loaded) {
        if (!shown(batch)) continue;
        gl.uniform1i(this.uniforms.cliff ?? null, batch.cliff ? 1 : 0);
        for (const geoset of geosets) {
          if ((geoset.filter === 2) !== blended && !(picking && geoset.filter === 2)) continue;
          gl.activeTexture(gl.TEXTURE0);
          gl.bindTexture(gl.TEXTURE_2D, geoset.texture);
          gl.uniform1i(this.uniforms.filterMode ?? null, geoset.filter);
          gl.uniform1i(this.uniforms.teamUnder ?? null, geoset.teamUnder ? 1 : 0);
          gl.bindVertexArray(geoset.vao);
          gl.drawElementsInstanced(gl.TRIANGLES, geoset.count, gl.UNSIGNED_SHORT, 0, batch.count);
        }
      }
      gl.depthMask(true);
    }
    gl.bindVertexArray(null);
  }

  dispose(): void {
    const { gl } = this;
    this.stopped = true;
    for (const { geosets } of this.loaded)
      for (const geoset of geosets) gl.deleteVertexArray(geoset.vao);
    for (const buffer of [...this.instances.values(), ...this.owned]) gl.deleteBuffer(buffer);
    for (const texture of this.textures.values())
      void texture.then((made) => made && gl.deleteTexture(made));
    gl.deleteTexture(this.relief);
    gl.deleteTexture(this.white);
    gl.deleteProgram(this.program);
  }

  private async loadBatch(batch: Batch): Promise<void> {
    const model = await this.model(batch.model);
    if (this.stopped) return;
    // A model of several shapes draws the batch's variation's geosets, and those of every variation; a thing whose
    // shape draws nothing is a marker, as one whose model draws nothing.
    const own = model ? ownGeosets(model.model, batch.variation) : [];
    if (!model || own.length === 0) {
      this.failed(batch);
      return;
    }
    const geosets = await Promise.all(
      own.map(async (geoset) => {
        const texture = await this.texture(geoset, batch);
        return {
          geoset,
          texture,
          buffers: model.buffers[model.model.geosets.indexOf(geoset)] ?? [],
        };
      }),
    );
    if (this.stopped) return;
    const { gl } = this;
    const instances = this.instances.get(batch) ?? null;
    this.loaded.push({
      batch,
      geosets: geosets.map(({ geoset, texture, buffers }) => {
        const vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        const [positions, normals, uvs, indices] = buffers;
        for (const [location, buffer, size] of [
          [0, positions, 3],
          [1, normals, 3],
          [2, uvs, 2],
        ] as const) {
          gl.bindBuffer(gl.ARRAY_BUFFER, buffer ?? null);
          gl.enableVertexAttribArray(location);
          gl.vertexAttribPointer(location, size, gl.FLOAT, false, 0, 0);
        }
        gl.bindBuffer(gl.ARRAY_BUFFER, instances);
        const stride = instanceFloats * 4;
        for (let column = 0; column < 4; column++) {
          gl.enableVertexAttribArray(3 + column);
          gl.vertexAttribPointer(3 + column, 4, gl.FLOAT, false, stride, column * 16);
          gl.vertexAttribDivisor(3 + column, 1);
        }
        for (const [location, size, offset] of [
          [7, 3, 16],
          [8, 3, 19],
          [9, 1, 22],
        ] as const) {
          gl.enableVertexAttribArray(location);
          gl.vertexAttribPointer(location, size, gl.FLOAT, false, stride, offset * 4);
          gl.vertexAttribDivisor(location, 1);
        }
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indices ?? null);
        gl.bindVertexArray(null);
        return {
          vao,
          count: geoset.indices.length,
          texture: texture ?? this.white,
          filter: geoset.filter,
          teamUnder: geoset.teamUnder,
        };
      }),
    });
    this.changed();
  }

  // A model's geometry on the card, once a path however many batches draw it.
  private model(path: string) {
    let known = this.models.get(path);
    if (!known) {
      known = loadModel(this.url(path)).then((model) => {
        if (!model || this.stopped) return null;
        const { gl } = this;
        const buffers = model.geosets.map((geoset) =>
          [
            [gl.ARRAY_BUFFER, geoset.positions],
            [gl.ARRAY_BUFFER, geoset.normals],
            [gl.ARRAY_BUFFER, geoset.uvs],
            [gl.ELEMENT_ARRAY_BUFFER, geoset.indices],
          ].map(([target, data]) => {
            const buffer = gl.createBuffer();
            gl.bindBuffer(target as number, buffer);
            gl.bufferData(target as number, data as ArrayBufferView, gl.STATIC_DRAW);
            this.owned.push(buffer);
            return buffer;
          }),
        );
        return { model, buffers };
      });
      this.models.set(path, known);
    }
    return known;
  }

  // A geoset's texture: the first of its files that loads (see texturePaths); null, drawn white, for none.
  private texture(geoset: MapGeoset, batch: Batch): Promise<WebGLTexture | null> {
    const paths = texturePaths(geoset.texture, batch.texture, geoset.hd);
    if (paths.length === 0) return Promise.resolve(null);
    const key = paths.join("|").toLowerCase();
    let known = this.textures.get(key);
    if (!known) {
      known = (async () => {
        for (const path of paths) {
          try {
            const response = await fetch(this.url(path));
            if (!response.ok) continue;
            const data = await response.arrayBuffer();
            const made = this.stopped ? null : makeTexture(this.gl, data);
            if (made) return made;
          } catch {
            // the next file, if any
          }
        }
        return null;
      })();
      this.textures.set(key, known);
    }
    return known;
  }
}
