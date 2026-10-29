/**
 * Draws a map scene on one WebGL2 context: the ground, the cliffs, the placed things and their markers, then the water
 * over them and the blended parts of models last. Textures and models load in the background, nearest the camera
 * first; each that arrives asks for the scene to be drawn again.
 */
import { mat4 } from "gl-matrix";
import { matrices, type View } from "../camera.ts";
import type { MapScene, PlacedThing, Terrain } from "../scene.ts";
import { type Batch, cliffBatches, thingBatches } from "./batches.ts";
import { GroundLayer } from "./ground.ts";
import { MarkerLayer } from "./markers.ts";
import { ddsInfo, makeTexture } from "./textures.ts";
import { ModelLayer } from "./things.ts";
import { WaterLayer } from "./water.ts";

/** Where the light comes from: high in the south-west (plan's call; the tileset's own light is not drawn yet). */
const light: [number, number, number] = [-0.3, -0.35, 0.89];

export class MapRenderer {
  private readonly ground: GroundLayer;
  private readonly water: WaterLayer;
  private readonly cliffs: ModelLayer;
  private readonly things: ModelLayer;
  private readonly markers: MarkerLayer;
  /** The things drawn as markers: those without a model, and those whose model draws nothing. */
  private readonly marked: PlacedThing[];
  /** Each ground texture's piece size in texels, once its sheet is loaded. */
  private readonly texels: number[] = [];
  private stopped = false;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    readonly scene: MapScene,
    readonly terrain: Terrain,
    /** The asset host's URL of a game or map file. */
    private readonly url: (path: string) => string,
    /** Asks for the scene to be drawn again (a texture or model arrived). */
    private readonly changed: () => void,
    /** Told when the ground's textures, and then the models, have all arrived (or failed), with how long they took. */
    private readonly loaded: (what: "ground" | "models", milliseconds: number) => void = () => {},
    /** Where the camera starts: the models nearest it load first. */
    near: [number, number] = [0, 0],
  ) {
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.clearColor(0.05, 0.05, 0.07, 1);
    this.ground = new GroundLayer(gl, terrain, scene.textures);
    this.water = new WaterLayer(gl, terrain, scene.water);
    this.marked = scene.things.filter((thing) => !thing.model);
    this.markers = new MarkerLayer(gl);
    this.markers.mark(this.marked);
    const failed = (batch: Batch) => {
      const numbers = new Set(batch.numbers);
      this.marked.push(...scene.things.filter((thing) => numbers.has(thing.number)));
      this.markers.mark(this.marked);
      changed();
    };
    this.cliffs = new ModelLayer(gl, terrain, cliffBatches(scene), url, changed, () => {});
    this.things = new ModelLayer(gl, terrain, thingBatches(scene.things), url, changed, failed);
    void this.load(near);
  }

  /** Draws the scene as `view` sees it on a canvas of `width` by `height` pixels. */
  draw(view: View, width: number, height: number): void {
    const { gl } = this;
    gl.viewport(0, 0, width, height);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    const { view: look, projection } = matrices(view, width, height);
    const viewProjection = mat4.multiply(mat4.create(), projection, look);
    this.ground.draw(viewProjection, (index) => this.texels[index] ?? 64, light);
    this.cliffs.draw(viewProjection, light);
    this.things.draw(viewProjection, light);
    this.markers.draw(viewProjection);
    this.water.draw(viewProjection);
  }

  dispose(): void {
    this.stopped = true;
    this.ground.dispose();
    this.water.dispose();
    this.cliffs.dispose();
    this.things.dispose();
    this.markers.dispose();
  }

  private async load(near: [number, number]): Promise<void> {
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
    if (this.stopped) return;
    this.loaded("ground", performance.now() - started);
    await Promise.all([this.cliffs.load(near), this.things.load(near)]);
    if (!this.stopped) this.loaded("models", performance.now() - started);
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
