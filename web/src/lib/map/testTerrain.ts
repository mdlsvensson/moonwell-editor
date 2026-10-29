/** For the tests: terrain bytes made by hand. */

/** Terrain bytes as the app writes them (TerrainBytes): columns, rows, ground, relief, water, layers, tiles. */
export function terrainBytes(
  columns: number,
  rows: number,
  ground: (index: number) => number,
  water: (index: number) => number = () => Number.NaN,
): ArrayBuffer {
  const corners = columns * rows;
  const tiles = (columns - 1) * (rows - 1);
  const buffer = new ArrayBuffer(8 + corners * 12 + tiles * 9);
  const view = new DataView(buffer);
  view.setInt32(0, columns, true);
  view.setInt32(4, rows, true);
  for (let i = 0; i < corners; i++) {
    view.setFloat32(8 + i * 4, ground(i), true);
    view.setFloat32(8 + corners * 4 + i * 4, ground(i) / 2, true);
    view.setFloat32(8 + corners * 8 + i * 4, water(i), true);
  }
  const layers = new Uint8Array(buffer, 8 + corners * 12, tiles * 8);
  layers.fill(255);
  layers.set([3, 19], 0);
  new Uint8Array(buffer, 8 + corners * 12 + tiles * 8, tiles).fill(1);
  return buffer;
}
