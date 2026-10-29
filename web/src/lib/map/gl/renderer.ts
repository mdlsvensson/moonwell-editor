/**
 * Draws a map scene on one WebGL2 context: the ground, the cliffs, the placed things and their markers, then the water
 * over them and the blended parts of models last. Textures and models load in the background, nearest the camera
 * first; each that arrives asks for the scene to be drawn again.
 */
import { mat4 } from "gl-matrix";
import { eye, matrices, type View } from "../camera.ts";
import { colourToNumber } from "../picking.ts";
import type { MapScene, PlacedThing, Terrain, ThingKind } from "../scene.ts";
import { type Batch, cliffBatches, thingBatches } from "./batches.ts";
import { GroundLayer } from "./ground.ts";
import { type Lighting, lighting, type Shadow } from "./lighting.ts";
import { MarkerLayer } from "./markers.ts";
import { MarksLayer } from "./marks.ts";
import { ShadowMap, shadowReach, sunView } from "./shadows.ts";
import { makeGroundTexture, makeTexture } from "./textures.ts";
import { ModelLayer } from "./things.ts";
import { UnitShadowLayer } from "./unitShadows.ts";
import { ripplesPath, WaterLayer } from "./water.ts";

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
  /** The tileset's terrain light (ground and cliffs) and unit light (things) at noon. */
  private readonly terrainLight: Lighting;
  private readonly unitLight: Lighting;
  /** The sun's shadow map (HD and Definitive Edition), and the empty one that stands in while it is drawn. */
  private readonly shadowMap: ShadowMap | null;
  private readonly noShadow: ShadowMap;
  /** Classic's unit shadow pictures; null in HD and Definitive Edition, which cast shadows instead. */
  private readonly unitShadows: UnitShadowLayer | null;
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
    this.terrainLight = lighting(scene.lights.terrain, scene.lights.sky, "terrain");
    this.unitLight = lighting(scene.lights.units, scene.lights.sky, "units");
    this.noShadow = new ShadowMap(gl, 1);
    this.shadowMap = this.terrainLight.pbr ? new ShadowMap(gl, 4096) : null;
    const none: Shadow = { texture: this.noShadow.texture, matrix: mat4.create(), on: false };
    this.terrainLight.shadow = none;
    this.unitLight.shadow = none;
    this.unitShadows = this.terrainLight.pbr
      ? null
      : new UnitShadowLayer(gl, scene.things, terrain);
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

  /** Whether anything moves (an animation, the water), so the scene must be drawn again each frame. */
  get moving(): boolean {
    return this.things.moving || this.cliffs.moving || this.water.moving;
  }

  /**
   * Draws the scene as `view` sees it on a canvas of `width` by `height` pixels, its animations and water at `now`
   * (milliseconds, as performance.now gives them).
   */
  draw(view: View, width: number, height: number, now = 0): void {
    const { gl } = this;
    const seconds = now / 1000;
    this.things.time = seconds;
    this.cliffs.time = seconds;
    this.water.time = seconds;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, width, height);
    gl.clearColor(0.05, 0.05, 0.07, 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    this.drawShadows(view);
    gl.viewport(0, 0, width, height);
    const viewProjection = this.viewProjection(view, width, height);
    this.ground.draw(viewProjection, this.terrainLight);
    this.cliffs.draw(viewProjection, this.terrainLight);
    if (this.layers.kinds.has("Unit")) this.unitShadows?.draw(viewProjection);
    this.things.draw(viewProjection, this.unitLight, false, this.shown);
    this.markers.draw(viewProjection);
    this.marks.draw(viewProjection, this.layers.regions, this.layers.cameras);
    this.water.draw(viewProjection, this.terrainLight, eye(view));
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
    this.ground.draw(viewProjection, this.terrainLight);
    this.cliffs.draw(viewProjection, this.terrainLight);
    gl.colorMask(true, true, true, true);
    gl.disable(gl.BLEND);
    this.things.draw(viewProjection, this.unitLight, true, this.shown);
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
    this.shadowMap?.dispose();
    this.noShadow.dispose();
    this.unitShadows?.dispose();
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

  // The sun's view of the ground, cliffs and models around the camera's target, into the shadow map; the lights then
  // read it. Drawn with the empty map bound, so the map is not read while it is drawn.
  private drawShadows(view: View): void {
    const map = this.shadowMap;
    if (!map) return;
    const matrix = sunView(
      [view.x, view.y, view.z],
      this.terrainLight.toward,
      shadowReach(view.distance),
    );
    const none: Shadow = { texture: this.noShadow.texture, matrix, on: false };
    const terrainLight = { ...this.terrainLight, shadow: none };
    const unitLight = { ...this.unitLight, shadow: none };
    map.begin();
    this.ground.draw(matrix, terrainLight);
    this.cliffs.draw(matrix, terrainLight);
    this.things.draw(matrix, unitLight, false, this.shown);
    map.end();
    const shadow: Shadow = { texture: map.texture, matrix, on: true };
    this.terrainLight.shadow = shadow;
    this.unitLight.shadow = shadow;
  }

  // One frame of the water texture, if the view draws it.
  private async loadWaterFrame(index: number): Promise<void> {
    const path = this.water.framePaths(this.terrainLight.pbr)[index];
    if (!path) return;
    const data = await this.bytes(path);
    const frame = data && !this.stopped ? makeTexture(this.gl, data, true) : null;
    if (!frame) return;
    this.water.setFrame(index, frame);
    if (index === 0) this.changed();
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
      // The water's first frame with the ground; the rest after, so the models need not wait for them.
      this.loadWaterFrame(0),
      ...(this.unitShadows?.wanted ?? []).map(async (path) => {
        const data = await this.bytes(path);
        const picture = data && !this.stopped ? makeTexture(this.gl, data) : null;
        if (picture) {
          this.unitShadows?.setPicture(path, picture);
          this.changed();
        }
      }),
      (async () => {
        if (!this.terrainLight.pbr) return;
        const data = await this.bytes(ripplesPath);
        const ripples = data && !this.stopped ? makeTexture(this.gl, data, true) : null;
        if (ripples) {
          this.water.setRipples(ripples);
          this.changed();
        }
      })(),
    ]);
    if (this.stopped) return;
    this.loaded("ground", performance.now() - started);
    void (async () => {
      const count = this.water.framePaths(this.terrainLight.pbr).length;
      for (let index = 1; index < count && !this.stopped; index++) await this.loadWaterFrame(index);
    })();
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
