using War3Net.Build.Common;
using War3Net.Build.Environment;

namespace MoonwellEditor.Map.Tests;

/// <summary>Small terrains made in the tests: flat ground at cliff level 2, height 0, no water, texture 0.</summary>
public static class Grids
{
    /// <summary>A terrain of <paramref name="columns"/> by <paramref name="rows"/> corners, its bottom left at 0, 0.</summary>
    public static MapEnvironment Flat(int columns, int rows)
    {
        var terrain = new MapEnvironment(MapEnvironmentFormatVersion.v12)
        {
            Tileset = Tileset.LordaeronSummer,
            Width = (uint)(columns - 1),
            Height = (uint)(rows - 1),
            Left = 0,
            Bottom = 0,
        };
        for (var i = 0; i < columns * rows; i++)
        {
            terrain.TerrainTiles.Add(new TerrainTile { CliffLevel = 2, Height = 0, WaterHeight = 0 });
        }

        return terrain;
    }

    /// <summary>The corner at <paramref name="column"/>, <paramref name="row"/>.</summary>
    public static TerrainTile At(this MapEnvironment terrain, int column, int row) =>
        terrain.TerrainTiles[(row * ((int)terrain.Width + 1)) + column];
}
