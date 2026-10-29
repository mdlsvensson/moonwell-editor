/**
 * Draws a map scene on one WebGL2 context: the ground, the cliffs, the placed things and their markers, then the water
 * over them and the blended parts of models last. Textures and models load in the background, nearest the camera
 * first; each that arrives asks for the scene to be drawn again.
 */
import { mat4 } from "gl-matrix";
import { matrices, type View } from "../camera.ts";
import { colourToNumber } from "../picking.ts";
import type { MapScene, PlacedThing, Terrain, ThingKind } from "../scene.ts";
import { type Batch, cliffBatches, thingBatches } from "./batches.ts";
import { GroundLayer } from "./ground.ts";
import { MarkerLayer } from "./markers.ts";
import { MarksLayer } from "./marks.ts";
import { makeGroundTexture, makeTexture } from "./textures.ts";
import { ModelLayer } from "./things.ts";
import { WaterLayer } from "./water.ts";

/** Where the light comes from: high in the south-west (plan's call; the tileset's own light is not drawn yet). */
const light: [number, number, number] = [-0.3, -0.35, 0.89];

/** Which layers show: the kinds of placed thing, the regions and the cameras. */
export type Layers = { kinds: ReadonlySet<ThingKind>; regions: boolean; cameras: boolean };

/** Every layer shown. */
export const allLayers: Layers = {
  kinds: new Set<ThingKind>(["Unit", "Item", "StartLocation", "Destructable", "Doodad"]),
  regions: true,
  cameras: true,
};

/** The hidden picture the pick pass draws into: a colour a pixel and the depth. */
type PickTarget = {
  frame: WebGLFramebuffer;
  colour: WebGLRenderbuffer;
  depth: WebGLRenderbuffer;
  width: number;
  height: number;
};

export class MapRenderer {
  private readonly ground: GroundLayer;
  private readonly water: WaterLayer;
  private readonly cliffs: ModelLayer;
  private readonly things: ModelLayer;
  private readonly markers: MarkerLayer;
  private readonly highlight: MarkerLayer;
  private readonly marks: MarksLayer;
  /** The things drawn as markers: those without a model, and those whose model draws nothing. */
  private readonly marked: PlacedThing[];
  private layers: Layers = allLayers;
  private pickTarget: PickTarget | null = null;
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
    this.highlight = new MarkerLayer(gl);
    this.marks = new MarksLayer(gl, terrain, scene.regions, scene.cameras);
    const failed = (batch: Batch) => {
      const numbers = new Set(batch.numbers);
      this.marked.push(...scene.things.filter((thing) => numbers.has(thing.number)));
      this.markShown();
      changed();
    };
    this.cliffs = new ModelLayer(gl, terrain, cliffBatches(scene), url, changed, () => {});
    this.things = new ModelLayer(gl, terrain, thingBatches(scene.things), url, changed, failed);
    void this.load(near);
  }

  /** Draws the scene as `view` sees it on a canvas of `width` by `height` pixels. */
  draw(view: View, width: number, height: number): void {
    const { gl } = this;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, width, height);
    gl.clearColor(0.05, 0.05, 0.07, 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    const viewProjection = this.viewProjection(view, width, height);
    this.ground.draw(viewProjection, light);
    this.cliffs.draw(viewProjection, light);
    this.things.draw(viewProjection, light, false, this.shown);
    this.markers.draw(viewProjection);
    this.marks.draw(viewProjection, this.layers.regions, this.layers.cameras);
    this.water.draw(viewProjection);
    gl.disable(gl.DEPTH_TEST);
    this.highlight.draw(viewProjection);
    gl.enable(gl.DEPTH_TEST);
  }

  /**
   * The number of the placed thing drawn at pixel `px`, `py` (from the canvas's top left), 0 for none: the things and
   * markers drawn again into a hidden picture, each in its number's colour, behind the ground and cliffs.
   */
  pickAt(view: View, width: number, height: number, px: number, py: number): number {
    const { gl } = this;
    const target = this.target(width, height);
    gl.bindFramebuffer(gl.FRAMEBUFFER, target.frame);
    gl.viewport(0, 0, width, height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    const viewProjection = this.viewProjection(view, width, height);
    // The ground and cliffs only hide what is behind them.
    gl.colorMask(false, false, false, false);
    this.ground.draw(viewProjection, light);
    this.cliffs.draw(viewProjection, light);
    gl.colorMask(true, true, true, true);
    gl.disable(gl.BLEND);
    this.things.draw(viewProjection, light, true, this.shown);
    this.markers.draw(viewProjection, true);
    const pixel = new Uint8Array(4);
    gl.readPixels(
      Math.round(px),
      Math.round(height - py - 1),
      1,
      1,
      gl.RGBA,
      gl.UNSIGNED_BYTE,
      pixel,
    );
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return colourToNumber(pixel[0] ?? 0, pixel[1] ?? 0, pixel[2] ?? 0, pixel[3] ?? 0);
  }

  /** Shows only these layers. */
  setLayers(layers: Layers): void {
    this.layers = layers;
    this.markShown();
    this.changed();
  }

  /** Draws a white diamond over each of these things (the one under the mouse, the one selected). */
  setHighlight(things: readonly PlacedThing[]): void {
    this.highlight.mark(things, [1, 1, 1]);
    this.changed();
  }

  /** Draws the region or camera whose trigger name is `ref` white. */
  selectMark(ref: string | null): void {
    this.marks.select(ref);
    this.changed();
  }

  dispose(): void {
    this.stopped = true;
    this.ground.dispose();
    this.water.dispose();
    this.cliffs.dispose();
    this.things.dispose();
    this.markers.dispose();
    this.highlight.dispose();
    this.marks.dispose();
    const target = this.pickTarget;
    if (target) {
      this.gl.deleteFramebuffer(target.frame);
      this.gl.deleteRenderbuffer(target.colour);
      this.gl.deleteRenderbuffer(target.depth);
    }
  }

  private readonly shown = (batch: Batch): boolean =>
    !batch.kind || this.layers.kinds.has(batch.kind);

  private markShown(): void {
    this.markers.mark(this.marked.filter((thing) => this.layers.kinds.has(thing.kind)));
  }

  private viewProjection(view: View, width: number, height: number): mat4 {
    const { view: look, projection } = matrices(view, width, height);
    return mat4.multiply(mat4.create(), projection, look);
  }

  // The pick pass's hidden picture, made again when the canvas changes size.
  private target(width: number, height: number): PickTarget {
    const { gl } = this;
    const known = this.pickTarget;
    if (known && known.width === width && known.height === height) return known;
    if (known) {
      gl.deleteFramebuffer(known.frame);
      gl.deleteRenderbuffer(known.colour);
      gl.deleteRenderbuffer(known.depth);
    }
    const frame = gl.createFramebuffer();
    const colour = gl.createRenderbuffer();
    const depth = gl.createRenderbuffer();
    gl.bindRenderbuffer(gl.RENDERBUFFER, colour);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.RGBA8, width, height);
    gl.bindRenderbuffer(gl.RENDERBUFFER, depth);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, width, height);
    gl.bindFramebuffer(gl.FRAMEBUFFER, frame);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.RENDERBUFFER, colour);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    this.pickTarget = { frame, colour, depth, width, height };
    return this.pickTarget;
  }

  private async load(near: [number, number]): Promise<void> {
    const started = performance.now();
    await Promise.all([
      ...this.ground.wanted.map(async (texture, index) => {
        const data = await this.bytes(texture.path);
        if (!data || this.stopped) return;
        const sheet = makeGroundTexture(this.gl, data);
        if (!sheet) return;
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
