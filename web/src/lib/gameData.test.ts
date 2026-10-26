import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { call } from "./bridge.ts";
import { chooseGameData, gameData, newer, viewOf } from "./gameData.ts";

describe("viewOf", () => {
  it("is missing with no folder, loading with a folder, failed with an error, ready once loaded", () => {
    expect(viewOf({ source: null, loaded: false, error: null, version: 0 })).toBe("missing");
    expect(viewOf({ source: "C:data", loaded: false, error: null, version: 1 })).toBe("loading");
    expect(viewOf({ source: "C:data", loaded: false, error: "no such folder", version: 2 })).toBe(
      "failed",
    );
    expect(viewOf({ source: "C:data", loaded: true, error: null, version: 2 })).toBe("ready");
  });
});

describe("newer", () => {
  const loading = { source: "C:data", loaded: false, error: null, version: 1 };
  const loaded = { source: "C:data", loaded: true, error: null, version: 2 };

  it("takes the first state, and a state as new or newer than the one it has", () => {
    expect(newer(null, loading)).toBe(loading);
    expect(newer(loading, loaded)).toBe(loaded);
    expect(newer(loaded, { ...loaded })).toEqual(loaded);
  });

  it("keeps the state it has when an older one arrives late", () => {
    // The reply to gameData() was on its way when the "loaded" event came.
    expect(newer(loaded, loading)).toBe(loaded);
  });
});

describe("the commands", () => {
  let sent: { id: number; command: string }[];
  let listener: ((event: { data: string }) => void) | undefined;

  beforeEach(() => {
    sent = [];
    listener = undefined;
    vi.stubGlobal("window", {
      chrome: {
        webview: {
          addEventListener: (_type: string, handler: typeof listener) => {
            listener = handler;
          },
          postMessage: (message: string) => sent.push(JSON.parse(message)),
        },
      },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("gameData and chooseGameData call the app's commands of those names", async () => {
    const state = { source: "C:data", loaded: false, error: null, version: 1 };
    const first = gameData();
    const second = chooseGameData();

    expect(sent.map((request) => request.command)).toEqual(["gameData", "chooseGameData"]);
    for (const request of sent) {
      listener?.({ data: JSON.stringify({ id: request.id, ok: true, result: state }) });
    }

    await expect(first).resolves.toEqual(state);
    await expect(second).resolves.toEqual(state);
  });

  it("a failed call carries the app's error code", async () => {
    const pending = call("needsData");
    listener?.({
      data: JSON.stringify({
        id: sent[0]?.id,
        ok: false,
        error: { code: "gameDataLoading", message: "still loading" },
      }),
    });

    await expect(pending).rejects.toMatchObject({
      code: "gameDataLoading",
      message: "still loading",
    });
  });
});
