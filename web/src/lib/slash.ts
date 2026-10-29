/**
 * The tooltip editor's `/` list (Sven, 2026-10-29): typing `/` in a text lists the values it can show, as slash
 * commands do elsewhere, and picking one puts its placeholder in (`<AHhb,Dur1>`). The words typed after the `/`
 * narrow the list; a number at their end picks the level, and a `.` or `%` how the number shows (`/dur2`,
 * `/dataa.`, `/dataa%`). A value that is not a whole number, which the game would round down, is listed with one
 * decimal and as a percentage (game facts, probes of 2026-10-29).
 */

import type { ObjectView } from "./api.ts";
import { tokenOf } from "./richText.ts";
import {
  abilityTextValues,
  isWhole,
  type NumberForm,
  placeholderNumber,
  textKey,
  wholeTextValues,
} from "./tooltip.ts";

/** A key a placeholder can name: the game's key, its name, and whether it has no level (`realHP`). */
export type SlashKey = { key: string; name: string; whole?: boolean };

/** An object whose values a placeholder can name: its id, its name and its values' keys with their names. */
export type SlashSource = {
  id: string;
  /** The object's name; "" for the object being edited, whose values need no name. */
  name: string;
  levels: number;
  keys: SlashKey[];
  /** The values by key and level (`Dur1`), or by key alone for a key without a level (`realHP`). */
  values: Record<string, string>;
};

/**
 * An object's values as a source: an ability's each key its text can name, with `names`' name for it, else its first
 * field's label; a unit's or an item's its keys without a level (`realHP`, `uses`), with `names`' name for each. `name`
 * is "" for the object being edited.
 */
export function sourceOf(
  view: ObjectView,
  name: string,
  names: Record<string, string>,
): SlashSource {
  if (view.kind !== "ability") {
    const values = wholeTextValues(view);
    return {
      id: view.id,
      name,
      levels: 0,
      keys: Object.keys(values).map((key) => ({ key, name: names[key] ?? key, whole: true })),
      values,
    };
  }
  const keys = new Map<string, string>();
  for (const field of view.fields) {
    const key = textKey(field);
    if (key && field.level > 0 && !keys.has(key)) keys.set(key, names[key] ?? field.label);
  }
  return {
    id: view.id,
    name,
    levels: view.levels,
    keys: [...keys].map(([key, named]) => ({ key, name: named })),
    values: abilityTextValues(view),
  };
}

/** The abilities an object's ability lists name (a unit's, an item's), whose values its tooltip can show too. */
export function listedAbilities(view: ObjectView): string[] {
  const ids = view.fields
    .filter((field) => field.type === "objectList" && field.listOf === "Ability")
    .flatMap((field) => field.value.split(","))
    .map((id) => id.trim())
    .filter((id) => id !== "");
  return [...new Set(ids)];
}

/** One entry of the list: the placeholder it puts in, and the value as the game shows it. */
export type Suggestion = { token: string; name: string; owner: string; value: string | undefined };

/** What a placeholder chip counts as in the text before the cursor (the object replacement character). */
export const chipChar = String.fromCharCode(0xfffc);

/** The words typed after a `/` that starts a word and ends the text before the cursor; null when there is none. */
export function slashQuery(before: string): string | null {
  // A `/` inside a word (`1/2`) is a slash; the object replacement character stands for a placeholder chip.
  const match = before.match(/(?:^|[\s\uFFFC])\/([\p{L}\p{N}%.]*)$/u);
  return match ? (match[1] ?? "") : null;
}

/** What a query asks for: the words, the level a number at its end picks, and the form a `.` or `%` there picks. */
export function queryParts(query: string): {
  words: string;
  level: number | null;
  form: NumberForm | null;
} {
  const match = query.match(/^(.*?)(\d*)([%.]?)$/) ?? [];
  const digits = match[2] ?? "";
  return {
    words: (match[1] ?? "").toLowerCase(),
    level: digits ? Number(digits) : null,
    form: match[3] ? (match[3] as NumberForm) : null,
  };
}

/** How a value is shown when nothing picks it: as it is when whole, else with one decimal and as a percentage. */
export function formsFor(value: string | undefined): NumberForm[] {
  return isWhole(value) ? [""] : [".", "%"];
}

/** The list for a query: each source's values at the level asked (else `level`), the ones its words name. */
export function suggestions(
  sources: SlashSource[],
  query: string,
  level: number,
  limit = 40,
): Suggestion[] {
  const asked = queryParts(query);
  const found: Suggestion[] = [];
  for (const source of sources) {
    const at = Math.min(Math.max(1, asked.level ?? level), Math.max(1, source.levels));
    for (const { key, name, whole } of source.keys) {
      const words = `${key} ${name} ${source.id} ${source.name}`.toLowerCase();
      if (asked.words && !words.includes(asked.words)) continue;
      const value = source.values[whole ? key : `${key}${at}`];
      for (const form of asked.form !== null ? [asked.form] : formsFor(value)) {
        found.push({
          token: tokenOf({ id: source.id, key, level: whole ? 0 : at, form }),
          name,
          owner: source.name,
          value: value === undefined ? undefined : placeholderNumber(value, form),
        });
        if (found.length >= limit) return found;
      }
    }
  }
  return found;
}
