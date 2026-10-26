/**
 * The page's side of the bridge to the C# app. Requests are `{id, command, args}` strings posted to WebView2;
 * replies are `{id, ok, result | error}` and events are `{event, payload}`, both coming back as strings.
 */

type Webview = NonNullable<NonNullable<Window["chrome"]>["webview"]>;
type Reply = {
  id: number | null;
  ok: boolean;
  result?: unknown;
  error?: { code: string; message: string };
};
type Pending = { resolve: (value: never) => void; reject: (error: Error) => void };

/** A command that failed in the app. `code` is the app's error code, such as `gameDataLoading`. */
export class BridgeError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "BridgeError";
    this.code = code;
  }
}

let nextId = 1;
const pending = new Map<number, Pending>();
const handlers = new Map<string, Set<(payload: unknown) => void>>();
const listening = new WeakSet<Webview>();

function webview(): Webview {
  const found = window.chrome?.webview;
  if (!found) throw new Error("The page is not running inside the Moonwell Editor window.");
  if (!listening.has(found)) {
    listening.add(found);
    found.addEventListener("message", (event) => receive(event.data));
  }
  return found;
}

function receive(data: unknown): void {
  const message = typeof data === "string" ? JSON.parse(data) : data;
  if (message && typeof message === "object" && "event" in message) {
    for (const handler of handlers.get(String(message.event)) ?? []) {
      handler((message as { payload: unknown }).payload);
    }
    return;
  }
  const reply = message as Reply;
  if (reply.id === null || reply.id === undefined) {
    // The app could not read a request (a badRequest), so it cannot say which one: every waiting call fails.
    if (!reply.ok) {
      const error = errorOf(reply);
      for (const waiting of pending.values()) waiting.reject(error);
      pending.clear();
    }
    return;
  }
  const waiting = pending.get(reply.id);
  if (!waiting) return;
  pending.delete(reply.id);
  if (reply.ok) waiting.resolve(reply.result as never);
  else waiting.reject(errorOf(reply));
}

function errorOf(reply: Reply): BridgeError {
  return new BridgeError(
    reply.error?.code ?? "failed",
    reply.error?.message ?? "The app reported an error.",
  );
}

/**
 * Runs a command in the app. Rejects with a BridgeError (the app's code and message) when the command fails, and
 * with an Error when the page is not inside the Moonwell Editor window.
 */
export async function call<T>(command: string, args: object = {}): Promise<T> {
  const channel = webview();
  const id = nextId++;
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: never) => void, reject });
    channel.postMessage(JSON.stringify({ id, command, args }));
  });
}

/** Listens to an event the app sends on its own. Returns a function that stops listening. */
export function on(event: string, handler: (payload: unknown) => void): () => void {
  webview();
  const set = handlers.get(event) ?? new Set();
  set.add(handler);
  handlers.set(event, set);
  return () => {
    set.delete(handler);
  };
}
