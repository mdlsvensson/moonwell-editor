<script lang="ts">
/**
 * The map view (the Terrain Editor button): the whole map drawn with WebGL2, read only. It asks the app for the scene
 * each time it opens, so it shows the session's current objects (Review Focus 4). The mouse and keys are Blender's
 * (Sven, 2026-10-29): a middle drag orbits, Shift with it pans, Ctrl with it zooms, the wheel zooms; the numpad's 1, 3
 * and 7 look from the front, right and top (Ctrl the opposite side), 2, 4, 6 and 8 orbit (Ctrl pans), 9 turns round,
 * + and - zoom; Home frames the map, the numpad's . the selection. The camera starts over player 1's start location,
 * else the map's middle.
 *
 * Hovering highlights what is under the mouse and a click selects it; the line at the bottom names it, with the name
 * triggers use for it. In pick mode (`pickOnMap`, for the trigger editor) a click on the asked kind answers, Esc
 * cancels.
 */
import { fileUrl, mapScene, terrainUrl } from "./api.ts";
import { framed, numpadView, orbited, panned, ray, type View, zoomed } from "./map/camera.ts";
import { GlHost } from "./map/gl/host.ts";
import { allLayers, type Layers, MapRenderer } from "./map/gl/renderer.ts";
import { groundPoint } from "./map/groundRay.ts";
import { answer, picking, pickOnMap } from "./map/pick.ts";
import { cameraAt, choose, type Pick, type PickKind, regionAt } from "./map/picking.ts";
import { groundAt, type MapScene, readTerrain, type Terrain, type ThingKind } from "./map/scene.ts";
import { type TextKey, t } from "./text.ts";

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
/** What the mouse is over, and what a click selected. */
let hovered = $state<Pick | null>(null);
let selected = $state<Pick | null>(null);
/** The result of the last "Try pick mode", shown until the next. */
let tried = $state("");
let layers = $state<Layers>(allLayers);

/** The game's own camera looks down 56 degrees from 1650 away (angle of attack 304). */
const pitch = (56 * Math.PI) / 180;
/** How near the mouse must be to a camera's target to pick it, in world units. */
const cameraReach = 96;
let view: View = { x: 0, y: 0, z: 0, distance: 1650, yaw: 0, pitch };
let scene: MapScene | null = null;
let terrain: Terrain | null = null;
let renderer: MapRenderer | null = null;
let frame = 0;
let hoverFrame = 0;
/** A middle drag: what it does, chosen when it starts (Blender's: Shift pans, Ctrl zooms, else it orbits). */
let drag: { x: number; y: number; does: "orbit" | "pan" | "zoom" } | null = null;
let press: { x: number; y: number } | null = null;

/** The kinds a layer switch shows or hides. */
const thingLayers: { kinds: ThingKind[]; key: TextKey }[] = [
  { kinds: ["Doodad"], key: "mapView.layer.doodads" },
  { kinds: ["Destructable"], key: "mapView.layer.destructables" },
  { kinds: ["Unit"], key: "mapView.layer.units" },
  { kinds: ["Item"], key: "mapView.layer.items" },
  { kinds: ["StartLocation"], key: "mapView.layer.startLocations" },
];
/** The pick kinds, each with its name's key, in the order the menu lists them. */
const kindNames: Record<PickKind, TextKey> = {
  unit: "mapView.kind.unit",
  item: "mapView.kind.item",
  destructable: "mapView.kind.destructable",
  doodad: "mapView.kind.doodad",
  startLocation: "mapView.kind.startLocation",
  region: "mapView.kind.region",
  camera: "mapView.kind.camera",
  point: "mapView.kind.point",
};
const pickKinds = Object.keys(kindNames) as PickKind[];

/** The host made last, whatever the mode: when the view goes, its context goes back to the browser. */
let lastHost: GlHost | null = null;
$effect(() => {
  if (!canvas) return;
  return () => lastHost?.stop(true);
});

$effect(() => {
  const shown = mode;
  const target = canvas;
  if (!target) return;
  let host: GlHost | null = null;
  let stopped = false;
  void open(shown, target, (made) => {
    if (stopped) made.stop();
    else {
      host = made;
      lastHost = made;
    }
  });
  const resize = new ResizeObserver(() => sizeCanvas(target));
  resize.observe(target);
  return () => {
    stopped = true;
    resize.disconnect();
    host?.stop();
    if (frame) cancelAnimationFrame(frame);
    if (hoverFrame) cancelAnimationFrame(hoverFrame);
    frame = 0;
    hoverFrame = 0;
  };
});

// The white diamond follows what is hovered and selected; a selected region or camera is drawn white.
$effect(() => {
  const marked = [hovered, selected].filter((pick): pick is Pick => !!pick && pick.typeId !== null);
  const things = scene?.things ?? [];
  renderer?.setHighlight(
    things.filter((thing) =>
      marked.some((pick) =>
        pick.ref ? pick.ref === thing.ref : pick.x === thing.x && pick.y === thing.y,
      ),
    ),
  );
  renderer?.selectMark(
    selected && (selected.kind === "region" || selected.kind === "camera") ? selected.ref : null,
  );
});

$effect(() => {
  renderer?.setLayers(layers);
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
    const read = await mapScene(shown);
    problems = read.problems;
    scene = read;
    if (read.columns === 0) {
      status = "ready";
      return;
    }
    const response = await fetch(terrainUrl(shown));
    if (!response.ok) throw new Error(t("mapView.noTerrain"));
    const grid = readTerrain(await response.arrayBuffer(), read.left, read.bottom);
    console.info(`[timing] map scene ${Math.round(performance.now() - started)} ms`);
    terrain = grid;
    view = start(read, grid);
    sizeCanvas(target);
    keep(
      new GlHost(target, (gl) => {
        software = softwareRenderer(gl);
        renderer = new MapRenderer(
          gl,
          read,
          grid,
          (path) => fileUrl(path, shown),
          redraw,
          (what, ms) => console.info(`[timing] map ${what} ${Math.round(ms)} ms`),
          [view.x, view.y],
        );
        renderer.setLayers(layers);
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
function start(read: MapScene, grid: Terrain): View {
  const home = read.things.find((thing) => thing.kind === "StartLocation" && thing.player === 0);
  const x = home?.x ?? grid.left + ((grid.columns - 1) * 128) / 2;
  const y = home?.y ?? grid.bottom + ((grid.rows - 1) * 128) / 2;
  return { x, y, z: groundAt(grid, x, y), distance: 1650, yaw: 0, pitch };
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
  frame = requestAnimationFrame((now) => {
    frame = 0;
    if (!canvas || !renderer) return;
    renderer.draw(view, canvas.width, canvas.height, now);
    // Animations and water keep the scene drawing, a frame at a time.
    if (renderer.moving) redraw();
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

/** What is under the mouse at `clientX`, `clientY`, as a click would pick it. */
function under(clientX: number, clientY: number): Pick | null {
  if (!canvas || !renderer || !scene || !terrain) return null;
  const box = canvas.getBoundingClientRect();
  const scale = canvas.width / Math.max(box.width, 1);
  const px = (clientX - box.left) * scale;
  const py = (clientY - box.top) * scale;
  const number = renderer.pickAt(view, canvas.width, canvas.height, px, py);
  const thing = number > 0 ? (scene.things[number - 1] ?? null) : null;
  const { origin, direction } = ray(view, canvas.width, canvas.height, px, py);
  const point = groundPoint(terrain, origin, direction);
  const region = point && layers.regions ? regionAt(scene.regions, point[0], point[1]) : null;
  const camera =
    point && layers.cameras ? cameraAt(scene.cameras, point[0], point[1], cameraReach) : null;
  return choose({ thing, region, camera, point }, $picking.kind);
}

function onwheel(event: WheelEvent): void {
  event.preventDefault();
  move(zoomed(view, -Math.sign(event.deltaY)));
}

function onpointerdown(event: PointerEvent): void {
  if (event.button === 0) {
    press = { x: event.clientX, y: event.clientY };
    return;
  }
  if (event.button !== 1) return;
  event.preventDefault();
  canvas?.setPointerCapture(event.pointerId);
  drag = {
    x: event.clientX,
    y: event.clientY,
    does: event.shiftKey ? "pan" : event.ctrlKey ? "zoom" : "orbit",
  };
}

function onpointermove(event: PointerEvent): void {
  if (drag) {
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    drag = { ...drag, x: event.clientX, y: event.clientY };
    if (drag.does === "orbit") move(orbited(view, dx, dy));
    else if (drag.does === "pan") move(panned(view, -dx * perPixel(), dy * perPixel()));
    else move(zoomed(view, -dy / 40));
    return;
  }
  // What is under the mouse, once a frame.
  const { clientX, clientY } = event;
  if (hoverFrame) cancelAnimationFrame(hoverFrame);
  hoverFrame = requestAnimationFrame(() => {
    hoverFrame = 0;
    hovered = under(clientX, clientY);
  });
}

function onpointerup(event: PointerEvent): void {
  if (drag) {
    canvas?.releasePointerCapture(event.pointerId);
    drag = null;
    return;
  }
  if (event.button !== 0 || !press) return;
  const still = Math.hypot(event.clientX - press.x, event.clientY - press.y) < 4;
  press = null;
  if (!still) return;
  const pick = under(event.clientX, event.clientY);
  if ($picking.kind !== null) {
    if (pick) answer(pick);
    return;
  }
  selected = pick;
}

function onpointerleave(): void {
  hovered = null;
}

function onkeydown(event: KeyboardEvent): void {
  if (event.key === "Escape") {
    if ($picking.kind !== null) answer(null);
    else selected = null;
    return;
  }
  if (event.code === "Home" && terrain) {
    event.preventDefault();
    const right = terrain.left + (terrain.columns - 1) * 128;
    const top = terrain.bottom + (terrain.rows - 1) * 128;
    move(framed(view, terrain.left, terrain.bottom, right, top));
    return;
  }
  if (event.code === "NumpadDecimal") {
    event.preventDefault();
    const at = selected ?? hovered;
    if (at) move({ ...view, x: at.x, y: at.y, distance: Math.min(view.distance, 900) });
    return;
  }
  const next = numpadView(view, event.code, event.ctrlKey);
  if (!next) return;
  event.preventDefault();
  move(next);
}

// Esc cancels pick mode from anywhere in the window, not only the canvas.
function onwindowkeydown(event: KeyboardEvent): void {
  if (event.key === "Escape" && $picking.kind !== null) answer(null);
}

function toggle(kinds: ThingKind[]): void {
  const next = new Set(layers.kinds);
  const on = kinds.every((kind) => next.has(kind));
  for (const kind of kinds) {
    if (on) next.delete(kind);
    else next.add(kind);
  }
  layers = { ...layers, kinds: next };
}

/** Tries pick mode as the trigger editor will use it, and shows what it gave. */
async function tryPick(kind: PickKind): Promise<void> {
  tried = "";
  const pick = await pickOnMap(kind);
  tried = pick ? describe(pick) : t("mapView.pickCancelled");
}

/** A pick in words: its name, its type, its trigger name, or a point's place. */
function describe(pick: Pick): string {
  if (pick.kind === "point") {
    return t("mapView.point", {
      x: Math.round(pick.x),
      y: Math.round(pick.y),
      z: Math.round(pick.z),
    });
  }
  return [pick.name, pick.typeId, pick.ref].filter(Boolean).join(" · ");
}

/** The selected or hovered thing's owner, for the line at the bottom. */
function ownerOf(pick: Pick): string {
  if (pick.kind !== "unit" && pick.kind !== "startLocation") return "";
  const thing = scene?.things.find(
    (each) => each.ref === pick.ref && each.x === pick.x && each.y === pick.y,
  );
  return thing ? t("mapView.player", { number: thing.player + 1 }) : "";
}
</script>

<svelte:window onkeydown={onwindowkeydown} />

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
    {onpointerleave}
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
  <div
    class="absolute right-3 top-3 flex flex-col items-end gap-1 text-sm"
    role="group"
    aria-label={t("mapView.layers")}
  >
    <div class="flex flex-wrap justify-end gap-1">
      {#each thingLayers as layer (layer.key)}
        <button
          type="button"
          class="rounded border border-line px-2 py-0.5 {layer.kinds.every((kind) => layers.kinds.has(kind))
            ? 'bg-surface/90 text-fg'
            : 'bg-surface/60 text-muted line-through'}"
          aria-pressed={layer.kinds.every((kind) => layers.kinds.has(kind))}
          onclick={() => toggle(layer.kinds)}>{t(layer.key)}</button
        >
      {/each}
      <button
        type="button"
        class="rounded border border-line px-2 py-0.5 {layers.regions ? 'bg-surface/90 text-fg' : 'bg-surface/60 text-muted line-through'}"
        aria-pressed={layers.regions}
        onclick={() => (layers = { ...layers, regions: !layers.regions })}>{t("mapView.layer.regions")}</button
      >
      <button
        type="button"
        class="rounded border border-line px-2 py-0.5 {layers.cameras ? 'bg-surface/90 text-fg' : 'bg-surface/60 text-muted line-through'}"
        aria-pressed={layers.cameras}
        onclick={() => (layers = { ...layers, cameras: !layers.cameras })}>{t("mapView.layer.cameras")}</button
      >
    </div>
    <!-- Until the trigger editor asks the map for its parameters, pick mode can be tried here (plan's call). -->
    <label class="flex items-center gap-1 rounded border border-line bg-surface/90 px-2 py-0.5 text-muted">
      {t("mapView.tryPick")}
      <select
        class="bg-transparent text-fg"
        value=""
        onchange={(event) => {
          const kind = event.currentTarget.value as PickKind;
          event.currentTarget.value = "";
          if (kind) void tryPick(kind);
        }}
      >
        <option value="">—</option>
        {#each pickKinds as kind (kind)}
          <option value={kind}>{t(kindNames[kind])}</option>
        {/each}
      </select>
    </label>
    {#if tried}
      <p class="rounded bg-surface/90 px-2 py-0.5 text-fg">{tried}</p>
    {/if}
  </div>
  {#if $picking.kind !== null}
    <p
      class="pointer-events-none absolute left-1/2 top-3 -translate-x-1/2 rounded border border-accent bg-surface/95 px-3 py-1 text-sm text-fg"
      data-testid="pick-banner"
    >
      {t("mapView.picking", { kind: t(kindNames[$picking.kind]) })}
    </p>
  {/if}
  {#if selected ?? hovered}
    {@const shown = selected ?? hovered}
    {#if shown}
      <p
        class="pointer-events-none absolute bottom-3 left-3 max-w-[80%] truncate rounded bg-surface/90 px-2 py-1 text-sm text-fg"
        data-testid="map-selection"
      >
        {describe(shown)}{#if ownerOf(shown)}<span class="text-muted"> · {ownerOf(shown)}</span>{/if}
      </p>
    {/if}
  {/if}
</div>
