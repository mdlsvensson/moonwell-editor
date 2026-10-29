/**
 * How the view lights the ground, cliffs and models by the tileset's light at noon (DayLights on the app's side).
 * Classic is the game's fixed-function lighting: the texture times the ambient light plus the light's colour by how
 * much the surface faces it, at most 1. HD and Definitive Edition work in linear colour, with the environment map's
 * sky as the ambient light, each face's colour by how much the surface faces it; the sun and sky are scaled so the
 * ground and models look as World Editor's do (Sven's screenshots of Lost Temple, 2026-10-29: the flat ground about 0.8
 * of its texture in sRGB, an ice troll brighter than that, a shadow about a fifth of the lit ground's light and bluer).
 * The scales are the view's call, not the game's.
 */
import type { mat4 } from "gl-matrix";
import type { SceneLight } from "../scene.ts";

/** The sun's shadow map (see shadows.ts) and the sun's view it was drawn in; `on` false draws no shadows. */
export type Shadow = { texture: WebGLTexture; matrix: mat4; on: boolean };

/** What the light is for: the terrain (ground and cliffs) or the units (every placed thing's model). */
export type Lit = "terrain" | "units";

/** How much of the light's colour and of the sky's HD and Definitive Edition take, by what is lit (by eye). */
export const scales: Record<Lit, { sun: number; sky: number }> = {
  terrain: { sun: 0.57, sky: 0.17 },
  units: { sun: 0.75, sky: 0.4 },
};

/** The light as the shaders take it. */
export type Lighting = {
  /** Toward the light, a unit vector. */
  toward: [number, number, number];
  colour: [number, number, number];
  ambient: [number, number, number];
  /** The sky along +x, -x, +y, -y, +z and -z, linear and scaled; zeros in Classic. */
  sky: Float32Array;
  /** Whether to light in linear colour by the sky (HD and Definitive Edition). */
  pbr: boolean;
  /** The sun's shadows, set for each frame; null before the first. */
  shadow: Shadow | null;
};

const linear = (srgb: number) => srgb ** 2.2;

/** A light from the scene for what it lights, with the sky (18 sRGB numbers) when the mode has one. */
export function lighting(light: SceneLight, sky: readonly number[], lit: Lit): Lighting {
  const pbr = sky.length === 18;
  const length = Math.hypot(...light.toward) || 1;
  const { sun: sunScale, sky: skyScale } = scales[lit];
  const scale = pbr ? sunScale : 1;
  return {
    toward: light.toward.map((part) => part / length) as [number, number, number],
    colour: light.colour.map((part) => part * scale) as [number, number, number],
    ambient: [...light.ambient] as [number, number, number],
    sky: pbr ? Float32Array.from(sky, (part) => linear(part) * skyScale) : new Float32Array(18),
    pbr,
    shadow: null,
  };
}

/** The uniforms `litGlsl` reads. */
export const lightingUniforms = [
  "lightToward",
  "lightColour",
  "lightAmbient",
  "sky",
  "pbr",
  "shadowMap",
  "shadowMatrix",
  "shadowed",
];

/** The texture unit the shadow map is bound to, in every program that lights. */
const shadowUnit = 7;

/**
 * `vec3 lit(vec3 rgb, vec3 normal, vec3 world)`: an sRGB colour lit on a surface at `world` facing `normal`;
 * `vec3 skyAt(vec3 d)`: the sky seen along a unit vector, linear (each face's colour by how much `d` points at it);
 * `float sunlit(vec3 world)`: how much of the sun reaches a point, 0 in shadow, 1 in the sun or outside the map.
 */
export const litGlsl = `
precision highp sampler2DShadow;
uniform vec3 lightToward;
uniform vec3 lightColour;
uniform vec3 lightAmbient;
uniform vec3 sky[6];
uniform bool pbr;
uniform sampler2DShadow shadowMap;
uniform mat4 shadowMatrix;
uniform bool shadowed;
float sunlit(vec3 world) {
  if (!shadowed) return 1.0;
  vec4 p = shadowMatrix * vec4(world, 1.0);
  vec3 s = p.xyz / p.w * 0.5 + 0.5;
  if (any(lessThan(s, vec3(0.0))) || any(greaterThan(s, vec3(1.0)))) return 1.0;
  vec2 texel = 1.0 / vec2(textureSize(shadowMap, 0));
  float sum = 0.0;
  for (int x = -1; x <= 1; x++) {
    for (int y = -1; y <= 1; y++) sum += texture(shadowMap, vec3(s.xy + vec2(x, y) * texel, s.z - 0.0004));
  }
  return sum / 9.0;
}
vec3 skyAt(vec3 d) {
  vec3 d2 = d * d;
  return d2.x * (d.x >= 0.0 ? sky[0] : sky[1]) + d2.y * (d.y >= 0.0 ? sky[2] : sky[3])
    + d2.z * (d.z >= 0.0 ? sky[4] : sky[5]);
}
vec3 lit(vec3 rgb, vec3 normal, vec3 world) {
  vec3 n = normalize(normal);
  float facing = max(dot(n, lightToward), 0.0) * sunlit(world);
  if (!pbr) return rgb * clamp(lightAmbient + lightColour * facing, 0.0, 1.0);
  vec3 light = lightColour * facing + skyAt(n) + lightAmbient;
  return pow(pow(rgb, vec3(2.2)) * light, vec3(1.0 / 2.2));
}`;

/** Sets `litGlsl`'s uniforms on the program in use. */
export function setLighting(
  gl: WebGL2RenderingContext,
  uniforms: Record<string, WebGLUniformLocation | null>,
  light: Lighting,
): void {
  gl.uniform3fv(uniforms.lightToward ?? null, light.toward);
  gl.uniform3fv(uniforms.lightColour ?? null, light.colour);
  gl.uniform3fv(uniforms.lightAmbient ?? null, light.ambient);
  gl.uniform3fv(uniforms.sky ?? null, light.sky);
  gl.uniform1i(uniforms.pbr ?? null, light.pbr ? 1 : 0);
  // A sampler of shadows must always have a depth map to read, so a light without shadows keeps its stand-in bound.
  gl.uniform1i(uniforms.shadowed ?? null, light.shadow?.on ? 1 : 0);
  gl.uniform1i(uniforms.shadowMap ?? null, shadowUnit);
  if (light.shadow) {
    gl.uniformMatrix4fv(uniforms.shadowMatrix ?? null, false, light.shadow.matrix);
    gl.activeTexture(gl.TEXTURE0 + shadowUnit);
    gl.bindTexture(gl.TEXTURE_2D, light.shadow.texture);
  }
}
