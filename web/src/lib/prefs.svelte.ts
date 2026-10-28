/**
 * The editor's own switches (Sven, 2026-10-27), remembered in the window's storage: whether the object tree's icons
 * get a frame in the theme's colours over the game's own icon border, whether a reload from disk asks first, the UI
 * scale (the window scales the page; Ctrl and the wheel do not), and whether the alpha notice opens at start (Sven,
 * 2026-10-28: only Settings turns it off).
 */

const storageKey = "moonwell-editor.prefs";

type Prefs = { iconFrames: boolean; confirmReload: boolean; uiScale: number; alphaNotice: boolean };

/** The UI scales the settings offer, in percent, as the app takes them. */
export const uiScales = [80, 100, 120];

// Where the notice's own "Don't show this again" was kept before it moved to Settings.
const oldAlphaKey = "moonwell-editor.alpha-understood";

function saved(): Prefs {
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey) ?? "{}") as Partial<Prefs>;
    return {
      iconFrames: stored.iconFrames !== false,
      confirmReload: stored.confirmReload !== false,
      uiScale: uiScales.includes(stored.uiScale ?? 100) ? (stored.uiScale ?? 100) : 100,
      alphaNotice: stored.alphaNotice ?? localStorage.getItem(oldAlphaKey) !== "yes",
    };
  } catch {
    return { iconFrames: true, confirmReload: true, uiScale: 100, alphaNotice: true };
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
