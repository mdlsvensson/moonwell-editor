import { describe, expect, it } from "vitest";
import type { FieldView } from "./api.ts";
import { copiedField, copyField, copyRange, fits } from "./fieldClip.ts";

function field(over: Partial<FieldView> & { id: string; type: string }): FieldView {
  return {
    label: over.id,
    category: "stats",
    sort: "",
    choice: null,
    listOf: null,
    level: 0,
    value: "",
    base: "",
    changed: false,
    skin: false,
    warning: null,
    modes: null,
    ...over,
  } as FieldView;
}

const hitPoints = field({ id: "uhpm", type: "int", value: "420" });
const armor = field({ id: "udef", type: "int", value: "2" });
const speed = field({ id: "ua1c", type: "float", value: "1.35" });
const name = field({ id: "unam", type: "string", value: "Footman" });
const hostile = field({ id: "uhos", type: "bool", value: "0" });
const attack = field({ id: "ua1t", type: "choice", choice: "attackType", value: "normal" });
const armorType = field({ id: "udty", type: "choice", choice: "defenseType", value: "large" });
const abilities = field({ id: "uabi", type: "objectList", listOf: "Ability", value: "Aatk,AInv" });
const heroAbilities = field({ id: "uhab", type: "objectList", listOf: "Ability", value: "" });
const defaultAbility = field({ id: "udaa", type: "objectRef", listOf: "Ability", value: "" });
const trains = field({ id: "utra", type: "objectList", listOf: "Unit", value: "hfoo" });
const requires = field({ id: "ureq", type: "objectList", listOf: "object", value: "" });

describe("pasting a copied field", () => {
  it("keeps the last copy", () => {
    copyField(armor);
    expect(copiedField()).toEqual({
      label: "udef",
      type: "int",
      choice: null,
      listOf: null,
      value: "2",
    });
  });

  it("puts a value into a field of the same kind", () => {
    expect(fits(copyField(armor), hitPoints)).toBe(true);
    expect(fits(copyField(name), field({ id: "utip", type: "string" }))).toBe(true);
    expect(fits(copyField(hostile), field({ id: "ubdg", type: "bool" }))).toBe(true);
    expect(fits(copyField(abilities), heroAbilities)).toBe(true);
  });

  it("puts a number into another number field, a whole one only into a whole-number field", () => {
    expect(fits(copyField(armor), speed)).toBe(true);
    expect(fits(copyField(speed), hitPoints)).toBe(false);
    expect(fits(copyField(field({ id: "x", type: "float", value: "3" })), hitPoints)).toBe(true);
  });

  it("keeps a choice to its own list", () => {
    expect(
      fits(copyField(attack), field({ id: "ua2t", type: "choice", choice: "attackType" })),
    ).toBe(true);
    expect(fits(copyField(attack), armorType)).toBe(false);
  });

  it("puts objects only where their kind goes, and one object where a field holds one", () => {
    expect(fits(copyField(trains), abilities)).toBe(false);
    expect(fits(copyField(trains), requires)).toBe(true);
    expect(fits(copyField(abilities), defaultAbility)).toBe(false);
    expect(fits(copyField({ ...abilities, value: "Aatk" }), defaultAbility)).toBe(true);
    expect(fits(copyField(defaultAbility), abilities)).toBe(true);
  });

  it("never mixes kinds of value", () => {
    expect(fits(copyField(name), hitPoints)).toBe(false);
    expect(fits(copyField(armor), name)).toBe(false);
    expect(fits(copyField(hostile), hitPoints)).toBe(false);
    expect(fits(copyField(abilities), name)).toBe(false);
  });
});

describe("copyRange", () => {
  it("keeps a total's range as the game writes it, for a paste into another total only", () => {
    const clip = copyRange("Attack 1 - Damage", { min: 12, max: 13 });
    expect(copiedField()).toBe(clip);
    expect(clip.value).toBe("12 - 13");
    expect(clip.range).toEqual({ min: 12, max: 13 });
    expect(fits(clip, hitPoints)).toBe(false);
    expect(fits(clip, speed)).toBe(false);
    expect(fits(clip, name)).toBe(false);
  });
});
