using War3Net.Build.Environment;

namespace MoonwellEditor.Map;

/// <summary>
/// The terrain's corners, one every 128, as heights the game reads (docs/game-facts.md, 10-25): a corner's ground is
/// its height and cliff level, <c>Height × 128 + (CliffLevel − 2) × 128</c>, and a ramp-flagged corner is raised 32 for
/// each of the (up to four) tiles it is a corner of that has another ramp-flagged corner exactly one cliff level higher
/// (64 for one along x or y, 32 for one only diagonal, 96 for an L of them; the rule Moonwell's build uses, which fits
/// all 15,625 corners of Lost Temple's playable area). Water stands at <c>WaterHeight × 128</c> plus the tileset's
/// water level (−89.6), and counts only where that is above the ground. Corners run row by row from the bottom left.
/// </summary>
public sealed class TerrainGrid
{
    private readonly float[] _ground;
    private readonly float[] _relief;
    private readonly float[] _water;
    private readonly bool[] _edge;
    private readonly bool[] _boundary;

    private TerrainGrid(int columns, int rows, float left, float bottom)
    {
        Columns = columns;
        Rows = rows;
        Left = left;
        Bottom = bottom;
        _ground = new float[columns * rows];
        _relief = new float[columns * rows];
        _water = new float[columns * rows];
        _edge = new bool[columns * rows];
        _boundary = new bool[columns * rows];
    }

    /// <summary>Corners a row: War3Net's <see cref="MapEnvironment.Width"/> counts tiles, one fewer.</summary>
    public int Columns { get; }

    /// <summary>Rows of corners: War3Net's <see cref="MapEnvironment.Height"/> counts tiles, one fewer.</summary>
    public int Rows { get; }

    /// <summary>The x of the first column of corners.</summary>
    public float Left { get; }

    /// <summary>The y of the first row of corners.</summary>
    public float Bottom { get; }

    /// <summary>
    /// The grid of <paramref name="terrain"/>, with water at its height plus <paramref name="waterLevel"/> (the
    /// tileset's <c>TerrainArt\Water.slk</c> <c>height</c> × 128: −0.7 × 128 = −89.6).
    /// </summary>
    public static TerrainGrid From(MapEnvironment terrain, float waterLevel)
    {
        var grid = new TerrainGrid((int)terrain.Width + 1, (int)terrain.Height + 1, terrain.Left, terrain.Bottom);
        var tiles = terrain.TerrainTiles;
        for (var index = 0; index < grid._ground.Length; index++)
        {
            var tile = tiles[index];
            var ground = (tile.Height * 128f) + ((tile.CliffLevel - 2) * 128f)
                + (32f * grid.HigherRampTiles(tiles, index));
            var surface = (tile.WaterHeight * 128f) + waterLevel;
            grid._ground[index] = ground;
            grid._relief[index] = tile.Height * 128f;
            grid._water[index] = tile.IsWater && surface > ground ? surface : float.NaN;
            grid._edge[index] = tile.IsEdgeTile;
            grid._boundary[index] = tile.IsBoundary;
        }

        return grid;
    }

    /// <summary>The ground's height at a corner, ramps raised, whatever water is above it.</summary>
    public float Ground(int column, int row) => _ground[Index(column, row)];

    /// <summary>The corner's height without its cliff level (<c>Height × 128</c>): what a cliff model stands on.</summary>
    public float Relief(int column, int row) => _relief[Index(column, row)];

    /// <summary>The water's surface at a corner; null where there is none, or it is not above the ground.</summary>
    public float? Water(int column, int row)
    {
        var water = _water[Index(column, row)];
        return float.IsNaN(water) ? null : water;
    }

    /// <summary>Whether the corner is outside the playable area (War3Net's <see cref="TerrainTile.IsEdgeTile"/>).</summary>
    public bool Edge(int column, int row) => _edge[Index(column, row)];

    /// <summary>Whether the corner has the boundary flag.</summary>
    public bool Boundary(int column, int row) => _boundary[Index(column, row)];

    /// <summary>
    /// The ground's height at a point: the bilinear blend of its tile's four corners, as <c>GetLocationZ</c> blends
    /// them (docs/game-facts.md, 10-28). A point outside the grid reads its edge.
    /// </summary>
    public float GroundAt(float x, float y)
    {
        var u = Math.Clamp((x - Left) / 128f, 0f, Columns - 1);
        var v = Math.Clamp((y - Bottom) / 128f, 0f, Rows - 1);
        var i = Math.Min((int)MathF.Floor(u), Math.Max(Columns - 2, 0));
        var j = Math.Min((int)MathF.Floor(v), Math.Max(Rows - 2, 0));
        var right = Math.Min(i + 1, Columns - 1);
        var top = Math.Min(j + 1, Rows - 1);
        var fx = u - i;
        var fy = v - j;
        var z00 = Ground(i, j);
        var z10 = Ground(right, j);
        var z01 = Ground(i, top);
        var z11 = Ground(right, top);
        return ((1f - fy) * (((1f - fx) * z00) + (fx * z10))) + (fy * (((1f - fx) * z01) + (fx * z11)));
    }

    private int Index(int column, int row) => (row * Columns) + column;

    // How many of the (up to four) tiles a ramp corner is a corner of have another ramp corner one cliff level up. A
    // tile off the grid counts by its corners on the grid, as Moonwell's build counts it.
    private int HigherRampTiles(IReadOnlyList<TerrainTile> tiles, int index)
    {
        var tile = tiles[index];
        if (!tile.IsRamp)
        {
            return 0;
        }

        var column = index % Columns;
        var row = index / Columns;
        var count = 0;
        for (var tileRow = row - 1; tileRow <= row; tileRow++)
        {
            for (var tileColumn = column - 1; tileColumn <= column; tileColumn++)
            {
                if (TileHasHigherRamp(tiles, tile, column, row, tileColumn, tileRow))
                {
                    count++;
                }
            }
        }

        return count;
    }

    // The tile whose bottom-left corner is tileColumn, tileRow: is one of its other corners a ramp one level higher?
    private bool TileHasHigherRamp(
        IReadOnlyList<TerrainTile> tiles, TerrainTile tile, int column, int row, int tileColumn, int tileRow)
    {
        for (var y = tileRow; y <= tileRow + 1; y++)
        {
            for (var x = tileColumn; x <= tileColumn + 1; x++)
            {
                if ((x == column && y == row) || x < 0 || x >= Columns || y < 0 || y >= Rows)
                {
                    continue;
                }

                var other = tiles[(y * Columns) + x];
                if (other.IsRamp && other.CliffLevel == tile.CliffLevel + 1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
