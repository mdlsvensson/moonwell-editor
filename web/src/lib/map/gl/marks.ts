/**
 * Regions and cameras as lines on the map: a region's edges in its colour, laid on the ground; a camera as a cross on
 * the point it looks at and a line toward where it stands (its rotation and angle of attack, in degrees, as the game
 * keeps them). The selected one is drawn white.
 */
import type { mat4 } from "gl-matrix";
import { type CameraMark, groundAt, type RegionMark, type Terrain } from "../scene.ts";
import { program } from "./program.ts";

const vertex = `#version 300 es
precision highp float;
layout(location = 0) in vec3 position;
layout(location = 1) in vec3 colour;
uniform mat4 viewProjection;
out vec3 vColour;
void main() {
  vColour = colour;
  gl_Position = viewProjection * vec4(position, 1.0);
}`;

const fragment = `#version 300 es
precision highp float;
in vec3 vColour;
out vec4 color;
void main() {
  color = vec4(vColour, 1.0);
}`;

/** How far apart a region's edge follows the ground, and how high above it. */
const step = 32;
const lift = 6;

export class MarksLayer {
  private readonly program: WebGLProgram;
  private readonly vao: WebGLVertexArrayObject;
  private readonly buffer: WebGLBuffer;
  private readonly viewProjection: WebGLUniformLocation | null;
  private regionCount = 0;
  private cameraCount = 0;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly terrain: Terrain,
    private readonly regions: readonly RegionMark[],
    private readonly cameras: readonly CameraMark[],
  ) {
    this.program = program(gl, vertex, fragment);
    this.viewProjection = gl.getUniformLocation(this.program, "viewProjection");
    this.vao = gl.createVertexArray();
    this.buffer = gl.createBuffer();
    gl.bindVertexArray(this.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffer);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 24, 0);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 24, 12);
    gl.bindVertexArray(null);
    this.select(null);
  }

  /** Draws the mark whose trigger name is `ref` white, the others in their own colours. */
  select(ref: string | null): void {
    const lines: number[] = [];
    const push = (x: number, y: number, z: number, colour: [number, number, number]) =>
      lines.push(x, y, z, ...colour);
    const on = (x: number, y: number) => groundAt(this.terrain, x, y) + lift;
    for (const region of this.regions) {
      const colour: [number, number, number] =
        region.ref === ref
          ? [1, 1, 1]
          : [
              ((region.color >>> 16) & 255) / 255,
              ((region.color >>> 8) & 255) / 255,
              (region.color & 255) / 255,
            ];
      const corners: [number, number][] = [
        [region.left, region.bottom],
        [region.right, region.bottom],
        [region.right, region.top],
        [region.left, region.top],
      ];
      corners.forEach(([x0, y0], index) => {
        const [x1, y1] = corners[(index + 1) % 4] ?? [x0, y0];
        const steps = Math.max(1, Math.ceil(Math.hypot(x1 - x0, y1 - y0) / step));
        for (let i = 0; i < steps; i++) {
          const a = i / steps;
          const b = (i + 1) / steps;
          const xa = x0 + (x1 - x0) * a;
          const ya = y0 + (y1 - y0) * a;
          const xb = x0 + (x1 - x0) * b;
          const yb = y0 + (y1 - y0) * b;
          push(xa, ya, on(xa, ya), colour);
          push(xb, yb, on(xb, yb), colour);
        }
      });
    }
    this.regionCount = lines.length / 6;
    for (const camera of this.cameras) {
      const colour: [number, number, number] = camera.ref === ref ? [1, 1, 1] : [0.95, 0.85, 0.3];
      const z = on(camera.x, camera.y) + camera.z;
      const size = 48;
      push(camera.x - size, camera.y, z, colour);
      push(camera.x + size, camera.y, z, colour);
      push(camera.x, camera.y - size, z, colour);
      push(camera.x, camera.y + size, z, colour);
      const yaw = (camera.rotation * Math.PI) / 180;
      const pitch = (camera.angleOfAttack * Math.PI) / 180;
      // The eye stands back from the target along the camera's view: the way it looks, reversed.
      const back = Math.min(camera.distance, 2000);
      push(camera.x, camera.y, z, colour);
      push(
        camera.x - Math.cos(yaw) * Math.cos(pitch) * back,
        camera.y - Math.sin(yaw) * Math.cos(pitch) * back,
        z - Math.sin(pitch) * back,
        colour,
      );
    }
    this.cameraCount = lines.length / 6 - this.regionCount;
    const { gl } = this;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(lines), gl.DYNAMIC_DRAW);
  }

  draw(viewProjection: mat4, regions: boolean, cameras: boolean): void {
    const { gl } = this;
    if ((!regions || this.regionCount === 0) && (!cameras || this.cameraCount === 0)) return;
    gl.useProgram(this.program);
    gl.uniformMatrix4fv(this.viewProjection, false, viewProjection);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(this.vao);
    if (regions) gl.drawArrays(gl.LINES, 0, this.regionCount);
    if (cameras) gl.drawArrays(gl.LINES, this.regionCount, this.cameraCount);
    gl.bindVertexArray(null);
  }

  dispose(): void {
    this.gl.deleteBuffer(this.buffer);
    this.gl.deleteVertexArray(this.vao);
    this.gl.deleteProgram(this.program);
  }
}
