import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { call } from "./bridge.ts";
import { chooseGameData, gameData, viewOf } from "./gameData.ts";

describe("viewOf", () => {
  it("is missing with no folder, loading with a folder, failed with an error, ready once loaded", () => {
    expect(viewOf({ source: null, loaded: false, error: null })).toBe("missing");
    expect(viewOf({ source: "C:data", loaded: false, error: null })).toBe("loading");
    expect(viewOf({ source: "C:data", loaded: false, error: "no such folder" })).toBe("failed");
    expect(viewOf({ source: "C:data", loaded: true, error: null })).toBe("ready");
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
    const state = { source: "C:data", loaded: false, error: null };
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
