import { describe, expect, it } from "vitest";
import { inFolders, inList, matching, modelFolders, ranked } from "./assets.ts";

const paths = [
  String.raw`ReplaceableTextures\CommandButtons\BTNPeasant.blp`,
  String.raw`ReplaceableTextures\CommandButtons\BTNFootman.blp`,
  String.raw`units\human\Peasant\Peasant.mdl`,
];

describe("the picker's search", () => {
  it("keeps the paths that hold every word, in any case", () => {
    expect(matching(paths, "peasant", 10)).toEqual([paths[0], paths[2]]);
    expect(matching(paths, "btn PEAS", 10)).toEqual([paths[0]]);
  });

  it("takes either slash, and keeps everything for an empty query, up to the limit", () => {
    expect(matching(paths, "human/peasant", 10)).toEqual([paths[2]]);
    expect(matching(paths, "", 2)).toEqual(paths.slice(0, 2));
  });
});

describe("the picker's order", () => {
  const models = [
    String.raw`Abilities\Spells\Human\HolyBolt\HolyBoltSpecialArt.mdl`,
    String.raw`Abilities\Weapons\FireBallMissile\FireBallMissile.mdl`,
    String.raw`Units\Human\Jaina\Jaina.mdl`,
    String.raw`Units\Human\Peasant\Peasant.mdl`,
  ];

  it("shows the field's own folders first, and still everything else", () => {
    const order = inFolders(modelFolders("unit", "umdl"));
    expect(ranked(models, "", 10, order)).toEqual([models[2], models[3], models[0], models[1]]);
    expect(ranked(models, "", 10, inFolders(modelFolders("unit", "ua1m")))[0]).toBe(models[1]);
  });

  it("puts a match in the file's name before a match in its folders", () => {
    const paths = [
      String.raw`Units\Human\Peasant\PeasantShadow.mdl`,
      String.raw`Units\Peasant.mdl`,
    ];
    expect(ranked([String.raw`Units\Jaina\Thing.mdl`, ...paths], "jaina", 10, () => 0)).toEqual([
      String.raw`Units\Jaina\Thing.mdl`,
    ]);
    expect(
      ranked(
        [String.raw`Units\Jaina\Thing.mdl`, String.raw`Units\Other\Jaina.mdl`],
        "jaina",
        10,
        () => 0,
      ),
    ).toEqual([String.raw`Units\Other\Jaina.mdl`, String.raw`Units\Jaina\Thing.mdl`]);
  });

  it("shows the icons the kind's objects use first, in their order", () => {
    const icons = [String.raw`X\BTNA.blp`, String.raw`X\BTNB.blp`, String.raw`X\BTNC.blp`];
    expect(ranked(icons, "", 10, inList([String.raw`x\btnc.blp`, "X/BTNB.blp"]))).toEqual([
      icons[2],
      icons[1],
      icons[0],
    ]);
  });
});
