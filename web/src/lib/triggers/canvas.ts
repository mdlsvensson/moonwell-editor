/** A category's grid: where its cards go, and how the view pans and zooms over them. */

/** The width of a card on the grid. */
export const cardWidth = 340;

/** The spacing of the grid's lines, which a card snaps to. */
export const grid = 20;

const pitch = cardWidth + grid * 2;
const cardGap = grid;
const maxColumns = 16;
const minScale = 0.1;
const maxScale = 1.5;

/** The smallest a category opens at: one long trigger should not shrink the rest to slivers. */
export const openScale = 1 / 3;

/** Below this a card's text cannot be read, and it shows its name large instead. */
export const farScale = 0.45;

/** What the view shows: the grid's origin on screen, and its scale. */
export type View = { x: number; y: number; scale: number };

/** A card on the grid. */
export type Box = { id: number; x: number; y: number; width: number; height: number };

/** A card to place: where it was put, if it ever was, and how tall it is drawn. */
export type CardSize = { id: number; place: { x: number; y: number } | null; height: number };

export function snapped(value: number): number {
  return Math.round(value / grid) * grid;
}

/** How many columns cards of these heights stand in to fill a screen of this width over height. */
export function columnsFor(heights: number[], aspect: number): number {
  const total = heights.reduce((sum, height) => sum + height + cardGap, 0);
  const columns = Math.round(Math.sqrt((aspect * total) / pitch));
  return Number.isNaN(columns) ? 1 : Math.max(1, Math.min(maxColumns, columns));
}

/**
 * Every card's box. A card with a place stays there; the others go in columns, each to the shortest one, below what
 * stands in that column.
 */
export function laidOut(cards: CardSize[], columns: number): Box[] {
  const bottoms = new Array<number>(columns).fill(0);
  for (const card of cards) {
    if (!card.place) continue;
    const column = Math.max(0, Math.min(columns - 1, Math.floor(card.place.x / pitch)));
    bottoms[column] = Math.max(bottoms[column] ?? 0, card.place.y + card.height + cardGap);
  }

  return cards.map((card) => {
    if (card.place) return { id: card.id, ...card.place, width: cardWidth, height: card.height };
    let column = 0;
    for (let at = 1; at < columns; at++) {
      if ((bottoms[at] ?? 0) < (bottoms[column] ?? 0)) column = at;
    }

    const y = Math.ceil((bottoms[column] ?? 0) / grid) * grid;
    bottoms[column] = y + card.height + cardGap;
    return { id: card.id, x: column * pitch, y, width: cardWidth, height: card.height };
  });
}

/**
 * The places to keep once a card was moved: all of the grid's, as a card that has none is placed by the others and
 * would otherwise jump.
 */
export function placesToSave(
  boxes: Box[],
  moved: { id: number; x: number; y: number },
): { id: number; x: number; y: number }[] {
  return boxes.map(({ id, x, y }) => (id === moved.id ? moved : { id, x, y }));
}

/** The view zoomed by `factor`, the point under `(px, py)` staying there. */
export function zoomedAt(view: View, px: number, py: number, factor: number): View {
  const scale = Math.max(minScale, Math.min(maxScale, view.scale * factor));
  const kept = scale / view.scale;
  return { x: px - (px - view.x) * kept, y: py - (py - view.y) * kept, scale };
}

/** The view that shows every card; never larger than life, nor smaller than `least`. */
export function fitted(boxes: Box[], width: number, height: number, least = minScale): View {
  if (boxes.length === 0) return { x: grid, y: grid, scale: 1 };
  const left = Math.min(...boxes.map((box) => box.x));
  const top = Math.min(...boxes.map((box) => box.y));
  const right = Math.max(...boxes.map((box) => box.x + box.width));
  const bottom = Math.max(...boxes.map((box) => box.y + box.height));
  const margin = grid;
  const scale = Math.max(
    least,
    Math.min(1, (width - margin * 2) / (right - left), (height - margin * 2) / (bottom - top)),
  );
  return { x: margin - left * scale, y: margin - top * scale, scale };
}

/** The view that brings a card to the top middle of the screen. */
export function focused(box: Box, width: number, _height: number, scale: number): View {
  return { x: width / 2 - (box.x + box.width / 2) * scale, y: grid - box.y * scale, scale };
}
