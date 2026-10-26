/** What the user picked in the unsaved-changes dialog. */
export type Choice = "save" | "discard" | "cancel";

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
  /** Ask even when the page thinks nothing is unsaved (the window saw the map dirty when it was closed). */
  always?: boolean;
};

/**
 * Settles the unsaved edits before an action that would drop them (opening another map, reloading, closing): true
 * when the action may go on (nothing unsaved, saved, or the user discards them), false when the user cancels or the
 * save failed. The edits on their way are waited for first, so an edit typed just before is counted: the app tells
 * the page the map is dirty before it answers the edit.
 */
export async function settleUnsaved(options: Settling): Promise<boolean> {
  await options.flush();
  if (!options.always && !options.dirty()) return true;
  const choice = await options.ask();
  if (choice === "cancel") return false;
  if (choice === "discard") return true;
  return options.save();
}
