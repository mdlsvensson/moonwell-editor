import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "./api.ts";
import { call, on } from "./bridge.ts";

vi.mock("./bridge.ts", () => ({
  call: vi.fn(async () => undefined),
  on: vi.fn(() => () => {}),
}));

const called = vi.mocked(call);
const listened = vi.mocked(on);

beforeEach(() => {
  called.mockClear();
  listened.mockClear();
});

const footmanLife: api.FieldRef = { kind: "unit", id: "hfoo", field: "uhpm", level: 0 };

describe("each wrapper calls its command with the app's argument names", () => {
  const cases: [string, () => Promise<unknown>, string, object][] = [
    [
      "openMap with a path",
      () => api.openMap({ path: "C:\\maps\\arena" }),
      "openMap",
      { path: "C:\\maps\\arena" },
    ],
    [
      "openMap with an archive's path",
      () => api.openMap({ path: "C:\\maps\\Arena.w3x" }),
      "openMap",
      { path: "C:\\maps\\Arena.w3x" },
    ],
    ["openMap with the folder dialog", () => api.openMap(), "openMap", {}],
    [
      "openMap with the archive dialog",
      () => api.openMap({ archive: true }),
      "openMap",
      { archive: true },
    ],
    [
      "openMap dropping unsaved edits",
      () => api.openMap({ discard: true }),
      "openMap",
      { discard: true },
    ],
    ["currentMap", () => api.currentMap(), "currentMap", {}],
    ["tree", () => api.tree("ability"), "tree", { kind: "ability" }],
    ["object", () => api.object("unit", "hfoo"), "object", { kind: "unit", id: "hfoo" }],
    [
      "setField",
      () => api.setField(footmanLife, "500"),
      "setField",
      { kind: "unit", id: "hfoo", field: "uhpm", level: 0, value: "500" },
    ],
    [
      "resetField",
      () => api.resetField(footmanLife),
      "resetField",
      { kind: "unit", id: "hfoo", field: "uhpm", level: 0 },
    ],
    [
      "createObject",
      () => api.createObject("unit", "hfoo"),
      "createObject",
      { kind: "unit", baseId: "hfoo" },
    ],
    [
      "createObject with an id",
      () => api.createObject("unit", "hfoo", "h00A"),
      "createObject",
      { kind: "unit", baseId: "hfoo", id: "h00A" },
    ],
    [
      "copyObject",
      () => api.copyObject("item", "I000", "I001"),
      "copyObject",
      { kind: "item", fromId: "I000", id: "I001" },
    ],
    [
      "deleteObject",
      () => api.deleteObject("doodad", "D000"),
      "deleteObject",
      { kind: "doodad", id: "D000" },
    ],
    ["undo", () => api.undo(), "undo", {}],
    ["redo", () => api.redo(), "redo", {}],
    ["save", () => api.save(), "save", {}],
    ["reload", () => api.reload(), "reload", {}],
    ["reload dropping unsaved edits", () => api.reload(true), "reload", { discard: true }],
    ["search", () => api.search("foot", 20), "search", { query: "foot", limit: 20 }],
    ["choices", () => api.choices(), "choices", {}],
    ["startup", () => api.startup(), "startup", {}],
    ["nextId", () => api.nextId("unit", "hfoo"), "nextId", { kind: "unit", baseId: "hfoo" }],
    ["fields", () => api.fields("unit"), "fields", { kind: "unit" }],
    [
      "table",
      () => api.table("unit", ["hfoo", "hpea"], [{ field: "uhpm", level: 0 }]),
      "table",
      { kind: "unit", ids: ["hfoo", "hpea"], columns: [{ field: "uhpm", level: 0 }] },
    ],
    [
      "setCells",
      () =>
        api.setCells("unit", [
          { id: "hfoo", field: "uhpm", level: 0, value: "500" },
          { id: "hpea", field: "uhpm", level: 0, value: null },
        ]),
      "setCells",
      {
        kind: "unit",
        edits: [
          { id: "hfoo", field: "uhpm", level: 0, value: "500" },
          { id: "hpea", field: "uhpm", level: 0, value: null },
        ],
      },
    ],
    ["closeAsking", () => api.closeAsking(), "closeAsking", {}],
    ["closeWindow", () => api.closeWindow(), "closeWindow", {}],
  ];

  it.each(cases)("%s", async (_name, run, command, args) => {
    await run();

    expect(called).toHaveBeenCalledTimes(1);
    const [sentCommand, sentArgs] = called.mock.calls[0] ?? [];
    expect(sentCommand).toBe(command);
    // The names as JSON sends them: an argument left out is not sent at all.
    expect(JSON.parse(JSON.stringify(sentArgs ?? {}))).toEqual(args);
  });
});

describe("onSessionChanged", () => {
  it("listens to the sessionChanged event and passes its payload on", () => {
    const seen: api.SessionState[] = [];
    const stop = api.onSessionChanged((state) => seen.push(state));

    expect(listened).toHaveBeenCalledTimes(1);
    const [event, handler] = listened.mock.calls[0] ?? [];
    expect(event).toBe("sessionChanged");
    handler?.({ dirty: true, changedObjects: 2 });

    expect(seen).toEqual([{ dirty: true, changedObjects: 2 }]);
    expect(typeof stop).toBe("function");
  });
});

describe("onCloseRequested", () => {
  it("listens to the closeRequested event and passes on whether the window saw the map dirty", () => {
    const heard: boolean[] = [];
    api.onCloseRequested((request) => heard.push(request.dirty));

    const [event, handler] = listened.mock.calls[0] ?? [];
    expect(event).toBe("closeRequested");
    handler?.({ dirty: true });
    handler?.({ dirty: false });
    handler?.({});

    expect(heard).toEqual([true, false, false]);
  });
});

describe("the kinds", () => {
  it("are the app's seven kind keys, in the Object Editor's tab order", () => {
    expect(api.kinds).toEqual([
      "unit",
      "item",
      "destructable",
      "doodad",
      "ability",
      "buff",
      "upgrade",
    ]);
  });
});
