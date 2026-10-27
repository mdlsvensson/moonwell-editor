/**
 * The names World Editor shows an icon, a model or a texture by (Sven, 2026-10-27): the objects that use the file,
 * fetched once the game data is ready. `fields.ts` reads them through `setAssetNames`, so a value shown follows them
 * as they arrive.
 */
import { assetNames } from "./api.ts";
import { setAssetNames } from "./fields.ts";

let groups = $state.raw<Record<string, Record<string, string[]>> | null>(null);

setAssetNames((group, key) => groups?.[group]?.[key]);

/** Fetches the names from the app; until they arrive (or when the app has none), files show their paths. */
export async function loadAssetNames(): Promise<void> {
  try {
    groups = await assetNames();
  } catch {
    groups = null;
  }
}
