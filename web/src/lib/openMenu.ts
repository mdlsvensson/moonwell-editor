/** The "Open map…" menu: a map is a folder or an archive, each picked in its own dialog of the app. */
import { type MapInfo, openMap } from "./api.ts";
import { t } from "./text.ts";

/** One entry of the menu: its label, and whether it asks for an archive (else a folder). */
export type OpenEntry = { label: string; archive: boolean };

/** The menu's entries, in order. */
export const openEntries = [
  { label: t("openMenu.folder"), archive: false },
  { label: t("openMenu.archive"), archive: true },
] as const satisfies readonly OpenEntry[];

/**
 * Opens the map the user picks in the entry's dialog (null when they cancel). `discard` lets the app drop unsaved
 * edits (see `openMap`).
 */
export function openFrom(entry: OpenEntry, discard: boolean): Promise<MapInfo | null> {
  return openMap({ archive: entry.archive, discard });
}
