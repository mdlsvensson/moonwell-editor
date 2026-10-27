/**
 * The page's own text (Sven, 2026-10-27): every label, tooltip, notice and error the page writes is in
 * `src/text/strings.json`, by a key named for where it shows (`titleBar.back`, `settings.tab.hotkeys`). A `{name}` in
 * a text is filled from the values given. A count's text is two keys, `.one` for 1 and `.other` for any other count.
 */
import strings from "../text/strings.json";

/** A key of the page's text; a key not in the file is a compile error. */
export type TextKey = Exclude<keyof typeof strings, "_about">;

/** The text of `key`, its `{name}` placeholders filled from `values`; a placeholder with no value stays as written. */
export function t(key: TextKey, values?: Record<string, string | number>): string {
  const text: string = strings[key];
  if (!values) return text;
  return text.replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? String(values[name]) : whole,
  );
}

/** The text for a count: `key.one` for 1, `key.other` for any other, with `{count}` filled. */
export function tCount(
  key: TextKey extends infer K ? (K extends `${infer Base}.one` ? Base : never) : never,
  count: number,
  values?: Record<string, string | number>,
): string {
  return t(`${key}.${count === 1 ? "one" : "other"}` as TextKey, { count, ...values });
}

/** A piece of a text for markup: plain text, or the place of a `{name}` the markup fills with an element of its own. */
export type TextPart = { text: string; slot?: undefined } | { text?: undefined; slot: string };

/**
 * The text of `key` in pieces, for markup that shows some values in elements of their own (a path in the code font):
 * the placeholders in `values` filled, each other `{name}` a slot, in the order the text has them.
 */
export function tParts(key: TextKey, values: Record<string, string | number> = {}): TextPart[] {
  const parts: TextPart[] = [];
  const raw: string = strings[key];
  let text = "";
  let from = 0;
  for (const match of raw.matchAll(/\{(\w+)\}/g)) {
    const name = match[1] ?? "";
    text += raw.slice(from, match.index);
    from = match.index + match[0].length;
    if (name in values) {
      text += String(values[name]);
      continue;
    }
    if (text !== "") parts.push({ text });
    text = "";
    parts.push({ slot: name });
  }
  text += raw.slice(from);
  if (text !== "") parts.push({ text });
  return parts;
}
