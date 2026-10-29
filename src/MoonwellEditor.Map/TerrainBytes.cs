namespace MoonwellEditor.Map;

/// <summary>
/// The terrain as the page draws it, little-endian: int32 columns, int32 rows; float32 ground, relief and water a
/// corner (water NaN where there is none); then a tile its four layers as texture, piece byte pairs (255, 255 for an
/// unused one); then a tile one byte of flags (bit 0 the ground is drawn, bit 1 outside the playable area, bit 2
/// boundary, each by the tile's bottom-left corner, *claim*, HiveWE). Corners and tiles run row by row from the bottom
/// left.
/// </summary>
public static class TerrainBytes
{
    /// <summary>The bytes of a terrain.</summary>
    public static byte[] Write(TerrainGrid grid, TileLayers layers, CliffTiles cliffs)
    {
        var columns = grid.Columns;
        var rows = grid.Rows;
        var tiles = (columns - 1) * (rows - 1);
        using var stream = new MemoryStream(8 + (columns * rows * 12) + (tiles * 9));
        using var writer = new BinaryWriter(stream);
        writer.Write(columns);
        writer.Write(rows);
        Corners(writer, grid, grid.Ground);
        Corners(writer, grid, grid.Relief);
        Corners(writer, grid, (column, row) => grid.Water(column, row) ?? float.NaN);
        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                var layer = layers.At(column, row);
                for (var k = 0; k < 4; k++)
                {
                    writer.Write(k < layer.Count ? layer[k].Texture : (byte)255);
                    writer.Write(k < layer.Count ? layer[k].Piece : (byte)255);
                }
            }
        }

        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                writer.Write((byte)((cliffs.Covered(column, row) ? 0 : 1)
                    | (grid.Edge(column, row) ? 2 : 0)
                    | (grid.Boundary(column, row) ? 4 : 0)));
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void Corners(BinaryWriter writer, TerrainGrid grid, Func<int, int, float> value)
    {
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var column = 0; column < grid.Columns; column++)
            {
                writer.Write(value(column, row));
            }
        }
    }
}
