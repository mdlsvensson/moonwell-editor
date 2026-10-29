import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const css = readFileSync(new URL("../app.css", import.meta.url), "utf8");

describe("app.css theme", () => {
  it.each([
    ["--bg", "#0d0c0c"],
    ["--accent", "#8ba4b0"],
    ["--changed", "#c4b28a"],
    ["--warn", "#c4746e"],
  ])("defines %s as %s", (name, value) => {
    expect(css.toLowerCase()).toContain(`${name}: ${value};`);
  });

  it("uses no pixel font", () => {
    expect(css).not.toMatch(/pixel|press start|silkscreen/i);
  });
});
