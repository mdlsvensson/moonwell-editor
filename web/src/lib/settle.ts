import { BridgeError } from "./bridge.ts";

/** What the user picked in the unsaved-changes dialog. */
export type Choice = "save" | "discard" | "cancel";

/**
 * How the unsaved edits were settled: there were none (`clean`), the user saved them or chose to discard them, or the
 * action stops (the user cancelled, or the save failed).
 */
export type Settled = "clean" | "saved" | "discarded" | "stopped";

/** What `settleUnsaved` needs from the page. */
export type Settling = {
  /** Sends a value still being typed and waits for every edit on its way to answer. */
  flush: () => Promise<unknown>;
  /** Whether the map has unsaved edits, as the app last said. */
  dirty: () => boolean;
  /** Shows the dialog and gives what the user picked. */
  ask: () => Promise<Choice>;
  /** Saves; true when it worked. */
  save: () => Promise<boolean>;
  /** Ask even when the page thinks nothing is unsaved (the window saw the map dirty, or a value was typed). */
  always?: boolean;
};

/**
 * Settles the unsaved edits before an action that would drop them (opening another map, reloading, closing). The
 * edits on their way are waited for first, so an edit typed just before is counted: the app tells the page the map is
 * dirty before it answers the edit.
 */
export async function settleUnsaved(options: Settling): Promise<Settled> {
  await options.flush();
  if (!options.always && !options.dirty()) return "clean";
  const choice = await options.ask();
  if (choice === "cancel") return "stopped";
  if (choice === "discard") return "discarded";
  return (await options.save()) ? "saved" : "stopped";
}

/**
 * Whether the page may tell the app to drop unsaved edits (`discard: true`): only once the user chose Discard, or
 * saved. A page that merely thinks the map is clean does not, so the app refuses when it holds edits the page does not
 * know of (after the page was loaded again).
 */
export function discards(settled: Settled): boolean {
  return settled === "saved" || settled === "discarded";
}

/** What `dropUnsaved` needs besides what `settleUnsaved` does. */
export type Dropping = Settling & {
  /** Does the action; `discard` tells the app it may drop unsaved edits. */
  act: (discard: boolean) => Promise<unknown>;
  /** Learns from the app where the map stands, after it said it has unsaved edits the page did not know of. */
  refresh: () => Promise<unknown>;
};

/**
 * Runs an action that drops the unsaved edits (opening another map, reloading): settles them first, then acts, telling
 * the app to discard them only when the user chose to (or saved). When the app refuses with `unsaved` (it holds edits
 * the page did not know of), the page learns where the map stands and asks the user after all. True when it acted;
 * false when the user cancelled or the save failed. Any other failure is passed on.
 */
export async function dropUnsaved(options: Dropping): Promise<boolean> {
  const settled = await settleUnsaved(options);
  if (settled === "stopped") return false;
  try {
    await options.act(discards(settled));
    return true;
  } catch (error) {
    if (!(error instanceof BridgeError && error.code === "unsaved") || discards(settled))
      throw error;
  }
  await options.refresh();
  const asked = await settleUnsaved({ ...options, always: true });
  if (asked === "stopped") return false;
  await options.act(discards(asked));
  return true;
}
