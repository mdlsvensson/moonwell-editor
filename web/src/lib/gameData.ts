import { call } from "./bridge.ts";

/**
 * Where the game data stands in the app: the folder in use, whether it is loaded, and why it failed. `version` grows
 * each time the state changes in the app.
 */
export type GameDataState = {
  source: string | null;
  loaded: boolean;
  error: string | null;
  version: number;
};

/** What the first-start screen or the status bar should say about the game data. */
export type GameDataView = "missing" | "loading" | "failed" | "ready";

/** Asks the app where the game data stands. */
export function gameData(): Promise<GameDataState> {
  return call<GameDataState>("gameData");
}

/** Opens the app's folder dialog; the app loads the folder chosen and answers with the new state. */
export function chooseGameData(): Promise<GameDataState> {
  return call<GameDataState>("chooseGameData");
}

/**
 * The state to keep: `next`, unless it is older than `current`. A reply can be on its way while a newer event comes
 * (a "loading" answer after the "loaded" event), and must not undo it.
 */
export function newer(current: GameDataState | null, next: GameDataState): GameDataState {
  return current && next.version < current.version ? current : next;
}

/** Sorts a state into the one thing the page shows. */
export function viewOf(state: GameDataState): GameDataView {
  if (state.loaded) return "ready";
  if (state.error) return "failed";
  return state.source ? "loading" : "missing";
}
