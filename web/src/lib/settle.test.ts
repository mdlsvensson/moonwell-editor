import { describe, expect, it } from "vitest";
import { type Choice, settleUnsaved } from "./settle.ts";

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

    expect(await settleUnsaved(options)).toBe(false);
    expect(options.seen).toEqual(["flush", "ask"]);
  });

  it("goes on without asking when nothing is unsaved after the edits on their way", async () => {
    const options = { ...page("cancel"), dirty: () => false };

    expect(await settleUnsaved(options)).toBe(true);
    expect(options.seen).toEqual(["flush"]);
  });

  it("asks even when the page thinks the map is clean, when told to (the window saw it dirty)", async () => {
    const options = { ...page("discard"), dirty: () => false };

    expect(await settleUnsaved({ ...options, always: true })).toBe(true);
    expect(options.seen).toEqual(["flush", "ask"]);
  });

  it("goes on after a save only when the save worked", async () => {
    const worked = page("save", true);
    const failed = page("save", false);

    expect(await settleUnsaved(worked)).toBe(true);
    expect(await settleUnsaved(failed)).toBe(false);
    expect(worked.seen).toEqual(["flush", "ask", "save"]);
  });

  it("goes on after Discard, and not after Cancel", async () => {
    expect(await settleUnsaved(page("discard"))).toBe(true);
    expect(await settleUnsaved(page("cancel"))).toBe(false);
  });
});
