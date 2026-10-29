/**
 * The game's 24 player colours, in player order (Sven, 2026-10-27): player 1 is Red, player 24 Peanut. A model's
 * team colour and hero glow take the colour of the player who owns the unit.
 */

import { t } from "./text.ts";

/** A player colour: its name as the game gives it and its sRGB hex code. */
export type TeamColor = { name: string; hex: string };

export const teamColors: readonly TeamColor[] = [
  { name: t("teamColour.red"), hex: "#ff0303" },
  { name: t("teamColour.blue"), hex: "#0042ff" },
  { name: t("teamColour.teal"), hex: "#1ce6b9" },
  { name: t("teamColour.purple"), hex: "#540081" },
  { name: t("teamColour.yellow"), hex: "#fffc00" },
  { name: t("teamColour.orange"), hex: "#fe8a0e" },
  { name: t("teamColour.green"), hex: "#20c000" },
  { name: t("teamColour.pink"), hex: "#e55bb0" },
  { name: t("teamColour.gray"), hex: "#959697" },
  { name: t("teamColour.lightBlue"), hex: "#7ebff1" },
  { name: t("teamColour.darkGreen"), hex: "#106246" },
  { name: t("teamColour.brown"), hex: "#4a2a04" },
  { name: t("teamColour.maroon"), hex: "#9b0000" },
  { name: t("teamColour.navy"), hex: "#0000c3" },
  { name: t("teamColour.turquoise"), hex: "#00eaff" },
  { name: t("teamColour.violet"), hex: "#be00fe" },
  { name: t("teamColour.wheat"), hex: "#ebcd87" },
  { name: t("teamColour.peach"), hex: "#f8a48b" },
  { name: t("teamColour.mint"), hex: "#bfff80" },
  { name: t("teamColour.lavender"), hex: "#dcb9eb" },
  { name: t("teamColour.coal"), hex: "#282828" },
  { name: t("teamColour.snow"), hex: "#ebf0ff" },
  { name: t("teamColour.emerald"), hex: "#00781e" },
  { name: t("teamColour.peanut"), hex: "#a46f33" },
];

/** The player index (0 for player 1) as a whole number from 0 to 23; anything else is player 1. */
export function playerIndex(index: number): number {
  return Number.isInteger(index) && index >= 0 && index < teamColors.length ? index : 0;
}

/** The colour of a player (0 for player 1) as red, green and blue from 0 to 1, as the renderer takes it. */
export function teamRgb(index: number): [number, number, number] {
  const hex = teamColors[playerIndex(index)]?.hex ?? "#ff0303";
  const channel = (at: number) => Number.parseInt(hex.slice(at, at + 2), 16) / 255;
  return [channel(1), channel(3), channel(5)];
}

/** The grey the game gives the neutral players (25 to 28: hostile, victim, extra, passive): its TeamColor24 to 27 files. */
const neutralGrey = 0x2e / 255;

/** The team colour a placed thing's owner (0 for player 1) gives it: a player's own, or the neutral players' grey. */
export function ownerRgb(player: number): [number, number, number] {
  return player >= 24 && player <= 27 ? [neutralGrey, neutralGrey, neutralGrey] : teamRgb(player);
}
