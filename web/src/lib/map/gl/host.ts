/**
 * The map view's WebGL2 context and what is made on it. The graphics card can take the context away (a driver reset,
 * the GPU process ending); the browser gives it back once asked for it, and then everything is made again from the
 * scene the page kept, so the view does not stay blank (Review Focus 5).
 */

/** Makes everything on a context; returns what drops it again. */
export type Build = (gl: WebGL2RenderingContext) => () => void;

export class GlHost {
  private drop: (() => void) | null = null;
  private stopped = false;
  /** The context in use; null while it is lost. */
  gl: WebGL2RenderingContext | null = null;

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly build: Build,
  ) {
    canvas.addEventListener("webglcontextlost", this.lost);
    canvas.addEventListener("webglcontextrestored", this.restored);
    this.make();
  }

  /** Drops everything and stops making it again. */
  stop(): void {
    this.stopped = true;
    this.canvas.removeEventListener("webglcontextlost", this.lost);
    this.canvas.removeEventListener("webglcontextrestored", this.restored);
    this.drop?.();
    this.drop = null;
    this.gl = null;
  }

  private make(): void {
    const gl = this.canvas.getContext("webgl2", {
      antialias: true,
    }) as WebGL2RenderingContext | null;
    if (!gl) return;
    this.gl = gl;
    this.drop = this.build(gl);
  }

  private readonly lost = (event: Event): void => {
    // Without this the browser does not give the context back.
    event.preventDefault();
    this.drop?.();
    this.drop = null;
    this.gl = null;
  };

  private readonly restored = (): void => {
    if (!this.stopped) this.make();
  };
}
