/**
 * The boxes that hold a value typed and not sent yet. Saving, opening another map, reloading and closing send them
 * first (`sendTyped`), without relying on the box losing the focus: a page whose window is not in front gets no blur.
 */
const boxes = new Set<() => void>();

/** A box holds a typed value; `send` sends it. Returns the function to call once it no longer does. */
export function typing(send: () => void): () => void {
  boxes.add(send);
  return () => {
    boxes.delete(send);
  };
}

/** True when a box holds a typed value not sent yet (closing the window then asks, even with nothing unsaved). */
export function hasTyped(): boolean {
  return boxes.size > 0;
}

/** Sends every typed value not sent yet. Each goes on the edit queue at once, before what is asked after this. */
export function sendTyped(): void {
  for (const send of [...boxes]) send();
}
