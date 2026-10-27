/**
 * The editor's own look-and-feel switches (Sven, 2026-10-27), remembered in the window's storage: whether the object
 * tree's icons get a frame in the theme's colours over the game's own icon border.
 */

const storageKey = "moonwell-editor.prefs";

type Prefs = { iconFrames: boolean };

function saved(): Prefs {
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey) ?? "{}") as Partial<Prefs>;
    return { iconFrames: stored.iconFrames !== false };
  } catch {
    return { iconFrames: true };
  }
}

/** The switches; reading one in a template follows its changes. */
export const prefs = $state<Prefs>(saved());

/** Sets a switch, and remembers it. */
export function setPref<K extends keyof Prefs>(key: K, value: Prefs[K]): void {
  prefs[key] = value;
  try {
    localStorage.setItem(storageKey, JSON.stringify(prefs));
  } catch {
    // Not remembered: the next start has the default.
  }
}
