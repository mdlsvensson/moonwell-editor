/**
 * The map view's camera: it looks at a point from a distance, turned by its yaw (0 looks north, a quarter turn east)
 * and tilted down by its pitch (a quarter turn is straight down). Moving it is pure: each move gives a new view.
 */
import { mat4, vec3, vec4 } from "gl-matrix";

/** Where the camera looks (x, y, z), from how far, and its yaw and pitch in radians. */
export type View = {
  x: number;
  y: number;
  z: number;
  distance: number;
  yaw: number;
  pitch: number;
};

/** The field of view top to bottom (plan's call: World Editor's is not measured). */
const fieldOfView = Math.PI / 3;
const near = 16;
const far = 60000;
const closest = 200;
const furthest = 20000;
/** The pitch stays between straight up and straight down, as Blender's does, short of the poles. */
const steepest = Math.PI / 2 - 0.001;

/** The direction the camera looks in, and its up. */
function axes(view: View): { forward: vec3; up: vec3 } {
  const { yaw, pitch } = view;
  const forward = vec3.fromValues(
    Math.sin(yaw) * Math.cos(pitch),
    Math.cos(yaw) * Math.cos(pitch),
    -Math.sin(pitch),
  );
  // Square to the forward direction, so looking straight down still has an up: the way the camera faces.
  const up = vec3.fromValues(
    Math.sin(yaw) * Math.sin(pitch),
    Math.cos(yaw) * Math.sin(pitch),
    Math.cos(pitch),
  );
  return { forward, up };
}

/** The camera's place. */
export function eye(view: View): vec3 {
  const { forward } = axes(view);
  return vec3.scaleAndAdd(vec3.create(), [view.x, view.y, view.z], forward, -view.distance);
}

/** The view and projection matrices for a canvas of `width` by `height`. */
export function matrices(
  view: View,
  width: number,
  height: number,
): { view: mat4; projection: mat4 } {
  const { up } = axes(view);
  return {
    view: mat4.lookAt(mat4.create(), eye(view), [view.x, view.y, view.z], up),
    projection: mat4.perspective(
      mat4.create(),
      fieldOfView,
      width / Math.max(height, 1),
      near,
      far,
    ),
  };
}

/** The ray from the camera through the pixel at `px`, `py` (from the canvas's top left). */
export function ray(
  view: View,
  width: number,
  height: number,
  px: number,
  py: number,
): { origin: vec3; direction: vec3 } {
  const { view: look, projection } = matrices(view, width, height);
  const inverse = mat4.invert(mat4.create(), mat4.multiply(mat4.create(), projection, look));
  const x = (2 * px) / width - 1;
  const y = 1 - (2 * py) / height;
  const unproject = (z: number) => {
    const point = vec4.transformMat4(vec4.create(), [x, y, z, 1], inverse);
    const w = point[3] ?? 1;
    return vec3.fromValues((point[0] ?? 0) / w, (point[1] ?? 0) / w, (point[2] ?? 0) / w);
  };
  const origin = unproject(-1);
  const direction = vec3.normalize(
    vec3.create(),
    vec3.subtract(vec3.create(), unproject(1), origin),
  );
  return { origin, direction };
}

/** The view moved along the ground: `right` to the camera's right, `ahead` the way it faces. */
export function panned(view: View, right: number, ahead: number): View {
  const sin = Math.sin(view.yaw);
  const cos = Math.cos(view.yaw);
  return { ...view, x: view.x + right * cos + ahead * sin, y: view.y - right * sin + ahead * cos };
}

/** The view nearer by `steps` (a wheel's notch is one), or further for fewer than none. */
export function zoomed(view: View, steps: number): View {
  return {
    ...view,
    distance: Math.min(Math.max(view.distance * 0.88 ** steps, closest), furthest),
  };
}

/** The view turned by `yaw` and tilted by `pitch`, in radians. */
export function turned(view: View, yaw: number, pitch: number): View {
  return {
    ...view,
    yaw: view.yaw + yaw,
    pitch: Math.min(Math.max(view.pitch + pitch, -steepest), Math.PI / 2),
  };
}

/** A middle drag of `dx`, `dy` pixels, as Blender orbits: right turns the view, down tilts it toward the top. */
export function orbited(view: View, dx: number, dy: number): View {
  return turned(view, -dx * 0.005, dy * 0.005);
}

/** Blender's numpad step: 15 degrees an orbit. */
const step = (15 * Math.PI) / 180;

/**
 * The view after a numpad key (`event.code`), as Blender's: 1, 3 and 7 look from the front (north), the right and the
 * top, with Ctrl from the opposite side; 4, 6, 8 and 2 orbit by 15 degrees, with Ctrl pan; 9 turns to the other side;
 * + and - zoom. Null for any other key.
 */
export function numpadView(view: View, code: string, ctrl: boolean): View | null {
  const pan = view.distance * 0.1;
  switch (code) {
    case "Numpad1":
      return { ...view, yaw: ctrl ? Math.PI : 0, pitch: 0 };
    case "Numpad3":
      return { ...view, yaw: ctrl ? Math.PI / 2 : -Math.PI / 2, pitch: 0 };
    case "Numpad7":
      return { ...view, pitch: ctrl ? -steepest : Math.PI / 2 };
    case "Numpad4":
      return ctrl ? panned(view, -pan, 0) : turned(view, -step, 0);
    case "Numpad6":
      return ctrl ? panned(view, pan, 0) : turned(view, step, 0);
    case "Numpad8":
      return ctrl ? panned(view, 0, pan) : turned(view, 0, step);
    case "Numpad2":
      return ctrl ? panned(view, 0, -pan) : turned(view, 0, -step);
    case "Numpad9":
      return { ...view, yaw: view.yaw + Math.PI };
    case "NumpadAdd":
      return zoomed(view, 1);
    case "NumpadSubtract":
      return zoomed(view, -1);
    default:
      return null;
  }
}

/** The view looking at the middle of a rectangle from far enough to see all of it, its angles kept (Blender's Home). */
export function framed(view: View, left: number, bottom: number, right: number, top: number): View {
  const size = Math.max(right - left, top - bottom);
  return {
    ...view,
    x: (left + right) / 2,
    y: (bottom + top) / 2,
    distance: Math.min((size / 2 / Math.tan(fieldOfView / 2)) * 1.1, 60000),
  };
}
