import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import strings from "../text/strings.json";
import { t, tCount, tParts } from "./text.ts";

const texts: Record<string, string> = strings;
const src = join(import.meta.dirname, "..");

function placeholders(text: string): string[] {
  return [...text.matchAll(/\{(\w+)\}/g)].map((match) => match[1] ?? "");
}

describe("t", () => {
  it("is the text of the key as the file has it", () => {
    expect(t("titleBar.back")).toBe(texts["titleBar.back"]);
  });

  it("fills the placeholders from the values", () => {
    expect(t("details.newObject", { base: "its base" })).toBe(
      texts["details.newObject"]?.replace("{base}", "its base"),
    );
    expect(t("grid.doesNotApplyTo", { field: "Hit points", object: "Footman" })).toBe(
      "Hit points does not apply to Footman.",
    );
    expect(t("tooltip.level", { level: 2, levels: 3 })).toBe("Level 2 of 3");
  });

  it("leaves a placeholder with no value as written", () => {
    expect(t("grid.doesNotApplyTo", { field: "Hit points" })).toBe(
      "Hit points does not apply to {object}.",
    );
    expect(t("grid.doesNotApplyTo")).toBe("{field} does not apply to {object}.");
  });

  it("does not fill a value's own braces again", () => {
    expect(t("grid.readOnly", { reason: "{reason}" })).toBe("Read only: {reason}");
  });
});

describe("tCount", () => {
  it("takes .one for 1 and .other for any other count, with the count filled", () => {
    expect(tCount("table.cells", 1)).toBe("1 cell");
    expect(tCount("table.cells", 0)).toBe("0 cells");
    expect(tCount("table.cells", 12)).toBe("12 cells");
    expect(tCount("notice.reset", 2, { name: "Footman" })).toBe(
      "Reset 2 fields of Footman · Ctrl Z brings them back",
    );
  });
});

describe("tParts", () => {
  it("cuts the text at the placeholders it has no value for, in the text's order", () => {
    expect(tParts("status.dataReady", { tables: "base tables" })).toEqual([
      { text: "Game data " },
      { slot: "folder" },
      { text: " · base tables" },
    ]);
    expect(tParts("dialog.delete.text", { kind: "unit" })).toEqual([
      { text: "The map loses the custom unit " },
      { slot: "name" },
      { text: " " },
      { slot: "id" },
      { text: ". Ctrl Z brings it back until the map is closed." },
    ]);
  });
});

describe("the text file", () => {
  const raw = readFileSync(join(src, "text", "strings.json"), "utf8");
  const keys = Object.keys(texts);

  it("starts with how to edit it, then has every key once, in order", () => {
    expect(keys[0]).toBe("_about");
    const written = [...raw.matchAll(/^\s*"([^"]+)":/gm)].map((match) => match[1]);
    expect(written).toEqual(keys);
    const rest = keys.slice(1);
    expect(rest).toEqual([...rest].sort());
  });

  it("names each key by its place, and holds text in each", () => {
    for (const key of keys.slice(1)) {
      expect(key, key).toMatch(/^[a-z][A-Za-z]*(\.[a-z][A-Za-z]*)+$/);
      expect(typeof texts[key], key).toBe("string");
      expect(texts[key], key).not.toBe("");
    }
  });

  it("writes its placeholders as {name}, with no other braces", () => {
    for (const key of keys) {
      const left = (texts[key] ?? "").replace(/\{\w+\}/g, "");
      expect(left, key).not.toMatch(/[{}]/);
    }
  });

  it("has both texts of a count, each with {count}", () => {
    for (const key of keys) {
      const pair = key.endsWith(".one")
        ? key.replace(/\.one$/, ".other")
        : key.endsWith(".other")
          ? key.replace(/\.other$/, ".one")
          : null;
      if (!pair) continue;
      expect(keys, key).toContain(pair);
      expect(placeholders(texts[key] ?? ""), key).toContain("count");
    }
  });
});

// The calls of t, tCount and tParts in the page's code, read from the source: each key's text has the placeholders
// the call fills (tParts fills some and leaves the rest to the markup).
type Call = {
  file: string;
  fn: "t" | "tCount" | "tParts";
  keys: string[];
  values: string[] | null;
};

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(path);
    return /\.(ts|svelte)$/.test(entry.name) ? [path] : [];
  });
}

/** The end of the expression starting at `from`: the index of the first `,` or `)` outside brackets and strings. */
function argumentEnd(code: string, from: number): number {
  let depth = 0;
  for (let at = from; at < code.length; at++) {
    const char = code[at];
    if (char === '"' || char === "'" || char === "`") {
      at = stringEnd(code, at);
    } else if (char === "(" || char === "[" || char === "{") depth++;
    else if (char === ")" || char === "]" || char === "}") {
      if (depth === 0) return at;
      depth--;
    } else if (char === "," && depth === 0) return at;
  }
  return code.length;
}

function stringEnd(code: string, from: number): number {
  const quote = code[from];
  for (let at = from + 1; at < code.length; at++) {
    if (code[at] === "\\") at++;
    else if (quote === "`" && code[at] === "$" && code[at + 1] === "{") {
      at = argumentEnd(code, at + 2);
    } else if (code[at] === quote) return at;
  }
  return code.length;
}

/** The names an object literal sets (`{ a: 1, b }`); null for anything else, or one that spreads another. */
function propertyNames(text: string): string[] | null {
  const body = text.trim();
  if (!body.startsWith("{") || !body.endsWith("}")) return null;
  const inner = body.slice(1, -1);
  const names: string[] = [];
  let from = 0;
  while (from < inner.length) {
    const end = argumentEnd(inner, from);
    const part = inner.slice(from, end).trim();
    if (part.startsWith("...")) return null;
    const name = /^(\w+)\s*(:|$)/.exec(part)?.[1];
    if (part !== "" && !name) return null;
    if (name) names.push(name);
    from = end + 1;
  }
  return names;
}

function callsIn(file: string): Call[] {
  const code = readFileSync(file, "utf8");
  const calls: Call[] = [];
  for (const match of code.matchAll(/(?<![\w.$])(tCount|tParts|t)\(/g)) {
    const fn = match[1] as Call["fn"];
    const start = (match.index ?? 0) + match[0].length;
    const firstEnd = argumentEnd(code, start);
    const first = code.slice(start, firstEnd);
    const literals = [...first.matchAll(/"([\w.]+)"/g)].map((each) => each[1] ?? "");
    const keys =
      fn === "tCount"
        ? literals.flatMap((base) => [`${base}.one`, `${base}.other`])
        : literals.filter((key) => key in texts);
    if (keys.length === 0) continue;
    let values: string[] | null = [];
    if (code[firstEnd] === ",") {
      const secondEnd = argumentEnd(code, firstEnd + 1);
      const second = code.slice(firstEnd + 1, secondEnd);
      if (fn === "tCount") {
        const thirdEnd = code[secondEnd] === "," ? argumentEnd(code, secondEnd + 1) : secondEnd;
        const third = code.slice(secondEnd + 1, thirdEnd);
        values = code[secondEnd] === "," ? propertyNames(third) : [];
        if (values) values.push("count");
      } else {
        values = propertyNames(second);
      }
    } else if (fn === "tCount") values = ["count"];
    calls.push({ file, fn, keys, values });
  }
  return calls;
}

describe("the code's calls", () => {
  const files = sourceFiles(src);
  // This file's own calls leave placeholders out on purpose.
  const calls = files.filter((file) => !file.endsWith("text.test.ts")).flatMap(callsIn);

  it("are found", () => {
    expect(calls.length).toBeGreaterThan(200);
  });

  it("fill every placeholder of their text, and no other", () => {
    for (const call of calls) {
      if (call.values === null) continue;
      for (const key of call.keys) {
        const where = `${call.fn}("${key}") in ${call.file}`;
        expect(Object.keys(texts), where).toContain(key);
        const wanted = [...new Set(placeholders(texts[key] ?? ""))].sort();
        const given = [...new Set(call.values)].sort();
        if (call.fn === "tParts") {
          for (const name of given) expect(wanted, where).toContain(name);
        } else {
          expect(given, where).toEqual(wanted);
        }
      }
    }
  });

  it("use every key of the file", () => {
    const code = files
      .filter((file) => !file.endsWith(".test.ts"))
      .map((file) => readFileSync(file, "utf8"))
      .join("\n");
    for (const key of Object.keys(texts).slice(1)) {
      const base = key.replace(/\.(one|other)$/, "");
      expect(code.includes(`"${key}"`) || code.includes(`"${base}"`), key).toBe(true);
    }
  });
});
