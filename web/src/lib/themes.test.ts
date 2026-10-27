import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { applyTheme, defaultTheme, themeOf, themes, tokens } from "./themes.ts";

const css = readFileSync(new URL("../app.css", import.meta.url), "utf8").toLowerCase();

describe("the themes", () => {
  it("each set every token to a colour, with ids of their own", () => {
    for (const each of themes) {
      for (const token of tokens)
        expect(each.colors[token], `${each.id} ${token}`).toMatch(/^#[0-9a-f]{6}$/);
    }
    expect(new Set(themes.map((each) => each.id)).size).toBe(themes.length);
  });

  it("start with Vitesse Dark, as app.css draws the page before a theme is put on", () => {
    const vitesse = themeOf(defaultTheme);
    expect(themes[0]).toBe(vitesse);
    for (const token of tokens) expect(css).toContain(`--${token}: ${vitesse.colors[token]};`);
  });

  it("include the ones Sven asked for", () => {
    const names = themes.map((each) => each.name);
    for (const name of ["Kanagawa Wave", "Gruvbox Dark", "Dracula", "Catppuccin Mocha"]) {
      expect(names).toContain(name);
    }
  });

  it("fall back to the default for an unknown id", () => {
    expect(themeOf("no-such-theme").id).toBe(defaultTheme);
    expect(themeOf(null).id).toBe(defaultTheme);
  });

  it("put their colours on the page", () => {
    const set: Record<string, string> = {};
    const root = {
      style: { setProperty: (name: string, value: string) => (set[name] = value) },
      dataset: {} as Record<string, string>,
    };
    const kanagawa = themeOf("kanagawa-wave");
    applyTheme(kanagawa, root as unknown as HTMLElement);
    expect(set["--bg"]).toBe(kanagawa.colors.bg);
    expect(set["--kind-upgrade"]).toBe(kanagawa.colors["kind-upgrade"]);
    expect(root.dataset.theme).toBe("kanagawa-wave");
  });
});
