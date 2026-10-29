/**
 * Draws a map scene on one WebGL2 context: the ground and the water now, the cliffs and placed things later. Textures
 * load in the background; each that arrives asks for the scene to be drawn again.
 */
import { mat4 } from "gl-matrix";
import { matrices, type View } from "../camera.ts";
import type { MapScene, Terrain } from "../scene.ts";
import { GroundLayer } from "./ground.ts";
import { ddsInfo, makeTexture } from "./textures.ts";
import { WaterLayer } from "./water.ts";

export class MapRenderer {
  private readonly ground: GroundLayer;
  private readonly water: WaterLayer;
  /** Each ground texture's piece size in texels, once its sheet is loaded. */
  private readonly texels: number[] = [];
  private stopped = false;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    readonly scene: MapScene,
    readonly terrain: Terrain,
    /** The asset host's URL of a game or map file. */
    private readonly url: (path: string) => string,
    /** Asks for the scene to be drawn again (a texture arrived). */
    private readonly changed: () => void,
    /** Told once every ground texture has arrived (or failed), with how long they took. */
    private readonly loaded: (milliseconds: number) => void = () => {},
  ) {
    gl.enable(gl.DEPTH_TEST);
    gl.clearColor(0.05, 0.05, 0.07, 1);
    this.ground = new GroundLayer(gl, terrain, scene.textures);
    this.water = new WaterLayer(gl, terrain, scene.water);
    void this.load();
  }

  /** Draws the scene as `view` sees it on a canvas of `width` by `height` pixels. */
  draw(view: View, width: number, height: number): void {
    const { gl } = this;
    gl.viewport(0, 0, width, height);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    const { view: look, projection } = matrices(view, width, height);
    const viewProjection = mat4.multiply(mat4.create(), projection, look);
    this.ground.draw(viewProjection, (index) => this.texels[index] ?? 64);
    this.water.draw(viewProjection);
  }

  dispose(): void {
    this.stopped = true;
    this.ground.dispose();
    this.water.dispose();
  }

  private async load(): Promise<void> {
    const started = performance.now();
    await Promise.all([
      ...this.ground.wanted.map(async (texture, index) => {
        const data = await this.bytes(texture.path);
        if (!data || this.stopped) return;
        const sheet = makeTexture(this.gl, data);
        if (!sheet) return;
        this.texels[index] = (ddsInfo(new Uint8Array(data))?.height ?? 256) / 4;
        this.ground.setSheet(index, sheet);
        this.changed();
      }),
      (async () => {
        const data = await this.bytes(this.water.framePath);
        const frame = data && !this.stopped ? makeTexture(this.gl, data) : null;
        if (frame) {
          this.water.setFrame(frame);
          this.changed();
        }
      })(),
    ]);
    if (!this.stopped) this.loaded(performance.now() - started);
  }

  private async bytes(path: string): Promise<ArrayBuffer | null> {
    if (!path) return null;
    try {
      const response = await fetch(this.url(path));
      return response.ok ? await response.arrayBuffer() : null;
    } catch {
      return null;
    }
  }
}
