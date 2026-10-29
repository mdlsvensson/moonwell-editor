import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const css = readFileSync(new URL("../app.css", import.meta.url), "utf8");

describe("app.css theme", () => {
  it.each([
    ["--bg", "#16161d"],
    ["--accent", "#7e9cd8"],
    ["--changed", "#e6c384"],
    ["--warn", "#e46876"],
  ])("defines %s as %s", (name, value) => {
    expect(css.toLowerCase()).toContain(`${name}: ${value};`);
  });

  it("uses no pixel font", () => {
    expect(css).not.toMatch(/pixel|press start|silkscreen/i);
  });
});
