/**
 * Pick mode: the trigger editor asks the map for one kind of thing (a unit, a region, a point ...); the map view
 * shows, a click on that kind answers, Esc answers null. An ask that comes while another waits answers the first null.
 * A store, so the view and the app follow it.
 */
import { get, writable } from "svelte/store";
import type { Pick, PickKind } from "./picking.ts";

type Asking = { kind: PickKind | null };

const state = writable<Asking>({ kind: null });
let waiting: ((pick: Pick | null) => void) | null = null;

/** What the map is asked for: `kind` null when nothing is. */
export const picking = { subscribe: state.subscribe };

/** Asks the map for a thing of `kind`; resolves with the click, or null when cancelled. */
export function pickOnMap(kind: PickKind): Promise<Pick | null> {
  answer(null);
  state.set({ kind });
  return new Promise((resolve) => {
    waiting = resolve;
  });
}

/** Answers the ask waiting, if any: the pick, or null to cancel. */
export function answer(pick: Pick | null): void {
  const resolve = waiting;
  waiting = null;
  if (get(state).kind !== null) state.set({ kind: null });
  resolve?.(pick);
}
