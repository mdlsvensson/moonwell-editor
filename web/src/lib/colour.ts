/**
 * Colours for the tooltip editor's picker (Sven, 2026-10-29): a colour as hue, saturation and value, which the picker
 * shows as its square and its hue bar, and as the `#rrggbb` the toolbar keeps.
 */

/** A colour: hue from 0 to 360, saturation and value from 0 to 1. */
export type Hsv = { h: number; s: number; v: number };

/** A colour typed as `#rrggbb` or `rrggbb` (any case), as `#rrggbb` in lower case; null when it is not one. */
export function parseHex(text: string): string | null {
  const match = text.trim().match(/^#?([0-9a-f]{6})$/i);
  return match ? `#${(match[1] ?? "").toLowerCase()}` : null;
}

/** The `#rrggbb` of a colour. */
export function hexOf({ h, s, v }: Hsv): string {
  const part = (n: number): string => {
    const k = (n + h / 60) % 6;
    const channel = v - v * s * Math.max(0, Math.min(k, 4 - k, 1));
    return Math.round(channel * 255)
      .toString(16)
      .padStart(2, "0");
  };
  return `#${part(5)}${part(3)}${part(1)}`;
}

/** A `#rrggbb` colour as hue, saturation and value; a grey keeps `hue` (it has none of its own). */
export function hsvOf(hex: string, hue = 0): Hsv {
  const value = Number.parseInt(hex.slice(1), 16);
  const r = ((value >> 16) & 255) / 255;
  const g = ((value >> 8) & 255) / 255;
  const b = (value & 255) / 255;
  const max = Math.max(r, g, b);
  const spread = max - Math.min(r, g, b);
  let h = hue;
  if (spread > 0) {
    if (max === r) h = 60 * (((g - b) / spread + 6) % 6);
    else if (max === g) h = 60 * ((b - r) / spread + 2);
    else h = 60 * ((r - g) / spread + 4);
  }
  return { h, s: max === 0 ? 0 : spread / max, v: max };
}
