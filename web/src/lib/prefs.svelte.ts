/**
 * The editor's own switches (Sven, 2026-10-27), remembered in the window's storage: whether the editor asks before
 * reloading, deleting or dropping unsaved changes (Sven, 2026-10-29: off asks nothing at all), the UI scale (the window scales the page; Ctrl and the wheel do not), whether the alpha notice opens at start
 * (Sven, 2026-10-28: only Settings turns it off), whether the experimental features are on (Sven, 2026-10-29: the
 * table view; off at first), and the team colour models are shown in at first (Sven, 2026-10-29: the side panel's and
 * the model picker's; player 1, Red, at first).
 */

import { playerIndex } from "./teamColors.ts";

const storageKey = "moonwell-editor.prefs";

type Prefs = {
  confirm: boolean;
  uiScale: number;
  alphaNotice: boolean;
  experimental: boolean;
  teamColor: number;
};

/** The UI scales the settings offer, in percent, as the app takes them. */
export const uiScales = [80, 100, 120];

// Where the notice's own "Don't show this again" was kept before it moved to Settings.
const oldAlphaKey = "moonwell-editor.alpha-understood";

function saved(): Prefs {
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey) ?? "{}") as Partial<Prefs>;
    return {
      // Kept as confirmReload before it covered every question.
      confirm: (stored.confirm ?? (stored as { confirmReload?: boolean }).confirmReload) !== false,
      uiScale: uiScales.includes(stored.uiScale ?? 100) ? (stored.uiScale ?? 100) : 100,
      alphaNotice: stored.alphaNotice ?? localStorage.getItem(oldAlphaKey) !== "yes",
      experimental: stored.experimental === true,
      teamColor: playerIndex(stored.teamColor ?? 0),
    };
  } catch {
    return {
      confirm: true,
      uiScale: 100,
      alphaNotice: true,
      experimental: false,
      teamColor: 0,
    };
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
