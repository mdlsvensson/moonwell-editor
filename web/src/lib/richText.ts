/**
 * The tooltip editor's text (Sven, 2026-10-27): the game's tooltip markup as pieces the editor shows as they look
 * (coloured text, line breaks, and the `<id,Key>` placeholders as named chips), and those pieces written back as
 * markup. A colour keeps its alpha as written (`|cffffcc00`); text with no colour is the game's white.
 */

/** A piece of tooltip text: text in a colour (`aarrggbb`, or null for none), a line break, or a placeholder. */
export type Segment =
  | { type: "text"; text: string; color: string | null }
  | { type: "break" }
  | { type: "token"; token: string; color: string | null };

const tokenPattern = /^<([^,<>]+),([^,<>]+)(,%)?>/;

/** The pieces of tooltip markup: `|cAARRGGBB` starts a colour, `|r` ends it, `|n` or a newline breaks the line. */
export function segmentsOf(markup: string): Segment[] {
  const segments: Segment[] = [];
  let color: string | null = null;
  let text = "";
  const flush = () => {
    if (text === "") return;
    const last = segments[segments.length - 1];
    if (last?.type === "text" && last.color === color) last.text += text;
    else segments.push({ type: "text", text, color });
    text = "";
  };
  for (let i = 0; i < markup.length; i++) {
    const char = markup[i] ?? "";
    if (char === "\r" && markup[i + 1] === "\n") continue;
    if (char === "\n" || char === "\r") {
      flush();
      segments.push({ type: "break" });
      continue;
    }
    if (char === "<") {
      const token = markup.slice(i).match(tokenPattern);
      if (token) {
        flush();
        segments.push({ type: "token", token: token[0], color });
        i += token[0].length - 1;
        continue;
      }
    }
    if (char !== "|") {
      text += char;
      continue;
    }
    const next = markup[i + 1]?.toLowerCase();
    if (next === "c" && /^[0-9a-f]{8}$/i.test(markup.slice(i + 2, i + 10))) {
      flush();
      color = markup.slice(i + 2, i + 10).toLowerCase();
      i += 9;
    } else if (next === "r") {
      flush();
      color = null;
      i += 1;
    } else if (next === "n") {
      flush();
      segments.push({ type: "break" });
      i += 1;
    } else if (next === "|") {
      text += "|";
      i += 1;
    } else {
      text += "|";
    }
  }
  flush();
  return segments;
}

/** How a text breaks its lines: as it does already (`|n` or a newline), else `|n` as the game's data does. */
export function newlineOf(markup: string): string {
  if (markup.includes("|n") || markup.includes("|N")) return "|n";
  return markup.includes("\n") ? "\n" : "|n";
}

/**
 * Markup from pieces: a colour opens where the colour changes and `|r` ends it before text with none (and at the end);
 * a bar that would read as a code is written twice. A colour stays across a line break.
 */
export function markupOf(segments: readonly Segment[], newline = "|n"): string {
  let out = "";
  let open: string | null = null;
  const colorTo = (color: string | null) => {
    if (color === open) return;
    if (color === null) out += "|r";
    else out += `|c${color}`;
    open = color;
  };
  for (const segment of segments) {
    if (segment.type === "break") {
      out += newline;
    } else if (segment.type === "token") {
      colorTo(segment.color);
      out += segment.token;
    } else {
      colorTo(segment.color);
      out += segment.text.replace(/\|(?=[cCrRnN|]|$)/g, "||");
    }
  }
  if (open !== null) out += "|r";
  return out;
}

/** A colour as CSS (`#rrggbb`) from the markup's `aarrggbb`. */
export function cssColor(color: string): string {
  return `#${color.slice(2)}`;
}

/** A CSS colour (`#rrggbb` or `rgb(r, g, b)`) as the markup's `aarrggbb`, fully opaque; null when it is not one. */
export function markupColor(css: string): string | null {
  const hex = css.trim().match(/^#([0-9a-f]{6})$/i);
  if (hex?.[1]) return `ff${hex[1].toLowerCase()}`;
  const rgb = css.match(/^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
  if (!rgb) return null;
  return `ff${[rgb[1], rgb[2], rgb[3]].map((part) => Number(part).toString(16).padStart(2, "0")).join("")}`;
}

/** What a placeholder names: the object's id, the game's key without its level (`Dur`), the level, and `,%`. */
export type TokenParts = { id: string; key: string; level: number; percent: boolean };

/** The parts of a placeholder (`<AHhb,Dur2,%>`); null for text that is not one. */
export function tokenParts(token: string): TokenParts | null {
  const match = token.match(/^<([^,<>]+),([^,<>]+?)(\d*)(,%)?>$/);
  if (!match) return null;
  return {
    id: match[1]?.trim() ?? "",
    key: match[2]?.trim() ?? "",
    level: match[3] ? Number(match[3]) : 0,
    percent: match[4] !== undefined,
  };
}

/** A placeholder from its parts. */
export function tokenOf(parts: TokenParts): string {
  return `<${parts.id},${parts.key}${parts.level > 0 ? parts.level : ""}${parts.percent ? ",%" : ""}>`;
}
