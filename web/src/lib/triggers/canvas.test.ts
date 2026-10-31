import { describe, expect, it } from "vitest";
import {
  cardWidth,
  columnsFor,
  fitted,
  focused,
  grid,
  laidOut,
  placesToSave,
  snapped,
  zoomedAt,
} from "./canvas.ts";

describe("snapped", () => {
  it("goes to the nearest grid line", () => {
    expect(snapped(29)).toBe(grid);
    expect(snapped(31)).toBe(grid * 2);
    expect(snapped(-29)).toBe(-grid);
  });
});

describe("columnsFor", () => {
  it("stands a few short cards in one column", () => {
    expect(columnsFor([100, 100], 1.6)).toBe(1);
  });

  it("spreads tall cards out to fill the screen", () => {
    expect(columnsFor(new Array(18).fill(1000), 1.6)).toBe(9);
  });

  it("gives a wider screen more columns", () => {
    const heights = new Array(18).fill(1000);
    expect(columnsFor(heights, 2.5)).toBeGreaterThan(columnsFor(heights, 1.2));
  });

  it("has a limit", () => {
    expect(columnsFor(new Array(1000).fill(1000), 1.6)).toBe(16);
  });

  it("handles no cards and a screen not measured yet", () => {
    expect(columnsFor([], 1.6)).toBe(1);
    expect(columnsFor([100], Number.NaN)).toBe(1);
  });
});

describe("laidOut", () => {
  it("keeps a card that has a place", () => {
    const [box] = laidOut([{ id: 1, place: { x: 200, y: 400 }, height: 100 }], 1);
    expect(box).toEqual({ id: 1, x: 200, y: 400, width: cardWidth, height: 100 });
  });

  it("puts each new card in the shortest column", () => {
    const boxes = laidOut(
      [
        { id: 1, place: null, height: 300 },
        { id: 2, place: null, height: 100 },
        { id: 3, place: null, height: 100 },
        { id: 4, place: null, height: 100 },
      ],
      2,
    );
    const right = cardWidth + grid * 2;
    expect(boxes.map((box) => box.x)).toEqual([0, right, right, right]);
    expect(boxes[2]?.y).toBe(120);
  });

  it("places a new card clear of the cards that have a place", () => {
    const boxes = laidOut(
      [
        { id: 1, place: { x: 0, y: 0 }, height: 200 },
        { id: 2, place: null, height: 100 },
      ],
      1,
    );
    expect(boxes[1]?.y).toBe(220);
  });
});

describe("placesToSave", () => {
  it("saves every card of the grid, so moving one does not move the rest", () => {
    const places = placesToSave(
      [
        { id: 1, x: 0, y: 0, width: 10, height: 10 },
        { id: 2, x: 400, y: 0, width: 10, height: 10 },
      ],
      { id: 2, x: 80, y: 60 },
    );
    expect(places).toEqual([
      { id: 1, x: 0, y: 0 },
      { id: 2, x: 80, y: 60 },
    ]);
  });
});

describe("zoomedAt", () => {
  it("keeps the point under the mouse where it is", () => {
    const view = { x: 10, y: 20, scale: 1 };
    const next = zoomedAt(view, 110, 220, 0.5);
    expect(next.scale).toBe(0.5);
    expect((110 - next.x) / next.scale).toBeCloseTo((110 - view.x) / view.scale);
    expect((220 - next.y) / next.scale).toBeCloseTo((220 - view.y) / view.scale);
  });

  it("stays between its limits", () => {
    expect(zoomedAt({ x: 0, y: 0, scale: 1 }, 0, 0, 100).scale).toBeLessThanOrEqual(1.5);
    expect(zoomedAt({ x: 0, y: 0, scale: 1 }, 0, 0, 0.001).scale).toBeGreaterThanOrEqual(0.1);
  });
});

describe("fitted", () => {
  it("shows every card, never larger than life", () => {
    const view = fitted(
      [
        { id: 1, x: 100, y: 100, width: 300, height: 200 },
        { id: 2, x: 900, y: 500, width: 300, height: 700 },
      ],
      800,
      600,
    );
    expect(view.scale).toBeLessThan(1);
    expect(view.x + 100 * view.scale).toBeGreaterThanOrEqual(0);
    expect(view.y + 1200 * view.scale).toBeLessThanOrEqual(600);
  });

  it("leaves a small grid at full size, at its top left", () => {
    const view = fitted([{ id: 1, x: 0, y: 0, width: 100, height: 100 }], 800, 600);
    expect(view.scale).toBe(1);
  });

  it("stops at the smallest scale asked for, at the top left", () => {
    const boxes = [{ id: 1, x: 40, y: 60, width: 340, height: 20000 }];
    expect(fitted(boxes, 800, 600).scale).toBeLessThan(0.1 + 0.01);
    const view = fitted(boxes, 800, 600, 0.5);
    expect(view.scale).toBe(0.5);
    expect(view.x + 40 * view.scale).toBe(grid);
    expect(view.y + 60 * view.scale).toBe(grid);
  });

  it("handles a grid with no cards", () => {
    expect(fitted([], 800, 600)).toEqual({ x: 20, y: 20, scale: 1 });
  });
});

describe("focused", () => {
  it("centres a card across and puts its top near the top", () => {
    const view = focused({ id: 1, x: 500, y: 700, width: 300, height: 200 }, 900, 600, 1);
    expect(view.x + 650).toBe(450);
    expect(view.y + 700).toBeLessThan(40);
  });
});
