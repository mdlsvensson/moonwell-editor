/**
 * Measures text as the page draws it, so the details panel can start the values just past its widest field name
 * (Sven, 2026-10-27).
 */

let context: CanvasRenderingContext2D | null | undefined;

/** The width in pixels of `text` in a CSS `font` (such as `14px "Google Sans"`); 0 where nothing can measure. */
export function textWidth(text: string, font: string): number {
  if (context === undefined) {
    context =
      typeof document === "undefined" ? null : document.createElement("canvas").getContext("2d");
  }
  if (!context) return 0;
  context.font = font;
  return context.measureText(text).width;
}

/** The narrowest and widest the name column may get, in pixels. */
export const labelColumn = { min: 140, max: 420 };

/**
 * The name column's width for these labels: the widest label (with its id after it when ids show), plus the cell's
 * padding, kept between `labelColumn.min` and `.max`.
 */
export function labelColumnWidth(
  labels: { label: string; id: string }[],
  showIds: boolean,
): number {
  let widest = 0;
  for (const each of labels) {
    let width = textWidth(each.label, '14px "Google Sans"');
    if (showIds) width += 6 + textWidth(each.id, '12.5px "JetBrains Mono Variable"');
    widest = Math.max(widest, width);
  }
  return Math.round(Math.min(labelColumn.max, Math.max(labelColumn.min, widest + 16)));
}
