import { beforeEach, describe, expect, it, vi } from "vitest";
import { openMap } from "./api.ts";
import { openEntries, openFrom } from "./openMenu.ts";
import { t } from "./text.ts";

vi.mock("./api.ts", () => ({ openMap: vi.fn(async () => null) }));

const opened = vi.mocked(openMap);

beforeEach(() => opened.mockClear());

describe("the Open map menu", () => {
  it("offers a folder and an archive", () => {
    expect(openEntries.map((entry) => entry.label)).toEqual([
      t("openMenu.folder"),
      t("openMenu.archive"),
    ]);
  });

  it("opens the folder dialog with Folder… and the file dialog with Archive…", async () => {
    const [folder, archive] = openEntries;
    await openFrom(folder, false);
    await openFrom(archive, true);
    expect(opened.mock.calls).toEqual([
      [{ archive: false, discard: false }],
      [{ archive: true, discard: true }],
    ]);
  });
});
