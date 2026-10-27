import { describe, expect, it } from "vitest";
import { t } from "./text.ts";
import { appName, windowTitle } from "./title.ts";

describe("windowTitle", () => {
  it("is the app's name with no map open", () => {
    expect(windowTitle(null)).toBe(appName);
  });

  it("puts the map's path first", () => {
    expect(windowTitle("C:\\Maps\\Arena.w3x")).toBe(
      t("app.windowTitle", { path: "C:\\Maps\\Arena.w3x", app: t("app.name") }),
    );
  });
});
