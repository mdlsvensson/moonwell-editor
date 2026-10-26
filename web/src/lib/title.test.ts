import { describe, expect, it } from "vitest";
import { appName, windowTitle } from "./title.ts";

describe("windowTitle", () => {
  it("is the app's name with no map open", () => {
    expect(windowTitle(null)).toBe(appName);
  });

  it("puts the map's path first", () => {
    expect(windowTitle("C:\Maps\Arena.w3x")).toBe(`C:\Maps\Arena.w3x — ${appName}`);
  });
});
