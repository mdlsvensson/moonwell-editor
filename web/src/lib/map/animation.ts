/**
 * A model's Stand animation, sampled for the graphics card: each bone's matrix and each geoset's alpha at a fixed rate
 * over the sequence, so the view can play it on every copy of the model at once, each copy at its own point (the
 * game starts each unit's Stand at its own time; the view's call how). Frames are not blended: at 30 a second the
 * steps do not show. A long animation keeps at most 120 frames, spread over all of it (fewer a second), so a map of
 * many models loads in time.
 */

/** Frames a second the view samples an animation at. */
export const sampleRate = 30;
/** The most frames kept of one animation (four seconds at `sampleRate`); a longer one is sampled less often. */
const mostFrames = 120;

/** What sampling needs of a model: its sequence, its bones and geosets, and their state at a time. */
export type Poser = {
  /** The sequence's first and last frame, in milliseconds. */
  start: number;
  end: number;
  bones: number;
  geosets: number;
  /** Poses the model at a time in the sequence. */
  at(time: number): void;
  /** A bone's matrix in the pose, 16 numbers in columns; undefined for one that does not move. */
  matrix(bone: number): ArrayLike<number> | undefined;
  /** A geoset's alpha in the pose, 0 hidden to 1. */
  alpha(geoset: number): number;
};

/** An animation as the shaders read it. */
export type Animation = {
  frames: number;
  /** Frames a second it plays at: `sampleRate`, or fewer for a long one. */
  rate: number;
  bones: number;
  geosets: number;
  /** A frame's bones' matrices, each three rows of four numbers (the fourth row is 0, 0, 0, 1). */
  poses: Float32Array;
  /** A frame's geosets' alphas. */
  alphas: Float32Array;
};

const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

/** Samples a model's sequence from its start, `sampleRate` frames a second. */
export function sampleAnimation(model: Poser): Animation {
  const length = Math.max(model.end - model.start, 0);
  const frames = Math.min(Math.max(Math.ceil((length * sampleRate) / 1000), 1), mostFrames);
  const rate = frames === mostFrames ? (frames * 1000) / length : sampleRate;
  const poses = new Float32Array(frames * model.bones * 12);
  const alphas = new Float32Array(frames * model.geosets);
  for (let frame = 0; frame < frames; frame++) {
    model.at(model.start + (frame * 1000) / rate);
    for (let bone = 0; bone < model.bones; bone++) {
      const m = model.matrix(bone) ?? identity;
      const at = (frame * model.bones + bone) * 12;
      for (let row = 0; row < 3; row++) {
        for (let column = 0; column < 4; column++) {
          poses[at + row * 4 + column] = m[column * 4 + row] ?? 0;
        }
      }
    }
    for (let geoset = 0; geoset < model.geosets; geoset++) {
      alphas[frame * model.geosets + geoset] = model.alpha(geoset);
    }
  }
  return { frames, rate, bones: model.bones, geosets: model.geosets, poses, alphas };
}

/** Whether every frame of an animation is its first: no bone moves and no geoset fades (most trees' Stand). */
export function isStill(animation: Animation): boolean {
  const pose = animation.bones * 12;
  for (let frame = 1; frame < animation.frames; frame++) {
    for (let i = 0; i < pose; i++) {
      if (Math.abs((animation.poses[frame * pose + i] ?? 0) - (animation.poses[i] ?? 0)) > 1e-4)
        return false;
    }
    for (let i = 0; i < animation.geosets; i++) {
      if (animation.alphas[frame * animation.geosets + i] !== animation.alphas[i]) return false;
    }
  }
  return true;
}

/** A frame's bones' matrices, 16 numbers in columns each, as the pose functions take them. */
export function poseAt(animation: Animation, frame: number): (bone: number) => number[] {
  return (bone) => {
    const at = (frame * animation.bones + bone) * 12;
    const m = new Array<number>(16).fill(0);
    m[15] = 1;
    for (let row = 0; row < 3; row++) {
      for (let column = 0; column < 4; column++)
        m[column * 4 + row] = animation.poses[at + row * 4 + column] ?? 0;
    }
    return m;
  };
}
