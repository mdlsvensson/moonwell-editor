/**
 * The game's interface icons the editor shows beside values (Sven, 2026-10-27): gold, lumber, food and mana as the
 * game's tooltips show them, and the armor, attack and hero attribute icons of the unit info card, by the field or the
 * list value they go with. The files are the Human interface's, from the install like any other game file.
 */
import type { FieldView } from "./api.ts";

const tooltips = "UI\\Widgets\\ToolTips\\Human\\";
const infocard = "UI\\Widgets\\Console\\Human\\infocard-";

/** The interface icons by name, as the paths the game keeps them under. */
export const uiIcons = {
  gold: `${tooltips}ToolTipGoldIcon.blp`,
  lumber: `${tooltips}ToolTipLumberIcon.blp`,
  food: `${tooltips}ToolTipSupplyIcon.blp`,
  mana: `${tooltips}ToolTipManaIcon.blp`,
  armor: `${infocard}armor-medium.blp`,
  attack: `${infocard}attack-melee.blp`,
  strength: `${infocard}heroattributes-str.blp`,
  agility: `${infocard}heroattributes-agi.blp`,
  intelligence: `${infocard}heroattributes-int.blp`,
} as const;

export type UiIcon = keyof typeof uiIcons;

// The armor icon of each defense type (`defenseType` spells them as words, `defenseTypeInt` as numbers); Normal, which
// the game does not use, shows as Medium.
const armorIcons: Record<string, string> = {
  normal: "medium",
  small: "small",
  medium: "medium",
  large: "large",
  fort: "fortified",
  hero: "hero",
  divine: "divine",
  none: "unarmored",
  "4": "medium",
  "0": "small",
  "1": "medium",
  "2": "large",
  "3": "fortified",
  "5": "hero",
  "6": "divine",
  "7": "unarmored",
};

// The attack icon of each attack type; Normal is the sword (melee), Spells the magic one, and Unknown has none.
const attackIcons: Record<string, string> = {
  normal: "melee",
  pierce: "piercing",
  siege: "siege",
  spells: "magic",
  chaos: "chaos",
  magic: "magic",
  hero: "hero",
};

const attributeIcons: Record<string, string> = { STR: "str", AGI: "agi", INT: "int" };

/** The interface icon's path for a value of a list (`defenseType`, `attackType`, `attributeType`); undefined for none. */
export function choiceIcon(list: string, value: string): string | undefined {
  if (list === "defenseType" || list === "defenseTypeInt") {
    const armor = armorIcons[value.toLowerCase()];
    return armor && `${infocard}armor-${armor}.blp`;
  }
  if (list === "attackType") {
    const attack = attackIcons[value.toLowerCase()];
    return attack && `${infocard}attack-${attack}.blp`;
  }
  if (list === "attributeType") {
    const attribute = attributeIcons[value.toUpperCase()];
    return attribute && `${infocard}heroattributes-${attribute}.blp`;
  }
  return undefined;
}

// The fields named by what they hold: the word in their label (World Editor's: Gold Cost, Food Produced, Mana Maximum,
// Starting Strength), first found first.
const byWord: [RegExp, UiIcon][] = [
  [/\bgold\b/i, "gold"],
  [/\blumber\b/i, "lumber"],
  [/\bfood\b/i, "food"],
  [/\bmana\b/i, "mana"],
  [/\bstrength\b/i, "strength"],
  [/\bagility\b/i, "agility"],
  [/\bintelligence\b/i, "intelligence"],
];

// A unit's armor and attack fields, which show the icon of the unit's own armor or attack type.
const armorFields = new Set(["udef", "udup"]);
const attackFields: Record<string, string> = {
  ua1b: "ua1t",
  ua1d: "ua1t",
  ua1s: "ua1t",
  udu1: "ua1t",
  ua2b: "ua2t",
  ua2d: "ua2t",
  ua2s: "ua2t",
  udu2: "ua2t",
};

/**
 * The interface icon's path to show beside a field's value, from the other fields of its object (`fields`, for the
 * unit's armor and attack types); undefined for a field without one. A choice field whose values have icons shows
 * them in its list instead.
 */
export function fieldIcon(field: FieldView, fields: readonly FieldView[]): string | undefined {
  if (field.type === "choice" || field.type === "string" || field.type === "bool") return undefined;
  if (armorFields.has(field.id)) {
    const type = fields.find((each) => each.id === "udty")?.value ?? "";
    return choiceIcon("defenseType", type) ?? uiIcons.armor;
  }
  const attackType = attackFields[field.id];
  if (attackType) {
    const type = fields.find((each) => each.id === attackType)?.value ?? "";
    return choiceIcon("attackType", type) ?? uiIcons.attack;
  }
  const word = byWord.find(([pattern]) => pattern.test(field.label));
  return word ? uiIcons[word[1]] : undefined;
}
