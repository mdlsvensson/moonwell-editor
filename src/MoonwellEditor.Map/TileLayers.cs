using War3Net.Build.Environment;

namespace MoonwellEditor.Map;

/// <summary>One texture drawn on a tile: its number in <see cref="TerrainTextures.All"/> and the piece of its sheet.</summary>
/// <param name="Texture">The texture's number.</param>
/// <param name="Piece">The piece, 0 to 15 across the sheet's 4 by 4 grid row by row from the top left, 16 to 31 the
/// right half of an 8 by 4 sheet.</param>
public readonly record struct TileLayer(byte Texture, byte Piece);

/// <summary>
/// The textures each tile draws, lowest first (*claim*, from HiveWE): every texture its four corners hold, in the
/// textures' order. The first is drawn whole, in the piece its variation picks; each one after it in the piece of its
/// corners: bottom right 1, bottom left 2, top right 4, top left 8. A corner by a cliff holds its cliff type's ground
/// texture, a blighted corner the blight, and one naming a texture the map does not list the map's first.
/// </summary>
public sealed class TileLayers
{
    private readonly int _columns;
    private readonly TileLayer[][] _tiles;

    private TileLayers(int columns, TileLayer[][] tiles)
    {
        _columns = columns;
        _tiles = tiles;
    }

    /// <summary>The layers of every tile of <paramref name="terrain"/>.</summary>
    public static TileLayers From(MapEnvironment terrain, TerrainTextures textures, CliffTiles cliffs)
    {
        var columns = (int)terrain.Width + 1;
        var rows = (int)terrain.Height + 1;
        var corners = terrain.TerrainTiles;
        var held = new byte[corners.Count];
        for (var index = 0; index < corners.Count; index++)
        {
            var corner = corners[index];
            held[index] = (byte)(cliffs.Touches(index % columns, index / columns) && textures.CliffGround.Count > 0
                ? textures.CliffGround[Cliffs.TypeAt(corner, textures.CliffGround.Count)]
                : corner.IsBlighted ? textures.Blight
                : corner.Texture < textures.Blight ? corner.Texture : 0);
        }

        var tiles = new TileLayer[(columns - 1) * (rows - 1)][];
        for (var y = 0; y < rows - 1; y++)
        {
            for (var x = 0; x < columns - 1; x++)
            {
                var bottomLeft = held[(y * columns) + x];
                var bottomRight = held[(y * columns) + x + 1];
                var topLeft = held[((y + 1) * columns) + x];
                var topRight = held[((y + 1) * columns) + x + 1];
                var used = new[] { bottomLeft, bottomRight, topLeft, topRight }.Distinct().Order().ToArray();
                var layers = new TileLayer[used.Length];
                var variation = Cliffs.Raw(corners[(y * columns) + x]) & 0x1F;
                layers[0] = new TileLayer(used[0], WholePiece(textures.All[used[0]].Extended, variation));
                for (var k = 1; k < used.Length; k++)
                {
                    var texture = used[k];
                    var piece = (bottomRight == texture ? 1 : 0) | (bottomLeft == texture ? 2 : 0)
                        | (topRight == texture ? 4 : 0) | (topLeft == texture ? 8 : 0);
                    layers[k] = new TileLayer(texture, (byte)piece);
                }

                tiles[(y * (columns - 1)) + x] = layers;
            }
        }

        return new TileLayers(columns, tiles);
    }

    /// <summary>The layers of the tile whose bottom-left corner is at <paramref name="column"/>, <paramref name="row"/>.</summary>
    public IReadOnlyList<TileLayer> At(int column, int row) => _tiles[(row * (_columns - 1)) + column];

    // The piece a whole tile is drawn in: an 8 by 4 sheet's variations 0 to 15 are its right half, 16 the last piece
    // of its left half, any other its first; a 4 by 4 sheet's first piece for variation 0, its last for any other.
    private static byte WholePiece(bool extended, int variation) => extended
        ? (byte)(variation <= 15 ? 16 + variation : variation == 16 ? 15 : 0)
        : (byte)(variation == 0 ? 0 : 15);
}
