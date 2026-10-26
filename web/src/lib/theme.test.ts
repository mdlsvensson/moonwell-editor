import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const css = readFileSync(new URL("../app.css", import.meta.url), "utf8");

describe("app.css theme", () => {
  it.each([
    ["--bg", "#121212"],
    ["--accent", "#4d9375"],
    ["--changed", "#e6cc77"],
    ["--warn", "#cb7676"],
  ])("defines %s as %s", (name, value) => {
    expect(css.toLowerCase()).toContain(`${name}: ${value};`);
  });

  it("uses no pixel font", () => {
    expect(css).not.toMatch(/pixel|press start|silkscreen/i);
  });
});
