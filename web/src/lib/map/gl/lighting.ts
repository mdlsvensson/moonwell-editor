/**
 * How the view lights the ground, cliffs and models by the tileset's light at noon (DayLights on the app's side).
 * Classic is the game's fixed-function lighting: the texture times the ambient light plus the light's colour by how
 * much the surface faces it, at most 1. HD and Definitive Edition work in linear colour, with the environment map's
 * sky as the ambient light, each face's colour by how much the surface faces it; the sun and sky are scaled so the
 * ground and models look as World Editor's do (Sven's screenshots of Lost Temple, 2026-10-29: the flat ground about 0.8
 * of its texture in sRGB, an ice troll brighter than that). The scales are the view's call, not the game's.
 */
import type { SceneLight } from "../scene.ts";

/** What the light is for: the terrain (ground and cliffs) or the units (every placed thing's model). */
export type Lit = "terrain" | "units";

/** How much of the light's colour and of the sky's HD and Definitive Edition take, by what is lit (by eye). */
export const scales: Record<Lit, { sun: number; sky: number }> = {
  terrain: { sun: 0.35, sky: 0.45 },
  units: { sun: 0.55, sky: 0.6 },
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
  };
}

/** The uniforms `litGlsl` reads. */
export const lightingUniforms = ["lightToward", "lightColour", "lightAmbient", "sky", "pbr"];

/** `vec3 lit(vec3 rgb, vec3 normal)`: an sRGB colour lit on a surface facing `normal`. */
export const litGlsl = `
uniform vec3 lightToward;
uniform vec3 lightColour;
uniform vec3 lightAmbient;
uniform vec3 sky[6];
uniform bool pbr;
vec3 lit(vec3 rgb, vec3 normal) {
  vec3 n = normalize(normal);
  float facing = max(dot(n, lightToward), 0.0);
  if (!pbr) return rgb * clamp(lightAmbient + lightColour * facing, 0.0, 1.0);
  vec3 n2 = n * n;
  vec3 ambient = n2.x * (n.x >= 0.0 ? sky[0] : sky[1]) + n2.y * (n.y >= 0.0 ? sky[2] : sky[3])
    + n2.z * (n.z >= 0.0 ? sky[4] : sky[5]);
  vec3 light = lightColour * facing + ambient + lightAmbient;
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
}
