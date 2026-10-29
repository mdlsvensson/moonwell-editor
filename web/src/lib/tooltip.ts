/**
 * The in-game tooltip of an object (Sven, 2026-10-27): which fields make it, the game's colour markup turned into
 * coloured runs, and the `<id,Field>` placeholders an ability's text names filled from its own fields.
 */
import type { FieldView, Kind, ObjectView } from "./api.ts";
import type { UiIcon } from "./gameIcons.ts";
import { t } from "./text.ts";

/** A stretch of tooltip text in one colour; `placeholder` marks a `<id,Field>` the preview cannot fill. */
export type Run = { text: string; color: string | null; placeholder?: boolean };

/** One line of tooltip text. */
export type Line = Run[];

/** The gold the game colours a hotkey letter with. */
export const hotkeyColor = "#ffcc00";

/**
 * Splits tooltip markup into lines of runs: `|cAARRGGBB` starts a colour (`#rrggbb`, the alpha ignored), `|r` ends
 * it, `|n` and a real newline break the line, `||` is a bar. Any other `|` is kept as text.
 */
export function parseColors(text: string): Line[] {
  let line: Line = [];
  const lines: Line[] = [line];
  let color: string | null = null;
  let buffer = "";

  const flush = () => {
    if (buffer === "") return;
    const last = line[line.length - 1];
    if (last && last.color === color) last.text += buffer;
    else line.push({ text: buffer, color });
    buffer = "";
  };
  const breakLine = () => {
    flush();
    line = [];
    lines.push(line);
  };

  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (char === "\r" && text[i + 1] === "\n") continue;
    if (char === "\n" || char === "\r") {
      breakLine();
      continue;
    }
    if (char !== "|") {
      buffer += char;
      continue;
    }
    const next = text[i + 1]?.toLowerCase();
    if (next === "c" && /^[0-9a-f]{8}$/i.test(text.slice(i + 2, i + 10))) {
      flush();
      color = `#${text.slice(i + 4, i + 10).toLowerCase()}`;
      i += 9;
    } else if (next === "r") {
      flush();
      color = null;
      i += 1;
    } else if (next === "n") {
      breakLine();
      i += 1;
    } else if (next === "|") {
      buffer += "|";
      i += 1;
    } else {
      buffer += "|";
    }
  }
  flush();
  return lines;
}

/**
 * How a placeholder shows its number: `""` as a whole number, `"."` with one decimal, `"%"` times 100 (the text puts the
 * % sign after it).
 */
export type NumberForm = "" | "." | "%";

/** A placeholder: `<AHhb,Dur1>`, `<AHhb,DataB1,%>` or `<AHhb,DataA1,.>`. */
const placeholderPattern = /<([^,<>]+),([^,<>]+)(?:,([%.]))?>/g;

/** A number as a field holds it, for a cost or a value listed: an integer as is, a decimal without trailing zeros. */
export function formatNumber(value: string): string {
  const number = Number(value);
  if (value.trim() === "" || !Number.isFinite(number)) return value;
  return String(Math.round(number * 1000) / 1000);
}

/**
 * A value as a placeholder shows it in the game (game facts, probes of 2026-10-29): rounded down to a whole number
 * (0.33 shows 0, 1.5 shows 1); with `.` to one decimal, a whole number without one (0.125 shows 0.1, 13 shows 13);
 * with `%` times 100 (0.11 shows 11). How `.` and `%` round past those values was not measured.
 */
export function placeholderNumber(value: string, form: NumberForm = ""): string {
  const number = Number(value);
  if (value.trim() === "" || !Number.isFinite(number)) return value;
  // A hair over, so that a value a float holds just under (0.11 × 100) shows as the game shows it.
  const down = (shown: number) => Math.floor(shown + 1e-6);
  if (form === ".") return String(down(number * 10) / 10);
  return String(down(form === "%" ? number * 100 : number));
}

/** Whether a value is a whole number, which a placeholder shows as it is; text that is no number counts as one. */
export function isWhole(value: string | undefined): boolean {
  const number = Number(value);
  return (
    value === undefined ||
    value.trim() === "" ||
    !Number.isFinite(number) ||
    Number.isInteger(number)
  );
}

/**
 * Fills the `<id,Field>`, `<id,Field,.>` and `<id,Field,%>` placeholders whose id is one of `own` (the object's id and
 * base id; the game matches the id's case, not the key's) from `values`, keyed by the game's text key with its level
 * (`Dur1`, `Cool2`) or as a whole (`realHP`), as the game shows them. A placeholder for another object, or a key `values`
 * lacks, stays as written.
 */
export function fillData(
  text: string,
  own: readonly string[],
  values: Record<string, string>,
): string {
  const byKey = new Map(Object.entries(values).map(([key, value]) => [key.toLowerCase(), value]));
  return text.replace(placeholderPattern, (whole, id: string, key: string, form?: string) => {
    if (!own.includes(id.trim())) return whole;
    const value = byKey.get(key.trim().toLowerCase());
    return value === undefined ? whole : placeholderNumber(value, (form ?? "") as NumberForm);
  });
}

/** Splits the runs of `lines` around the placeholders left in them, which are marked `placeholder`. */
export function markPlaceholders(lines: Line[]): Line[] {
  return lines.map((line) =>
    line.flatMap((run) => {
      const parts: Run[] = [];
      let at = 0;
      for (const match of run.text.matchAll(placeholderPattern)) {
        const start = match.index ?? 0;
        if (start > at) parts.push({ text: run.text.slice(at, start), color: run.color });
        parts.push({ text: match[0], color: run.color, placeholder: true });
        at = start + match[0].length;
      }
      if (at === 0) return [run];
      if (at < run.text.length) parts.push({ text: run.text.slice(at), color: run.color });
      return parts;
    }),
  );
}

/** Tooltip markup as lines of runs, with the placeholders left in it marked. */
export function tooltipLines(text: string): Line[] {
  return markPlaceholders(parseColors(text));
}

/**
 * The ability fields an ability's text can name, by the game's text key; its data fields go by their letter (`DataA`).
 */
export const abilityKeys: Record<string, string> = {
  adur: "Dur",
  ahdu: "HeroDur",
  acdn: "Cool",
  amcs: "Cost",
  aran: "Rng",
  aare: "Area",
};

/** The game's text key of an ability's field (`Dur`, `DataA`); undefined for a field its text cannot name. */
export function textKey(field: Pick<FieldView, "id" | "data">): string | undefined {
  if (field.data && field.data >= 1 && field.data <= 9) return `Data${"ABCDEFGHI"[field.data - 1]}`;
  return abilityKeys[field.id];
}

/** The values an ability's text can name, keyed by the game's text key with its level (`Dur1`, `Cool3`, `DataA2`). */
export function abilityTextValues(view: ObjectView): Record<string, string> {
  const values: Record<string, string> = {};
  for (const field of view.fields) {
    const key = textKey(field);
    if (key && field.level > 0) values[`${key}${field.level}`] = field.value;
  }
  return values;
}

/**
 * The keys a unit's or an item's own text can name, which have no level, with how their values come from its fields
 * (game facts, probes of 2026-10-29; the standard data's tooltips use them too). A unit's damage is its first attack's:
 * the base plus the dice (lowest), or the base plus the dice times their sides (highest).
 */
export const wholeKeys: Partial<
  Record<Kind, Record<string, (field: (id: string) => number) => number>>
> = {
  unit: {
    realHP: (field) => field("uhpm"),
    regenHP: (field) => field("uhpr"),
    mindmg1: (field) => field("ua1b") + field("ua1d"),
    maxdmg1: (field) => field("ua1b") + field("ua1d") * field("ua1s"),
  },
  item: { uses: (field) => field("iuse") },
};

/** The values a unit's or an item's own text can name, by their keys (`realHP`, `uses`); none for another kind. */
export function wholeTextValues(view: ObjectView): Record<string, string> {
  const keys = wholeKeys[view.kind];
  if (!keys) return {};
  const values: Record<string, string> = {};
  for (const [key, make] of Object.entries(keys)) {
    let missing = false;
    const number = make((id) => {
      const field = view.fields.find((each) => each.id === id);
      if (!field) missing = true;
      return Number(field?.value ?? 0);
    });
    if (!missing && Number.isFinite(number)) values[key] = String(number);
  }
  return values;
}

/** The values an object's own text can name: an ability's by key and level, a unit's or an item's by key. */
export function textValues(view: ObjectView): Record<string, string> {
  return view.kind === "ability" ? abilityTextValues(view) : wholeTextValues(view);
}

/** One cost of the tooltip's cost line, with the game's icon for it (none for a cooldown, which shows its label). */
export type Cost = { label: string; value: string; icon?: UiIcon };

/** What the preview shows: the icon's path, the title and text as markup (placeholders filled), the costs. */
export type Tooltip = {
  /** The icon's path; "" for none. */
  icon: string;
  /** The basic tooltip, with the hotkey added in gold when there is one. */
  title: string;
  /** The extended tooltip. */
  text: string;
  /** The costs that are not zero, in the game's order. */
  costs: Cost[];
  /** The hotkey letter; "" for none. */
  hotkey: string;
  /** A hero ability's learn tooltip; null for none. */
  learn: { title: string; text: string } | null;
};

/** The fields that make a kind's tooltip, by what they are for. */
type Recipe = {
  icon: string;
  title: string;
  text: string;
  hotkey: string;
  costs: { label: string; ids: string[]; step?: string; icon?: UiIcon }[];
};

const recipes: Partial<Record<Kind, Recipe>> = {
  unit: {
    icon: "uico",
    title: "utip",
    text: "utub",
    hotkey: "uhot",
    costs: [
      { label: t("tooltip.gold"), ids: ["ugol"], icon: "gold" },
      { label: t("tooltip.lumber"), ids: ["ulum"], icon: "lumber" },
      { label: t("tooltip.food"), ids: ["ufoo"], icon: "food" },
    ],
  },
  item: {
    icon: "iico",
    title: "utip",
    text: "utub",
    hotkey: "uhot",
    costs: [
      { label: t("tooltip.gold"), ids: ["igol", "ugol"], icon: "gold" },
      { label: t("tooltip.lumber"), ids: ["ilum", "ulum"], icon: "lumber" },
    ],
  },
  ability: {
    icon: "aart",
    title: "atp1",
    text: "aub1",
    hotkey: "ahky",
    costs: [
      { label: t("tooltip.mana"), ids: ["amcs"], icon: "mana" },
      { label: t("tooltip.cooldown"), ids: ["acdn"] },
    ],
  },
  buff: { icon: "fart", title: "ftip", text: "fube", hotkey: "", costs: [] },
  upgrade: {
    icon: "gar1",
    title: "gtp1",
    text: "gub1",
    hotkey: "ghk1",
    costs: [
      { label: t("tooltip.gold"), ids: ["gglb"], step: "gglm", icon: "gold" },
      { label: t("tooltip.lumber"), ids: ["glmb"], step: "glmm", icon: "lumber" },
    ],
  },
};

/** A field at a level: the per-level field's level, else the field without levels; undefined when it has neither. */
function fieldAt(view: ObjectView, id: string, level: number): FieldView | undefined {
  return (
    view.fields.find((field) => field.id === id && field.level === level) ??
    view.fields.find((field) => field.id === id && field.level === 0)
  );
}

function valueAt(view: ObjectView, id: string, level: number): string {
  return fieldAt(view, id, level)?.value ?? "";
}

/** The basic tooltip with the hotkey after it in gold, as the game shows it: `Train Footman (F)`. */
export function withHotkey(title: string, hotkey: string): string {
  const letter = hotkey.trim().charAt(0).toUpperCase();
  if (letter === "" || title === "") return title;
  return `${title} (|cffffcc00${letter}|r)`;
}

/**
 * The tooltip of an object at a level (from 1; an object without levels ignores it), or null for a kind the game
 * shows no tooltip for (destructables and doodads). The icon is the kind's icon field, else the first icon field.
 */
export function tooltipOf(view: ObjectView, level = 1): Tooltip | null {
  const recipe = recipes[view.kind];
  if (!recipe) return null;
  const own = [view.id, ...(view.baseId ? [view.baseId] : [])];
  const values = textValues(view);
  const fill = (text: string) => fillData(text, own, values);

  const hotkey = recipe.hotkey ? valueAt(view, recipe.hotkey, level).trim() : "";
  const icon =
    fieldAt(view, recipe.icon, level)?.value ??
    view.fields.find((field) => field.asset === "icon")?.value ??
    "";

  const costs: Cost[] = [];
  for (const cost of recipe.costs) {
    const id = cost.ids.find((candidate) => fieldAt(view, candidate, level));
    if (!id) continue;
    let number = Number(valueAt(view, id, level));
    if (cost.step && level > 1)
      number += Number(valueAt(view, cost.step, level) || 0) * (level - 1);
    if (Number.isFinite(number) && number !== 0) {
      costs.push({ label: cost.label, value: formatNumber(String(number)), icon: cost.icon });
    }
  }

  let learn: Tooltip["learn"] = null;
  if (view.kind === "ability" && valueAt(view, "aher", level) !== "0") {
    const title = valueAt(view, "aret", level).replaceAll("%d", String(level));
    const text = valueAt(view, "arut", level);
    if (title || text) {
      learn = { title: fill(withHotkey(title, valueAt(view, "arhk", level))), text: fill(text) };
    }
  }

  return {
    icon,
    title: fill(withHotkey(valueAt(view, recipe.title, level), hotkey)),
    text: fill(valueAt(view, recipe.text, level)),
    costs,
    hotkey,
    learn,
  };
}
