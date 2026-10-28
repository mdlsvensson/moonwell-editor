/**
 * The game's interface icons the tooltip shows beside its costs: gold, lumber, food and mana as the game's tooltips
 * show them. The files are the Human interface's, from the install like any other game file.
 */

const tooltips = "UI\\Widgets\\ToolTips\\Human\\";

/** The interface icons by name, as the paths the game keeps them under. */
export const uiIcons = {
  gold: `${tooltips}ToolTipGoldIcon.blp`,
  lumber: `${tooltips}ToolTipLumberIcon.blp`,
  food: `${tooltips}ToolTipSupplyIcon.blp`,
  mana: `${tooltips}ToolTipManaIcon.blp`,
} as const;

export type UiIcon = keyof typeof uiIcons;
