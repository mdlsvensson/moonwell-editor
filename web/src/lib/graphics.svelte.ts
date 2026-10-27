/**
 * The graphics mode the editor shows (Sven, 2026-10-27), as World Editor's: Classic (`sd`), Reforged (`hd`) or
 * Definitive Edition (`de`, the default). It picks the icons and models (each mode's own files first) and the values
 * the game keeps per mode (the Priest's icon is BTNPriest in Classic, BTNPriestV1 otherwise). Remembered in the
 * window's storage.
 */
import type { FieldView, ObjectView } from "./api.ts";
import { t } from "./text.ts";

export type GraphicsMode = "sd" | "hd" | "de";

/** The modes with World Editor's names, in its order. */
export const graphicsModes: { mode: GraphicsMode; name: string }[] = [
  { mode: "sd", name: t("graphics.sd") },
  { mode: "hd", name: t("graphics.hd") },
  { mode: "de", name: t("graphics.de") },
];

const storageKey = "moonwell-editor.graphics";

function saved(): GraphicsMode {
  try {
    const value = localStorage.getItem(storageKey);
    if (value === "sd" || value === "hd" || value === "de") return value;
  } catch {
    // The default, below.
  }
  return "de";
}

/** The mode on the page; reading it in a template follows its changes. */
export const graphics = $state<{ mode: GraphicsMode }>({ mode: saved() });

/** Shows another mode, and remembers it. */
export function setGraphicsMode(mode: GraphicsMode): void {
  graphics.mode = mode;
  try {
    localStorage.setItem(storageKey, mode);
  } catch {
    // Not remembered: the next start shows the default.
  }
}

/** A field as the mode shows it: a standard value the game keeps per mode is the mode's. */
export function fieldInMode(field: FieldView, mode: GraphicsMode): FieldView {
  const own = field.modes?.[mode];
  if (own === undefined) return field;
  return field.changed ? { ...field, base: own } : { ...field, value: own, base: own };
}

/** An object as the mode shows it. */
export function viewInMode(view: ObjectView, mode: GraphicsMode): ObjectView {
  return view.fields.some((field) => field.modes)
    ? { ...view, fields: view.fields.map((field) => fieldInMode(field, mode)) }
    : view;
}
