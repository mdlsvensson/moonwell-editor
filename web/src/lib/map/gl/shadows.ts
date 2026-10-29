/**
 * The sun's shadows in HD and Definitive Edition (World Editor draws them at noon, Sven's screenshots, 2026-10-29): the
 * ground, cliffs and models drawn again from the sun into a depth texture around the camera's target, before each
 * frame; a surface the sun cannot see from there loses the sun's light (see `litGlsl`). Classic draws none this way.
 */
import { mat4, vec3 } from "gl-matrix";

/** How far either side of the camera's target the shadows reach: more the further the camera is (the view's call). */
export function shadowReach(distance: number): number {
  return Math.min(Math.max(distance * 1.6, 800), 7000);
}

/** How far above the target the sun's view starts, so tall models and cliffs nearer the sun still cast. */
const above = 3000;

/**
 * The sun's view and projection: looking along the light from `toward` (a unit vector toward the sun), square on
 * `target`, `reach` either side of it.
 */
export function sunView(
  target: [number, number, number],
  toward: [number, number, number],
  reach: number,
): mat4 {
  const depth = above + reach;
  const eye = vec3.scaleAndAdd(vec3.create(), target, toward, depth);
  // Up is north unless the sun looks straight along it.
  const up: [number, number, number] = Math.abs(toward[1]) > 0.99 ? [1, 0, 0] : [0, 1, 0];
  const look = mat4.lookAt(mat4.create(), eye, target, up);
  const projection = mat4.ortho(mat4.create(), -reach, reach, -reach, reach, 0, depth * 2);
  return mat4.multiply(mat4.create(), projection, look);
}

/** The depth texture the sun's view is drawn into, and its framebuffer. */
export class ShadowMap {
  readonly texture: WebGLTexture;
  readonly size: number;
  private readonly frame: WebGLFramebuffer;

  /** A map `size` texels square; a 1 by 1 map draws nothing and stands in where no map is drawn. */
  constructor(
    private readonly gl: WebGL2RenderingContext,
    size: number,
  ) {
    this.size = Math.min(size, gl.getParameter(gl.MAX_TEXTURE_SIZE) as number);
    this.texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.texture);
    gl.texStorage2D(gl.TEXTURE_2D, 1, gl.DEPTH_COMPONENT24, this.size, this.size);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_MODE, gl.COMPARE_REF_TO_TEXTURE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_FUNC, gl.LEQUAL);
    this.frame = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.frame);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.TEXTURE_2D, this.texture, 0);
    gl.drawBuffers([gl.NONE]);
    gl.readBuffer(gl.NONE);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  }

  /** Starts drawing the sun's view: depth only, pushed back a little so a surface does not shadow itself. */
  begin(): void {
    const { gl } = this;
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.frame);
    gl.viewport(0, 0, this.size, this.size);
    gl.clear(gl.DEPTH_BUFFER_BIT);
    gl.colorMask(false, false, false, false);
    gl.enable(gl.POLYGON_OFFSET_FILL);
    gl.polygonOffset(2, 4);
  }

  end(): void {
    const { gl } = this;
    gl.disable(gl.POLYGON_OFFSET_FILL);
    gl.colorMask(true, true, true, true);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  }

  dispose(): void {
    this.gl.deleteFramebuffer(this.frame);
    this.gl.deleteTexture(this.texture);
  }
}
