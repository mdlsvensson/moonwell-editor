<script lang="ts">
/**
 * A 3D preview of a game model: it plays the model's Stand animation, lit and textured, and turns slowly; a drag turns
 * it by hand. war3-model parses and draws the model with WebGL. Each model gets its own canvas and GL context, and
 * both go when the path changes or the preview closes. Particles and ribbons are drawn as war3-model draws them. The
 * Reforged (HD) and Definitive Edition models are drawn with war3-model's HD shader (the diffuse, normal and ORM maps,
 * lit, with the team colour), once their skins are read again (see `readSkins`); their emissive maps, reflections and
 * popcorn particles are not drawn. The team colour and hero glow take a player's colour, and a row of the 24 colours
 * can pick it (Sven, 2026-10-27). The side panel's preview can play any of the model's animations, picked from a list
 * (Sven, 2026-10-27).
 */
import MoveVertical from "@lucide/svelte/icons/move-vertical";
import RotateCw from "@lucide/svelte/icons/rotate-cw";
import { decodeDds, parseHeaders } from "dds-parser";
import { mat4, quat, vec3 } from "gl-matrix";
import { untrack } from "svelte";
import { decodeBLP, getBLPImageData, ModelRenderer, parseMDL, parseMDX } from "war3-model";
import {
  type Extent,
  frame,
  hdTextureUse,
  posed,
  readSkins,
  type Skinnable,
  skinGeosets,
  solidExtent,
  standSequence,
  stripChunks,
  unreadChunks,
} from "./mdx.ts";
import Select from "./Select.svelte";
import { playerIndex, teamColors, teamRgb } from "./teamColors.ts";
import { t, tCount } from "./text.ts";

type Props = {
  /** The model's game path as object data spells it, with .mdl, .mdx or no extension. */
  path: string;
  /** The URL that answers a game file's bytes (404 when there is none), for the model and its textures. */
  fileUrl: (path: string) => string;
  /** The width and height in pixels. */
  size?: number;
  /** The player whose colour the team colour and hero glow take, 0 for player 1 (Red) to 23 (Peanut). */
  teamColor?: number;
  /** Whether to show the 24 player colours under the model; a click sets `teamColor`. */
  picker?: boolean;
  /** Whether to show the model's animations to pick from. */
  animations?: boolean;
};

let {
  path,
  fileUrl,
  size = 240,
  teamColor = $bindable(0),
  picker = false,
  animations = false,
}: Props = $props();

/** The model's animations by name, in its order, once it is loaded; and the one playing. */
let sequences = $state<string[]>([]);
let playing = $state(-1);

let host: HTMLDivElement | undefined = $state();
let status: "loading" | "ready" | "error" = $state("loading");
let error = $state("");
/** The textures that could not be loaded; the model is drawn without them. */
let missing: string[] = $state([]);
// The model turns by itself until the user drags it; the play button sets it turning again (Sven, 2026-10-27).
let turning = $state(true);
// Whether Ctrl and the wheel moved the model up or down; a button puts it back (Sven, 2026-10-28).
let lifted = $state(false);

/** The preview on show, so a new team colour reaches its renderer without loading the model again. */
let current: ReturnType<typeof show> | undefined;

// A new size only resizes the canvas: loading the model again for it would hide the animations while it loads, which
// changes the panel's height, and with it maybe its scroll bar and its width, and so the size again (Sven,
// 2026-10-28: the side panel shook).
$effect(() => {
  if (!host) return;
  const preview = show(
    host,
    path,
    fileUrl,
    untrack(() => size),
    untrack(() => teamColor),
  );
  current = preview;
  return () => {
    preview.stop();
    if (current === preview) current = undefined;
  };
});

$effect(() => {
  current?.tint(teamColor);
});

$effect(() => {
  current?.resize(size);
});

type Model = ReturnType<typeof parseMDX>;

/** The textures the game swaps in by number, for the ones a model leaves to the map: cliffs and trees. */
const replaceable: Record<number, string> = {
  11: "ReplaceableTextures\\Cliff\\Cliff0.blp",
  31: "ReplaceableTextures\\LordaeronTree\\LordaeronSummerTree.blp",
  32: "ReplaceableTextures\\AshenvaleTree\\AshenTree.blp",
  33: "ReplaceableTextures\\BarrensTree\\BarrensTree.blp",
  34: "ReplaceableTextures\\NorthrendTree\\NorthTree.blp",
  35: "ReplaceableTextures\\Mushroom\\MushroomTree.blp",
  36: "ReplaceableTextures\\RuinsTree\\RuinsTree.blp",
  37: "ReplaceableTextures\\OutlandMushroomTree\\MushroomTree.blp",
};

/** One pixel of a flat normal map (straight out) and of an ORM map (unshadowed, rough, not metal, no team colour). */
const flat = { normal: [128, 128, 255, 255], orm: [255, 200, 0, 0] };

const fieldOfView = Math.PI / 4;
/** Radians a second the model turns by itself. */
const turnSpeed = 0.35;

/**
 * Loads the model at `at` into a new canvas in `into`, in player `team`'s colour, and keeps drawing it until `stop`.
 */
function show(
  into: HTMLElement,
  at: string,
  url: (path: string) => string,
  px: number,
  team: number,
) {
  status = "loading";
  error = "";
  missing = [];
  sequences = [];
  playing = -1;
  lifted = false;
  const abort = new AbortController();
  let stopped = false;
  let frameId = 0;
  let renderer: ModelRenderer | undefined;
  // The flat maps the classic materials of an HD model are drawn with (see `flatFill`); null when it has none.
  let maps: MapsFill | null = null;
  // war3-model reads the colour each frame: it fills texture 1 (team colour) with it and draws texture 2 (the hero
  // glow) as a disc of it.
  const colour = vec3.fromValues(...teamRgb(team));
  const canvas = document.createElement("canvas");
  const ratio = window.devicePixelRatio || 1;
  const sizeTo = (pixels: number) => {
    canvas.width = Math.round(pixels * ratio);
    canvas.height = Math.round(pixels * ratio);
  };
  sizeTo(px);
  canvas.className = "block size-full touch-none cursor-grab";
  canvas.setAttribute("role", "img");
  canvas.setAttribute("aria-label", t("model.previewOf", { path: at }));
  canvas.style.visibility = "hidden";
  into.prepend(canvas);
  const gl =
    canvas.getContext("webgl2", { alpha: false, antialias: true }) ??
    canvas.getContext("webgl", { alpha: false, antialias: true });

  const fail = (message: string) => {
    if (stopped) return;
    status = "error";
    error = message;
  };

  // The camera: it turns about the model's centre, `yaw` round the up axis and `pitch` above the ground.
  let yaw = Math.PI / 5;
  let pitch = 0.3;
  let dragging: { x: number; y: number } | null = null;
  const down = (event: PointerEvent) => {
    dragging = { x: event.clientX, y: event.clientY };
    turning = false;
    canvas.setPointerCapture(event.pointerId);
    canvas.style.cursor = "grabbing";
  };
  const move = (event: PointerEvent) => {
    if (!dragging) return;
    yaw -= (event.clientX - dragging.x) * 0.01;
    pitch = Math.min(1.45, Math.max(-1.2, pitch + (event.clientY - dragging.y) * 0.01));
    dragging = { x: event.clientX, y: event.clientY };
  };
  const up = () => {
    dragging = null;
    canvas.style.cursor = "";
  };
  canvas.addEventListener("pointerdown", down);
  canvas.addEventListener("pointermove", move);
  canvas.addEventListener("pointerup", up);
  canvas.addEventListener("pointercancel", up);
  // The wheel zooms while the pointer is over the model, and with Ctrl held moves the model up and down (Sven,
  // 2026-10-27), within limits that keep it in view. `lift` is how far the camera's target is raised, in the model's
  // radius.
  let zoom = 1;
  let lift = 0;
  const wheel = (event: WheelEvent) => {
    event.preventDefault();
    if (event.ctrlKey) {
      lift = Math.min(1.5, Math.max(-1.5, lift + (event.deltaY > 0 ? -0.1 : 0.1)));
      lifted = Math.abs(lift) > 1e-6;
    } else zoom = Math.min(3.5, Math.max(0.25, zoom * (event.deltaY > 0 ? 1.12 : 1 / 1.12)));
    turning = false;
  };
  canvas.addEventListener("wheel", wheel, { passive: false });

  async function load(): Promise<void> {
    if (!gl) return fail(t("model.noWebGl"));
    const response = await fetch(url(at), { signal: abort.signal });
    if (response.status === 404) return fail(t("model.notFound"));
    if (!response.ok) return fail(t("model.loadFailedHttp", { status: response.status }));
    const bytes = await response.arrayBuffer();
    if (stopped) return;
    let model: Model;
    try {
      model = parse(bytes);
    } catch (e) {
      return fail(t("model.readFailed", { error: message(e) }));
    }
    if (model.Geosets.length === 0) return fail(t("model.nothingToDraw"));
    for (const texture of model.Textures) {
      const swap = texture.ReplaceableId && replaceable[texture.ReplaceableId];
      if (!texture.Image && swap) texture.Image = swap;
    }
    try {
      lightShaders(gl);
      renderer = new ModelRenderer(model);
      renderer.initGL(gl);
      renderer.setTeamColor(colour);
    } catch (e) {
      return fail(t("model.drawFailed", { error: message(e) }));
    }
    const sequence = standSequence(model.Sequences.map((s) => s.Name));
    if (sequence >= 0) renderer.setSequence(sequence);
    sequences = model.Sequences.map((each) => each.Name);
    playing = sequence;
    const pose = skinnedPose(model, renderer);

    const images = [
      ...new Set(model.Textures.map((texture) => texture.Image).filter((image) => image)),
    ];
    const failed = await Promise.all(
      images.map(async (image) => ((await loadTexture(image)) ? null : image)),
    );
    if (stopped) return;
    missing = failed.filter((image): image is string => image !== null);
    // Without its normal or ORM map the HD shader reads black from it and draws the geoset dark; a flat one stands in.
    for (const image of missing) {
      const use = hdTextureUse(model, image);
      if (use) setImage(image, new ImageData(new Uint8ClampedArray(flat[use]), 1, 1));
    }
    maps = flatFill(model);
    if (maps) {
      setImage(flatPaths.normal, new ImageData(new Uint8ClampedArray(flat.normal), 1, 1));
      setImage(flatPaths.orm, new ImageData(new Uint8ClampedArray(flat.orm), 1, 1));
    }

    const extents: (Extent | undefined)[] = [
      solidExtent(model, model.Sequences[sequence]?.Interval, pose),
      sequenceExtent(model, sequence),
      { min: model.Info.MinimumExtent, max: model.Info.MaximumExtent },
    ];
    const framing = frame(extents);
    status = "ready";
    canvas.style.visibility = "";
    start(framing.centre, framing.radius);
  }

  /** Fetches a texture and gives it to the renderer; false when it is missing or in a form it cannot read. */
  async function loadTexture(image: string): Promise<boolean> {
    try {
      const response = await fetch(url(image), { signal: abort.signal });
      if (!response.ok) return false;
      const data = await response.arrayBuffer();
      if (stopped || !renderer || !gl) return false;
      const head = new TextDecoder("latin1").decode(
        new Uint8Array(data, 0, Math.min(4, data.byteLength)),
      );
      if (head === "DDS ") {
        const info = parseHeaders(data);
        const s3tc = gl.getExtension("WEBGL_compressed_texture_s3tc");
        const format =
          s3tc &&
          {
            dxt1: s3tc.COMPRESSED_RGBA_S3TC_DXT1_EXT,
            dxt3: s3tc.COMPRESSED_RGBA_S3TC_DXT3_EXT,
            dxt5: s3tc.COMPRESSED_RGBA_S3TC_DXT5_EXT,
            ati2: undefined,
          }[info.format];
        if (format) {
          renderer.setTextureCompressedImage(image, format, data, info);
        } else {
          // Without the GPU's DXT support, the largest image is decoded here.
          const top = info.images[0];
          if (!top) return false;
          const { width, height } = top.shape;
          const pixels = decodeDds(
            new Uint8Array(data, top.offset, top.length),
            info.format,
            width,
            height,
          );
          setImage(image, new ImageData(new Uint8ClampedArray(pixels), width, height));
        }
        return true;
      }
      if (head.startsWith("BLP")) {
        const blp = decodeBLP(data);
        const top = getBLPImageData(blp, 0);
        setImage(image, new ImageData(new Uint8ClampedArray(top.data), top.width, top.height));
        return true;
      }
      return false;
    } catch {
      return false;
    }
  }

  /** Gives the renderer decoded pixels; it uploads them as it would an image, with mipmaps. */
  function setImage(image: string, pixels: ImageData): void {
    // setTextureImage hands its argument to texImage2D, which takes ImageData as well as an image element.
    renderer?.setTextureImage(image, pixels as unknown as HTMLImageElement);
  }

  function start(centre: [number, number, number], radius: number): void {
    if (!gl || !renderer) return;
    const draw = renderer;
    const distance = (radius / Math.sin(fieldOfView / 2)) * 0.95;
    const projection = mat4.create();
    mat4.perspective(projection, fieldOfView, 1, distance / 50, distance + radius * 10);
    const view = mat4.create();
    const target = vec3.fromValues(...centre);
    const eye = vec3.create();
    const facing = quat.create();
    const tilt = quat.create();
    const flat = vec3.create();
    const toEye = vec3.create();
    const background = clearColour(into);
    let last = performance.now();
    let failedFrame = false;

    const tick = (now: number) => {
      if (stopped) return;
      // The next frame is asked for first, so a frame that throws does not stop the drawing: it stopped on the
      // Footman's Decay Flesh, and no other animation could be picked after it (Sven, 2026-10-28).
      frameId = requestAnimationFrame(tick);
      const delta = Math.min(100, now - last);
      last = now;
      try {
        drawFrame(delta);
      } catch (e) {
        if (!failedFrame) console.error("[model] a frame could not be drawn:", e);
        failedFrame = true;
      }
    };

    const drawFrame = (delta: number) => {
      if (!dragging && turning) yaw += (turnSpeed * delta) / 1000;
      vec3.set(target, centre[0], centre[1], centre[2] + lift * radius);
      vec3.set(
        toEye,
        Math.cos(pitch) * Math.cos(yaw),
        Math.cos(pitch) * Math.sin(yaw),
        Math.sin(pitch),
      );
      vec3.scaleAndAdd(eye, target, toEye, distance * zoom);
      mat4.lookAt(view, eye, target, [0, 0, 1]);
      // Which way the camera faces, for the sprites (particles) that turn towards it.
      vec3.set(flat, toEye[0], toEye[1], 0);
      vec3.normalize(flat, flat);
      quat.rotationTo(facing, [1, 0, 0], flat);
      quat.rotationTo(tilt, flat, toEye);
      quat.mul(facing, tilt, facing);
      draw.setCamera(eye, facing);
      draw.setLightPosition(eye);
      draw.update(delta);

      gl.viewport(0, 0, canvas.width, canvas.height);
      gl.clearColor(background[0], background[1], background[2], 1);
      gl.depthMask(true);
      // A material's later layers lie on the same surface as its first, so equal depth has to pass.
      gl.depthFunc(gl.LEQUAL);
      gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
      const restore = maps ? fillMaps(draw, maps) : undefined;
      try {
        draw.render(view, projection, { wireframe: false, levelOfDetail: 0 });
      } finally {
        restore?.();
      }
    };
    frameId = requestAnimationFrame(tick);
  }

  load().catch((e) => {
    if (!stopped) fail(t("model.loadFailed", { error: message(e) }));
  });

  return {
    /** Plays the model's animation at `index` (in its order), from its start. */
    play(index: number): void {
      if (renderer && index >= 0 && index < sequences.length) {
        renderer.setSequence(index);
        playing = index;
      }
    },
    /** Gives the canvas a new width and height in pixels; the next frame draws at it. */
    resize(pixels: number): void {
      sizeTo(pixels);
    },
    /** Puts the model back at the height it was framed at. */
    lower(): void {
      lift = 0;
      lifted = false;
    },
    /** Sets the team colour to player `player`'s; the next frame draws it. */
    tint(player: number): void {
      vec3.set(colour, ...teamRgb(player));
      renderer?.setTeamColor(colour);
    },
    stop(): void {
      stopped = true;
      abort.abort();
      cancelAnimationFrame(frameId);
      if (renderer && gl) {
        // destroy() frees the buffers and shaders but not the textures it made.
        const textures = (
          renderer as unknown as { rendererData?: { textures?: Record<string, WebGLTexture> } }
        ).rendererData?.textures;
        try {
          renderer.destroy();
        } catch {
          // war3-model 4.0.1 throws partway for a classic model (it frees HD shaders it never made); losing the
          // context below frees what it leaves.
        }
        for (const texture of Object.values(textures ?? {})) gl.deleteTexture(texture);
      }
      renderer = undefined;
      gl?.getExtension("WEBGL_lose_context")?.loseContext();
      canvas.removeEventListener("pointerdown", down);
      canvas.removeEventListener("pointermove", move);
      canvas.removeEventListener("pointerup", up);
      canvas.removeEventListener("pointercancel", up);
      canvas.removeEventListener("wheel", wheel);
      canvas.remove();
    },
  };
}

/**
 * The model from an MDX file (without the chunks war3-model cannot read, and with the HD skins it misreads read again),
 * or from MDL text.
 */
function parse(bytes: ArrayBuffer): Model {
  const head = new TextDecoder("latin1").decode(
    new Uint8Array(bytes, 0, Math.min(4, bytes.byteLength)),
  );
  if (head !== "MDLX") return parseMDL(new TextDecoder().decode(bytes));
  const kept = stripChunks(bytes, unreadChunks);
  const model = parseMDX(kept);
  skinGeosets(model.Geosets as Skinnable[], readSkins(kept));
  return model;
}

/**
 * For an HD model, each geoset's vertices where the animation's first frame puts them; undefined for a Classic model,
 * whose bind pose is the way it stands.
 */
function skinnedPose(
  model: Model,
  renderer: ModelRenderer,
): ((geoset: number) => Float32Array | undefined) | undefined {
  if (!model.Geosets.some((geoset) => geoset.SkinWeights?.length)) return undefined;
  // war3-model works out its bones' matrices in update; with no time passed that is the animation's first frame.
  renderer.update(0);
  const nodes = (renderer as unknown as { rendererData?: { nodes?: { matrix: Float32Array }[] } })
    .rendererData?.nodes;
  if (!nodes) return undefined;
  return (index) => {
    const geoset = model.Geosets[index];
    return geoset && posed(geoset, (bone) => nodes[bone]?.matrix);
  };
}

/** The paths the flat normal and ORM maps are given, for the layers that have none of their own. */
const flatPaths = { normal: "moonwell:flat-normal", orm: "moonwell:flat-orm" } as const;

/** The flat maps' texture numbers, and the materials that are drawn with them. */
type MapsFill = { normal: number; orm: number; materials: number[] };

/**
 * For the classic materials of an HD model (a layer whose shader is not the HD one, with no normal or ORM layer of its
 * own): the flat normal and ORM maps, added to the model's textures, and which materials take them. war3-model draws
 * such a material with its HD shader, which takes the second and third layer's textures as those maps, and throws when
 * there are none: the Footman's material 7, which only Decay Flesh shows, stopped the drawing (Sven, 2026-10-28). Null
 * when the model has no such material.
 */
function flatFill(model: Model): MapsFill | null {
  const hd = model.Geosets.some((geoset) => geoset.SkinWeights?.length);
  const materials = model.Materials.flatMap((material, index) =>
    material.Layers[0]?.ShaderTypeId !== 1 && material.Layers.length < 3 ? [index] : [],
  );
  if (!hd || materials.length === 0) return null;
  const normal = model.Textures.length;
  model.Textures.push(
    { Image: flatPaths.normal, ReplaceableId: 0 } as Model["Textures"][number],
    { Image: flatPaths.orm, ReplaceableId: 0 } as Model["Textures"][number],
  );
  return { normal, orm: normal + 1, materials };
}

/**
 * Gives the classic materials the flat maps as their second and third layers' textures for one draw, and returns what
 * puts them back: war3-model's update reads every entry as a layer of the material, which those have not.
 */
function fillMaps(renderer: ModelRenderer, maps: MapsFill): () => void {
  const ids = (renderer as unknown as { rendererData?: { materialLayerTextureID?: number[][] } })
    .rendererData?.materialLayerTextureID;
  const lengths = maps.materials.map((index) => ids?.[index]?.length ?? 0);
  for (const index of maps.materials) {
    const layers = ids?.[index];
    if (!layers) continue;
    layers[1] ??= maps.normal;
    layers[2] ??= maps.orm;
  }
  return () => {
    maps.materials.forEach((index, at) => {
      const layers = ids?.[index];
      if (layers) layers.length = lengths[at] ?? layers.length;
    });
  };
}

function sequenceExtent(model: Model, index: number): Extent | undefined {
  const sequence = model.Sequences[index];
  return sequence && { min: sequence.MinimumExtent, max: sequence.MaximumExtent };
}

/** The element's background as GL's 0-to-1 numbers, so the canvas matches the theme; black when it has none. */
function clearColour(element: HTMLElement): [number, number, number] {
  const match = getComputedStyle(element).backgroundColor.match(/[\d.]+/g);
  if (!match || match.length < 3) return [0, 0, 0];
  return [Number(match[0]) / 255, Number(match[1]) / 255, Number(match[2]) / 255];
}

/**
 * Adds light to war3-model's shaders for classic models, which it draws unlit: a light from the camera's upper left,
 * on normals turned by each vertex's first bone. Brightens its HD shader, which lights a model by one lamp (here at the
 * camera) with almost no light elsewhere, so an HD model stands about as bright as a classic one. It rewrites the
 * shader text as the renderer hands it to this context; a war3-model whose shaders read differently is drawn as it
 * draws it.
 */
function lightShaders(gl: WebGLRenderingContext | WebGL2RenderingContext): void {
  const source = gl.shaderSource.bind(gl);
  gl.shaderSource = (shader, text) => source(shader, lit(text));
}

function lit(text: string): string {
  if (text.includes("uniform sampler2D uOrmSampler;")) return brightHd(text);
  if (text.includes("#version 300 es") || text.includes("aSkin")) return text;
  if (text.includes("attribute vec3 aNormal;") && text.includes("vNormal = aNormal;")) {
    const turned = text.includes("attribute vec4 aGroup;")
      ? "uNodesMatrices[int(aGroup[0])] * vec4(aNormal, 0.0)"
      : "vec4(aNormal, 0.0)";
    return text.replace("vNormal = aNormal;", `vNormal = (uMVMatrix * ${turned}).xyz;`);
  }
  const test = "// hand-made alpha-test";
  if (
    text.includes("varying vec3 vNormal;") &&
    text.includes("uReplaceableType") &&
    text.includes(test)
  ) {
    const shade = [
      "float shade() {",
      "    if (dot(vNormal, vNormal) < 1e-6) return 1.0;",
      "    vec3 light = normalize(vec3(-0.4, 0.5, 0.75));",
      "    return 0.6 + 0.55 * max(dot(normalize(vNormal), light), 0.0);",
      "}",
      "",
      "void main(void) {",
    ].join("\n");
    return text
      .replace("void main(void) {", shade)
      .replace(test, `gl_FragColor.rgb *= shade();\n\n    ${test}`);
  }
  return text;
}

/**
 * The HD fragment shader with more light: the lamp's light about the strength of a classic model's, an even light
 * from all round (war3-model's is 3 in 100) in place of the sky it would reflect, and no tone mapping, which dulls
 * the colours. Unchanged unless all three lines are found.
 */
function brightHd(text: string): string {
  const lines: [RegExp, string][] = [
    [/vec3 radiance = uLightColor;/, "vec3 radiance = uLightColor * 2.2;"],
    [/vec3 ambient = vec3\(\.03\);/, "vec3 ambient = vec3(.4);"],
    [/color = color \/ \(vec3\(1\.\) \+ color\);/, ""],
  ];
  if (!lines.every(([line]) => line.test(text))) return text;
  return lines.reduce((out, [line, instead]) => out.replace(line, instead), text);
}

function message(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
</script>

<div class="flex shrink-0 flex-col gap-1.5" style="width: {size}px">
  <div
    bind:this={host}
    class="relative shrink-0 overflow-hidden rounded border border-line bg-bg"
    style="width: {size}px; height: {size}px"
  >
    {#if status === "loading"}
      <p class="absolute inset-0 m-0 flex items-center justify-center text-xs text-faint">{t("model.loading")}</p>
    {:else if status === "error"}
      <p
        class="absolute inset-0 m-0 flex items-center justify-center p-3 text-center text-xs text-warn"
        role="alert"
      >
        {error}
      </p>
    {:else if missing.length > 0}
      <p
        class="absolute right-0 bottom-0 m-0 truncate px-2 py-1 text-[11px] text-faint"
        title={missing.join("\n")}
      >
        {tCount("model.texturesMissing", missing.length)}
      </p>
    {/if}
    {#if status === "ready" && (!turning || lifted)}
      <div class="absolute bottom-1.5 left-1.5 flex gap-1">
        {#if !turning}
          {@render overlayButton(t("model.turnAgain"), RotateCw, () => (turning = true))}
        {/if}
        {#if lifted}
          {@render overlayButton(t("model.lower"), MoveVertical, () => current?.lower())}
        {/if}
      </div>
    {/if}
  </div>
  <!-- Always there, also while the model loads or when it has none, so the panel keeps its height. -->
  {#if animations}
    <Select
      options={sequences.map((name, index) => ({ value: String(index), label: name || t("model.unnamed", { index }) }))}
      value={String(playing)}
      label={t("model.animation")}
      missing={status === "loading" ? t("model.animationsLoading") : t("model.noAnimations")}
      tone="border-line bg-raised text-fg hover:border-muted"
      compact
      disabled={sequences.length === 0}
      onpick={(value) => current?.play(Number(value))}
    />
  {/if}
  {#if picker}
    <div class="grid grid-cols-12 gap-0.5" role="radiogroup" aria-label={t("model.teamColour")}>
      {#each teamColors as colour, index (colour.name)}
        {@const chosen = playerIndex(teamColor) === index}
        <button
          type="button"
          class="aspect-square rounded-sm border {chosen
            ? 'relative z-10 border-bg outline-2 outline-fg'
            : 'border-line hover:border-muted'}"
          style="background-color: {colour.hex}"
          title={t("model.playerTitle", { player: index + 1, colour: colour.name })}
          aria-label={t("model.playerLabel", { player: index + 1, colour: colour.name })}
          role="radio"
          aria-checked={chosen}
          onclick={() => (teamColor = index)}
        ></button>
      {/each}
    </div>
  {/if}
</div>

{#snippet overlayButton(label: string, Icon: typeof RotateCw, onclick: () => void)}
  <button
    type="button"
    class="cursor-pointer rounded-full bg-surface/70 p-1.5 text-muted opacity-70 hover:text-fg hover:opacity-100"
    title={label}
    aria-label={label}
    {onclick}
  >
    <Icon size={14} />
  </button>
{/snippet}
