<script lang="ts">
/**
 * The map view (the Terrain Editor button): the whole map drawn with WebGL2, read only. It asks the app for the scene
 * each time it opens, so it shows the session's current objects (Review Focus 4). The wheel zooms, a middle or right
 * drag pans, Ctrl with a right drag turns, the arrow keys pan (plan's call, to be matched to World Editor's). The
 * camera starts over player 1's start location, else the map's middle.
 */
import { fileUrl, mapScene, terrainUrl } from "./api.ts";
import { panned, turned, type View, zoomed } from "./map/camera.ts";
import { GlHost } from "./map/gl/host.ts";
import { MapRenderer } from "./map/gl/renderer.ts";
import { groundAt, type MapScene, readTerrain, type Terrain } from "./map/scene.ts";
import { t } from "./text.ts";

type Props = {
  /** The graphics mode the map is drawn in: `sd`, `hd` or `de`. */
  mode: string;
};

let { mode }: Props = $props();

let canvas: HTMLCanvasElement | undefined = $state();
let status = $state<"loading" | "ready" | "failed">("loading");
let failure = $state("");
let problems = $state<string[]>([]);
let software = $state(false);

/** The game's own camera looks down 56 degrees from 1650 away (angle of attack 304). */
const pitch = (56 * Math.PI) / 180;
let view: View = { x: 0, y: 0, z: 0, distance: 1650, yaw: 0, pitch };
let terrain: Terrain | null = null;
let renderer: MapRenderer | null = null;
let frame = 0;
let drag: { x: number; y: number; turn: boolean } | null = null;

$effect(() => {
  const shown = mode;
  const target = canvas;
  if (!target) return;
  let host: GlHost | null = null;
  let stopped = false;
  void open(shown, target, (made) => {
    if (stopped) made.stop();
    else host = made;
  });
  const resize = new ResizeObserver(() => sizeCanvas(target));
  resize.observe(target);
  return () => {
    stopped = true;
    resize.disconnect();
    host?.stop();
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
  };
});

async function open(
  shown: string,
  target: HTMLCanvasElement,
  keep: (host: GlHost) => void,
): Promise<void> {
  status = "loading";
  problems = [];
  const started = performance.now();
  try {
    const scene = await mapScene(shown);
    problems = scene.problems;
    if (scene.columns === 0) {
      status = "ready";
      return;
    }
    const response = await fetch(terrainUrl(shown));
    if (!response.ok) throw new Error(t("mapView.noTerrain"));
    const read = readTerrain(await response.arrayBuffer(), scene.left, scene.bottom);
    console.info(`[timing] map scene ${Math.round(performance.now() - started)} ms`);
    terrain = read;
    view = start(scene, read);
    sizeCanvas(target);
    keep(
      new GlHost(target, (gl) => {
        software = softwareRenderer(gl);
        renderer = new MapRenderer(
          gl,
          scene,
          read,
          (path) => fileUrl(path, shown),
          redraw,
          (ms) => console.info(`[timing] map ground textures ${Math.round(ms)} ms`),
        );
        redraw();
        return () => {
          renderer?.dispose();
          renderer = null;
        };
      }),
    );
    status = "ready";
  } catch (error) {
    failure = error instanceof Error ? error.message : String(error);
    status = "failed";
  }
}

/** Over player 1's start location, else the map's middle. */
function start(scene: MapScene, read: Terrain): View {
  const home = scene.things.find((thing) => thing.kind === "StartLocation" && thing.player === 0);
  const x = home?.x ?? read.left + ((read.columns - 1) * 128) / 2;
  const y = home?.y ?? read.bottom + ((read.rows - 1) * 128) / 2;
  return { x, y, z: groundAt(read, x, y), distance: 1650, yaw: 0, pitch };
}

/** Whether WebView2 draws without the graphics card (Chrome's software renderers), which is slow. */
function softwareRenderer(gl: WebGL2RenderingContext): boolean {
  const info = gl.getExtension("WEBGL_debug_renderer_info");
  const name = String(gl.getParameter(info ? info.UNMASKED_RENDERER_WEBGL : gl.RENDERER) ?? "");
  return /swiftshader|basic render|llvmpipe|software/i.test(name);
}

function sizeCanvas(target: HTMLCanvasElement): void {
  const scale = window.devicePixelRatio || 1;
  const width = Math.max(1, Math.round(target.clientWidth * scale));
  const height = Math.max(1, Math.round(target.clientHeight * scale));
  if (target.width !== width || target.height !== height) {
    target.width = width;
    target.height = height;
  }
  redraw();
}

function redraw(): void {
  if (frame) return;
  frame = requestAnimationFrame(() => {
    frame = 0;
    if (canvas) renderer?.draw(view, canvas.width, canvas.height);
  });
}

/** Moves the camera and keeps it looking at the ground. */
function move(next: View): void {
  view = terrain ? { ...next, z: groundAt(terrain, next.x, next.y) } : next;
  redraw();
}

/** World units a pixel moves at the camera's target. */
function perPixel(): number {
  return (2 * view.distance * Math.tan(Math.PI / 6)) / Math.max(canvas?.clientHeight ?? 1, 1);
}

function onwheel(event: WheelEvent): void {
  event.preventDefault();
  move(zoomed(view, -Math.sign(event.deltaY)));
}

function onpointerdown(event: PointerEvent): void {
  if (event.button !== 1 && event.button !== 2) return;
  event.preventDefault();
  canvas?.setPointerCapture(event.pointerId);
  drag = { x: event.clientX, y: event.clientY, turn: event.button === 2 && event.ctrlKey };
}

function onpointermove(event: PointerEvent): void {
  if (!drag) return;
  const dx = event.clientX - drag.x;
  const dy = event.clientY - drag.y;
  drag = { ...drag, x: event.clientX, y: event.clientY };
  if (drag.turn) move(turned(view, dx * 0.005, dy * 0.005));
  else move(panned(view, -dx * perPixel(), dy * perPixel()));
}

function onpointerup(event: PointerEvent): void {
  if (!drag) return;
  canvas?.releasePointerCapture(event.pointerId);
  drag = null;
}

function onkeydown(event: KeyboardEvent): void {
  const step = view.distance * 0.08;
  const by: Record<string, [number, number]> = {
    ArrowLeft: [-step, 0],
    ArrowRight: [step, 0],
    ArrowUp: [0, step],
    ArrowDown: [0, -step],
  };
  const pan = by[event.key];
  if (!pan) return;
  event.preventDefault();
  move(panned(view, pan[0], pan[1]));
}
</script>

<div class="relative h-full min-h-0 overflow-hidden bg-[#0d0d12]" data-testid="map-view">
  <canvas
    bind:this={canvas}
    class="block h-full w-full outline-none"
    tabindex="0"
    aria-label={t("main.map")}
    {onwheel}
    {onpointerdown}
    {onpointermove}
    {onpointerup}
    {onkeydown}
    oncontextmenu={(event) => event.preventDefault()}
  ></canvas>
  {#if status !== "ready" || problems.length > 0 || software}
    <div class="pointer-events-none absolute left-3 top-3 flex max-w-md flex-col gap-1 text-sm">
      {#if status === "loading"}
        <p class="rounded bg-surface/90 px-2 py-1 text-muted">{t("mapView.loading")}</p>
      {:else if status === "failed"}
        <p class="rounded bg-surface/90 px-2 py-1 text-warn">{t("mapView.failed", { error: failure })}</p>
      {/if}
      {#if software}
        <p class="rounded bg-surface/90 px-2 py-1 text-warn">{t("mapView.software")}</p>
      {/if}
      {#each problems as problem (problem)}
        <p class="rounded bg-surface/90 px-2 py-1 text-warn">{problem}</p>
      {/each}
    </div>
  {/if}
</div>
