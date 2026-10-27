/**
 * The game's 24 player colours, in player order (Sven, 2026-10-27): player 1 is Red, player 24 Peanut. A model's
 * team colour and hero glow take the colour of the player who owns the unit.
 */

/** A player colour: its name as the game gives it and its sRGB hex code. */
export type TeamColor = { name: string; hex: string };

export const teamColors: readonly TeamColor[] = [
  { name: "Red", hex: "#ff0303" },
  { name: "Blue", hex: "#0042ff" },
  { name: "Teal", hex: "#1ce6b9" },
  { name: "Purple", hex: "#540081" },
  { name: "Yellow", hex: "#fffc00" },
  { name: "Orange", hex: "#fe8a0e" },
  { name: "Green", hex: "#20c000" },
  { name: "Pink", hex: "#e55bb0" },
  { name: "Gray", hex: "#959697" },
  { name: "Light Blue", hex: "#7ebff1" },
  { name: "Dark Green", hex: "#106246" },
  { name: "Brown", hex: "#4a2a04" },
  { name: "Maroon", hex: "#9b0000" },
  { name: "Navy", hex: "#0000c3" },
  { name: "Turquoise", hex: "#00eaff" },
  { name: "Violet", hex: "#be00fe" },
  { name: "Wheat", hex: "#ebcd87" },
  { name: "Peach", hex: "#f8a48b" },
  { name: "Mint", hex: "#bfff80" },
  { name: "Lavender", hex: "#dcb9eb" },
  { name: "Coal", hex: "#282828" },
  { name: "Snow", hex: "#ebf0ff" },
  { name: "Emerald", hex: "#00781e" },
  { name: "Peanut", hex: "#a46f33" },
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
