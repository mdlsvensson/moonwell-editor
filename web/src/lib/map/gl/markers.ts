/**
 * Markers: a small diamond over each placed thing that has no model to draw (a start location, a type the map no
 * longer has, a model that is missing or draws nothing), in a colour by its kind, so it can still be seen and picked.
 */
import type { mat4 } from "gl-matrix";
import type { PlacedThing } from "../scene.ts";
import { markerColour } from "./batches.ts";
import { program } from "./program.ts";

/** Half the diamond's width and its height above the ground. */
const size = 24;
const lift = 48;

const vertex = `#version 300 es
precision highp float;
layout(location = 0) in vec3 corner;
layout(location = 1) in vec3 place;
layout(location = 2) in vec3 colour;
layout(location = 3) in float number;
uniform mat4 viewProjection;
out vec3 vColour;
flat out float vNumber;
void main() {
  vColour = colour * (0.75 + 0.25 * corner.z / ${size.toFixed(1)});
  vNumber = number;
  gl_Position = viewProjection * vec4(place + vec3(0.0, 0.0, ${lift.toFixed(1)}) + corner, 1.0);
}`;

const fragment = `#version 300 es
precision highp float;
in vec3 vColour;
flat in float vNumber;
uniform bool picking;
out vec4 color;
void main() {
  if (picking) {
    uint n = uint(vNumber + 0.5);
    color = vec4(float(n & 255u), float((n >> 8) & 255u), float((n >> 16) & 255u), 255.0) / 255.0;
    return;
  }
  color = vec4(vColour, 1.0);
}`;

/** An octahedron: six points, eight faces. */
const corners = [
  [size, 0, 0],
  [-size, 0, 0],
  [0, size, 0],
  [0, -size, 0],
  [0, 0, size],
  [0, 0, -size],
];
const faces = [0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5];

export class MarkerLayer {
  private readonly program: WebGLProgram;
  private readonly vao: WebGLVertexArrayObject;
  private readonly buffers: WebGLBuffer[] = [];
  private readonly instances: WebGLBuffer;
  private readonly uniforms: Record<string, WebGLUniformLocation | null>;
  private count = 0;

  constructor(private readonly gl: WebGL2RenderingContext) {
    this.program = program(gl, vertex, fragment);
    this.uniforms = Object.fromEntries(
      ["viewProjection", "picking"].map((name) => [
        name,
        gl.getUniformLocation(this.program, name),
      ]),
    );
    this.vao = gl.createVertexArray();
    gl.bindVertexArray(this.vao);
    const points = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, points);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(corners.flat()), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
    const indices = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indices);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(faces), gl.STATIC_DRAW);
    this.instances = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, this.instances);
    for (const [location, size, offset] of [
      [1, 3, 0],
      [2, 3, 3],
      [3, 1, 6],
    ] as const) {
      gl.enableVertexAttribArray(location);
      gl.vertexAttribPointer(location, size, gl.FLOAT, false, 28, offset * 4);
      gl.vertexAttribDivisor(location, 1);
    }
    gl.bindVertexArray(null);
    this.buffers.push(points, indices, this.instances);
  }

  /** Marks these things, in place of any marked before: each in its kind's colour, or all in `colour`. */
  mark(things: readonly PlacedThing[], colour?: [number, number, number]): void {
    const floats = new Float32Array(things.length * 7);
    things.forEach((thing, index) => {
      floats.set(
        [thing.x, thing.y, thing.z, ...(colour ?? markerColour(thing.kind)), thing.number],
        index * 7,
      );
    });
    this.gl.bindBuffer(this.gl.ARRAY_BUFFER, this.instances);
    this.gl.bufferData(this.gl.ARRAY_BUFFER, floats, this.gl.DYNAMIC_DRAW);
    this.count = things.length;
  }

  draw(viewProjection: mat4, picking = false): void {
    if (this.count === 0) return;
    const { gl } = this;
    gl.useProgram(this.program);
    gl.uniformMatrix4fv(this.uniforms.viewProjection ?? null, false, viewProjection);
    gl.uniform1i(this.uniforms.picking ?? null, picking ? 1 : 0);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(this.vao);
    gl.drawElementsInstanced(gl.TRIANGLES, faces.length, gl.UNSIGNED_SHORT, 0, this.count);
    gl.bindVertexArray(null);
  }

  dispose(): void {
    for (const buffer of this.buffers) this.gl.deleteBuffer(buffer);
    this.gl.deleteVertexArray(this.vao);
    this.gl.deleteProgram(this.program);
  }
}
