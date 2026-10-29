import { get } from "svelte/store";
import { describe, expect, it } from "vitest";
import { answer, picking, pickOnMap } from "./pick.ts";
import type { Pick } from "./picking.ts";

const point: Pick = { kind: "point", name: "", ref: null, typeId: null, x: 1, y: 2, z: 3 };

describe("pickOnMap", () => {
  it("asks the map for one kind and gives the click", async () => {
    const asked = pickOnMap("point");
    expect(get(picking).kind).toBe("point");
    answer(point);
    expect(await asked).toEqual(point);
    expect(get(picking).kind).toBeNull();
  });

  it("gives null when cancelled (Esc)", async () => {
    const asked = pickOnMap("unit");
    answer(null);
    expect(await asked).toBeNull();
  });

  it("gives null to an earlier ask when another one comes", async () => {
    const first = pickOnMap("unit");
    const second = pickOnMap("region");
    expect(await first).toBeNull();
    expect(get(picking).kind).toBe("region");
    answer(null);
    expect(await second).toBeNull();
  });
});
