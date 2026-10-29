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
/** The pitch stays between nearly level and straight down. */
const flattest = 0.1;

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
    pitch: Math.min(Math.max(view.pitch + pitch, flattest), Math.PI / 2),
  };
}
