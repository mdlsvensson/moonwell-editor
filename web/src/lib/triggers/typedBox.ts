/**
 * A box whose text is sent when it changes, and also when a save, a reload or closing asks for what is typed (`typing`),
 * as a window that is not in front gets no blur. Used as a Svelte action: `use:typedBox={(value) => ...}`.
 */
import { typing } from "../typing.ts";

type Box = HTMLInputElement | HTMLTextAreaElement;

export function typedBox(
  box: Box,
  send: (value: string) => void,
): { update(next: (value: string) => void): void; destroy(): void } {
  let current = send;
  let release: (() => void) | null = null;

  const flush = (): void => {
    if (!release) return;
    release();
    release = null;
    current(box.value);
  };
  const oninput = (): void => {
    release ??= typing(flush);
  };

  box.addEventListener("input", oninput);
  box.addEventListener("change", flush);
  return {
    update(next) {
      current = next;
    },
    destroy() {
      box.removeEventListener("input", oninput);
      box.removeEventListener("change", flush);
      flush();
    },
  };
}
