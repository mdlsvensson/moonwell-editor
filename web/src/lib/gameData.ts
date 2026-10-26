import { call } from "./bridge.ts";

/** Where the game data stands in the app: the folder in use, whether it is loaded, and why it failed. */
export type GameDataState = { source: string | null; loaded: boolean; error: string | null };

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

/** Sorts a state into the one thing the page shows. */
export function viewOf(state: GameDataState): GameDataView {
  if (state.loaded) return "ready";
  if (state.error) return "failed";
  return state.source ? "loading" : "missing";
}
