import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { call, on } from "./bridge.ts";

type Listener = (event: { data: string }) => void;

/** A stand-in for window.chrome.webview: it records what the page posts and lets a test reply. */
function fakeWebview() {
  const listeners = new Set<Listener>();
  const sent: { id: number; command: string; args: unknown }[] = [];
  return {
    sent,
    addEventListener: (_type: string, listener: Listener) => listeners.add(listener),
    removeEventListener: (_type: string, listener: Listener) => listeners.delete(listener),
    postMessage: (message: string) => sent.push(JSON.parse(message)),
    reply: (message: object) => {
      for (const listener of listeners) listener({ data: JSON.stringify(message) });
    },
  };
}

let webview: ReturnType<typeof fakeWebview>;

beforeEach(() => {
  webview = fakeWebview();
  vi.stubGlobal("window", { chrome: { webview } });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("call", () => {
  it("posts the command and resolves with the result", async () => {
    const pending = call<string>("ping", { x: 1 });
    expect(webview.sent).toHaveLength(1);
    expect(webview.sent[0]).toMatchObject({ command: "ping", args: { x: 1 } });

    webview.reply({ id: webview.sent[0]?.id, ok: true, result: "pong" });

    await expect(pending).resolves.toBe("pong");
  });

  it("rejects with the error message when ok is false", async () => {
    const pending = call("boom");
    webview.reply({
      id: webview.sent[0]?.id,
      ok: false,
      error: { code: "failed", message: "it broke" },
    });

    await expect(pending).rejects.toThrow("it broke");
  });

  it("matches replies by id when they arrive out of order", async () => {
    const first = call<string>("a");
    const second = call<string>("b");
    const [a, b] = webview.sent;

    webview.reply({ id: b?.id, ok: true, result: "from b" });
    webview.reply({ id: a?.id, ok: true, result: "from a" });

    await expect(first).resolves.toBe("from a");
    await expect(second).resolves.toBe("from b");
  });
});

describe("on", () => {
  it("receives the payload of its event and stops after unsubscribing", () => {
    const seen: unknown[] = [];
    const off = on("mapOpened", (payload) => seen.push(payload));

    webview.reply({ event: "mapOpened", payload: { name: "arena" } });
    webview.reply({ event: "other", payload: 1 });
    off();
    webview.reply({ event: "mapOpened", payload: { name: "later" } });

    expect(seen).toEqual([{ name: "arena" }]);
  });
});
