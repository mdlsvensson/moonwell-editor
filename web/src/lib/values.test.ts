import { describe, expect, it } from "vitest";
import type { ChoiceList, FieldView, ObjectView } from "./api.ts";
import { t, tCount } from "./text.ts";
import {
  addId,
  checkValue,
  editorFor,
  formatFlags,
  nameField,
  noticeTitle,
  parseFlags,
  removeId,
  savedText,
} from "./values.ts";

/** A field with plain defaults; `over` sets what a test cares about. */
function field(over: Partial<FieldView> & { id: string }): FieldView {
  return {
    label: over.id,
    category: "stats",
    sort: "",
    type: "int",
    choice: null,
    listOf: null,
    level: 0,
    value: "0",
    base: "0",
    changed: false,
    skin: false,
    warning: null,
    modes: null,
    ...over,
  };
}

const targets: ChoiceList = {
  flags: true,
  store: "string",
  entries: [
    { value: "ground", label: "Ground" },
    { value: "air", label: "Air" },
    { value: "enemies", label: "Enemy", alternates: ["enemy"] },
    { value: "friend", label: "Friend" },
  ],
};

const attackType: ChoiceList = {
  flags: false,
  store: "string",
  entries: [
    { value: "normal", label: "Normal" },
    { value: "pierce", label: "Pierce" },
  ],
};

// A list stored as a number: each value is its bit's own value, as the game's data spells them.
const pathing: ChoiceList = {
  flags: true,
  store: "int",
  entries: [
    { value: "1", label: "Ground" },
    { value: "2", label: "Air" },
    { value: "4", label: "Building" },
  ],
};

const choices = { targetList: targets, attackType, pathingListPrevent: pathing };

describe("editorFor", () => {
  it("gives each type its editor", () => {
    const cases: [Partial<FieldView>, string][] = [
      [{ type: "int" }, "number"],
      [{ type: "float" }, "number"],
      [{ type: "bool" }, "toggle"],
      [{ type: "string" }, "text"],
      [{ type: "choice", choice: "attackType" }, "select"],
      [{ type: "choice", choice: "targetList" }, "flags"],
      [{ type: "choice", choice: "pathingListPrevent" }, "flags"],
      [{ type: "objectList", listOf: "Ability" }, "ids"],
      [{ type: "objectRef", listOf: "Unit" }, "ids"],
      [{ type: "objectList", listOf: "object" }, "ids"],
    ];
    for (const [over, editor] of cases) {
      expect(editorFor(field({ id: "x", ...over }), choices), JSON.stringify(over)).toBe(editor);
    }
  });

  it("falls back to text when the choice list or the list's kind is not known", () => {
    expect(editorFor(field({ id: "x", type: "choice", choice: "missing" }), choices)).toBe("text");
    expect(editorFor(field({ id: "x", type: "choice", choice: "attackType" }), null)).toBe("text");
    expect(editorFor(field({ id: "x", type: "objectList", listOf: null }), choices)).toBe("text");
    expect(editorFor(field({ id: "x", type: "objectList", listOf: "Model" }), choices)).toBe(
      "text",
    );
    expect(editorFor(field({ id: "x", type: "somethingNew" }), choices)).toBe("text");
  });
});

describe("flags", () => {
  it("reads air,enemies as two ticked entries", () => {
    expect(parseFlags("air,enemies", targets)).toEqual(["air", "enemies"]);
  });

  it("writes two ticked entries back as air,enemies", () => {
    expect(formatFlags(["air", "enemies"], targets)).toBe("air,enemies");
  });

  it("writes the ticked entries in the order of the list, not the order they were ticked", () => {
    expect(formatFlags(["friend", "ground", "air"], targets)).toBe("ground,air,friend");
  });

  it("spells a value as the list does, whatever its case", () => {
    expect(parseFlags("Air, ENEMIES", targets)).toEqual(["air", "enemies"]);
  });

  it("ticks an entry the data spells another way (enemy for enemies), as the list spells it", () => {
    expect(parseFlags("air,enemy", targets)).toEqual(["air", "enemies"]);
  });

  it("keeps a value the list does not know, after the known ones", () => {
    expect(parseFlags("hostile,air", targets)).toEqual(["hostile", "air"]);
    expect(formatFlags(["hostile", "air"], targets)).toBe("air,hostile");
  });

  it("reads and writes nothing ticked as empty", () => {
    expect(parseFlags("", targets)).toEqual([]);
    expect(formatFlags([], targets)).toBe("");
  });

  it("reads and writes a list stored as a number as its bits' values", () => {
    expect(parseFlags("6", pathing)).toEqual(["2", "4"]);
    expect(formatFlags(["4", "2"], pathing)).toBe("6");
    expect(formatFlags([], pathing)).toBe("0");
    // A bit the list does not know is kept.
    expect(parseFlags("9", pathing)).toEqual(["1", "8"]);
    expect(formatFlags(["1", "8"], pathing)).toBe("9");
  });

  it("keeps a hidden bit when another is ticked (Extra Versions' 4 on the Priest)", () => {
    const versions: ChoiceList = {
      flags: true,
      store: "int",
      entries: [
        { value: "1", label: "Reign of Chaos" },
        { value: "2", label: "The Frozen Throne" },
        { value: "4", label: "Unlisted", hidden: true },
      ],
    };
    const ticked = parseFlags("6", versions);
    expect(ticked).toEqual(["2", "4"]);
    expect(formatFlags([...ticked, "1"], versions)).toBe("7");
    expect(formatFlags(["4"], versions)).toBe("4");
  });
});

describe("checkValue", () => {
  const life = field({ id: "uhpm", label: "Hit Points (Base)", type: "int" });
  const regen = field({ id: "uhpr", label: "Hit Points Regeneration", type: "float" });
  const name = field({ id: "unam", label: "Name", type: "string" });

  it("refuses 1.5 in a whole-number field before it is sent", () => {
    expect(checkValue(life, "1.5")).toEqual({
      ok: false,
      problem: t("value.notWhole", { field: "Hit Points (Base)", text: "1.5" }),
    });
  });

  it("refuses text and nothing in a number field", () => {
    expect(checkValue(life, "lots").ok).toBe(false);
    expect(checkValue(life, "").ok).toBe(false);
    expect(checkValue(regen, "fast").ok).toBe(false);
    expect(checkValue(regen, "1.5.2").ok).toBe(false);
    expect(checkValue(life, "99999999999").ok).toBe(false);
  });

  it("takes numbers, trimmed", () => {
    expect(checkValue(life, " 650 ")).toEqual({ ok: true, value: "650" });
    expect(checkValue(life, "-5")).toEqual({ ok: true, value: "-5" });
    expect(checkValue(regen, "1.5")).toEqual({ ok: true, value: "1.5" });
    expect(checkValue(regen, ".25")).toEqual({ ok: true, value: ".25" });
    expect(checkValue(regen, "2")).toEqual({ ok: true, value: "2" });
    expect(checkValue(regen, "1e3")).toEqual({ ok: true, value: "1e3" });
  });

  it("sends text as it was typed", () => {
    expect(checkValue(name, " Footman ")).toEqual({ ok: true, value: " Footman " });
  });
});

describe("object lists", () => {
  it("adds an id at the end, once", () => {
    expect(addId("AHhb,AHds", "AHad", false)).toBe("AHhb,AHds,AHad");
    expect(addId("", "AHad", false)).toBe("AHad");
    expect(addId("AHhb,AHds", "AHds", false)).toBe("AHhb,AHds");
  });

  it("replaces the id of a field that holds one", () => {
    expect(addId("hfoo", "hkni", true)).toBe("hkni");
  });

  it("removes the id at a place", () => {
    expect(removeId("AHhb,AHds,AHad", 1)).toBe("AHhb,AHad");
    expect(removeId("AHhb", 0)).toBe("");
  });
});

describe("nameField", () => {
  function view(kind: ObjectView["kind"], ids: string[]): ObjectView {
    return {
      kind,
      id: "x000",
      baseId: null,
      name: "x",
      chain: [],
      levels: 0,
      readOnly: null,
      fields: ids.map((id) => field({ id, type: "string" })),
    };
  }

  it("finds the field that holds the object's name", () => {
    expect(nameField(view("unit", ["uhpm", "unam"]))?.id).toBe("unam");
    expect(nameField(view("ability", ["anam"]))?.id).toBe("anam");
    expect(nameField(view("buff", ["fnam", "ftip"]))?.id).toBe("ftip");
    expect(nameField(view("upgrade", ["gnam"]))?.id).toBe("gnam");
    expect(nameField(view("doodad", ["uhpm"]))).toBeUndefined();
  });
});

describe("noticeTitle", () => {
  it("starts with the whole text, so a long one cut off in the status bar can be read", () => {
    const text =
      "Not saved: The map archive C:\\Maps\\Arena.w3x was not saved, and is as it was: a reason";
    expect(noticeTitle({ text })).toBe(text);
    expect(noticeTitle({ text: "Saved 1 file", title: "war3map.w3u\nBackup: C:\\B" })).toBe(
      "Saved 1 file\nwar3map.w3u\nBackup: C:\\B",
    );
  });
});

describe("savedText", () => {
  it("says how many files were written and where the backup is", () => {
    expect(
      savedText({
        written: ["war3map.w3u", "war3map.wts"],
        backup: "C:\\Maps\\Arena.backup\\2026-10-26 12-00-00",
        note: "",
      }),
    ).toBe(
      `${tCount("save.saved", 2)} · ${t("save.backup", { folder: "Arena.backup\\2026-10-26 12-00-00" })}`,
    );
    expect(savedText({ written: ["war3map.w3u"], backup: "", note: "" })).toBe(
      tCount("save.saved", 1),
    );
    expect(savedText({ written: [], backup: "", note: "" })).toBe(t("save.nothing"));
  });

  it("adds the note of a save whose replace reported an error after the map was saved", () => {
    const note =
      "The map archive C:\\Maps\\Arena.w3x was saved, but replacing it reported an error";
    expect(savedText({ written: ["war3map.w3u"], backup: "", note })).toBe(
      `${tCount("save.saved", 1)} · ${note}`,
    );
  });
});
