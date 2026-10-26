import { describe, expect, it } from "vitest";
import { BridgeError } from "./bridge.ts";
import { type Choice, discards, dropUnsaved, settleUnsaved } from "./settle.ts";

/** A page whose map turns dirty only once the edit queued before it has answered, as the app's event arrives. */
function page(choice: Choice, saved = true) {
  const seen: string[] = [];
  let dirty = false;
  return {
    seen,
    flush: async () => {
      seen.push("flush");
      await Promise.resolve();
      dirty = true; // sessionChanged came with the edit's answer
    },
    dirty: () => dirty,
    ask: async () => {
      seen.push("ask");
      return choice;
    },
    save: async () => {
      seen.push("save");
      return saved;
    },
  };
}

describe("settleUnsaved", () => {
  it("waits for the edits on their way before it looks at whether the map is dirty", async () => {
    const options = page("cancel");

    expect(await settleUnsaved(options)).toBe("stopped");
    expect(options.seen).toEqual(["flush", "ask"]);
  });

  it("goes on without asking when nothing is unsaved after the edits on their way", async () => {
    const options = { ...page("cancel"), dirty: () => false };

    expect(await settleUnsaved(options)).toBe("clean");
    expect(options.seen).toEqual(["flush"]);
  });

  it("asks even when the page thinks the map is clean, when told to (the window saw it dirty)", async () => {
    const options = { ...page("discard"), dirty: () => false };

    expect(await settleUnsaved({ ...options, always: true })).toBe("discarded");
    expect(options.seen).toEqual(["flush", "ask"]);
  });

  it("goes on after a save only when the save worked", async () => {
    const worked = page("save", true);
    const failed = page("save", false);

    expect(await settleUnsaved(worked)).toBe("saved");
    expect(await settleUnsaved(failed)).toBe("stopped");
    expect(worked.seen).toEqual(["flush", "ask", "save"]);
  });

  it("goes on after Discard, and not after Cancel", async () => {
    expect(await settleUnsaved(page("discard"))).toBe("discarded");
    expect(await settleUnsaved(page("cancel"))).toBe("stopped");
  });

  it("lets the app drop the edits only after the user saved or chose Discard", () => {
    expect(discards("saved")).toBe(true);
    expect(discards("discarded")).toBe(true);
    expect(discards("clean")).toBe(false);
    expect(discards("stopped")).toBe(false);
  });
});

/** A page loaded again: it thinks the map is clean, while the app holds unsaved edits until told to drop them. */
function reloadedPage(choice: Choice) {
  const options = { ...page(choice), dirty: () => false };
  const acts: boolean[] = [];
  let appDirty = true;
  return {
    ...options,
    acts,
    act: async (discard: boolean) => {
      acts.push(discard);
      if (appDirty && !discard) throw new BridgeError("unsaved", "The map has unsaved changes.");
      appDirty = false;
    },
    refresh: async () => {
      options.seen.push("refresh");
    },
  };
}

describe("dropUnsaved", () => {
  it("tells the app to discard only once the user chose Discard", async () => {
    const options = { ...page("discard"), acts: [] as boolean[] };

    const done = await dropUnsaved({
      ...options,
      act: async (discard) => options.acts.push(discard),
      refresh: async () => {},
    });

    expect(done).toBe(true);
    expect(options.acts).toEqual([true]);
  });

  it("does not act after Cancel", async () => {
    const options = reloadedPage("cancel");
    const dirtyPage = { ...options, dirty: () => true };

    expect(await dropUnsaved(dirtyPage)).toBe(false);
    expect(options.acts).toEqual([]);
  });

  it("asks when the app has unsaved edits the page did not know of, then acts on the answer", async () => {
    const options = reloadedPage("discard");

    expect(await dropUnsaved(options)).toBe(true);
    expect(options.acts).toEqual([false, true]);
    expect(options.seen).toEqual(["flush", "refresh", "flush", "ask"]);
  });

  it("keeps the app's edits when the user cancels the question asked late", async () => {
    const options = reloadedPage("cancel");

    expect(await dropUnsaved(options)).toBe(false);
    expect(options.acts).toEqual([false]);
  });

  it("passes on any other failure", async () => {
    const options = {
      ...page("discard"),
      dirty: () => false,
      act: async () => {
        throw new BridgeError("noMap", "No map is open.");
      },
      refresh: async () => {},
    };

    await expect(dropUnsaved(options)).rejects.toThrow("No map is open.");
  });
});
