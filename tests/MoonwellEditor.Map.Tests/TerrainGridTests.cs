namespace MoonwellEditor.Map.Tests;

public sealed class TerrainGridTests
{
    private const float WaterLevel = -89.6f;

    [Fact]
    public void Ground_is_height_and_cliff_level()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(1, 1).Height = 0.5f;
        terrain.At(1, 1).CliffLevel = 3;

        var grid = TerrainGrid.From(terrain, WaterLevel);

        Assert.Equal(2, grid.Columns);
        Assert.Equal(2, grid.Rows);
        Assert.Equal(192f, grid.Ground(1, 1));
        Assert.Equal(64f, grid.Relief(1, 1));
        Assert.Equal(0f, grid.Ground(0, 0));
    }

    [Fact]
    public void A_ramp_corner_rises_32_for_each_tile_with_a_higher_ramp_corner()
    {
        // The corner at 1, 1 is a ramp at level 2; around it, ramp corners one level up.
        var beside = Grids.Flat(3, 3);
        beside.At(1, 1).IsRamp = true;
        beside.At(2, 1).IsRamp = true;
        beside.At(2, 1).CliffLevel = 3;

        var diagonal = Grids.Flat(3, 3);
        diagonal.At(1, 1).IsRamp = true;
        diagonal.At(2, 2).IsRamp = true;
        diagonal.At(2, 2).CliffLevel = 3;

        var ell = Grids.Flat(3, 3);
        ell.At(1, 1).IsRamp = true;
        foreach (var (column, row) in new[] { (2, 1), (2, 2), (1, 2) })
        {
            ell.At(column, row).IsRamp = true;
            ell.At(column, row).CliffLevel = 3;
        }

        Assert.Equal(64f, TerrainGrid.From(beside, WaterLevel).Ground(1, 1));
        Assert.Equal(32f, TerrainGrid.From(diagonal, WaterLevel).Ground(1, 1));
        Assert.Equal(96f, TerrainGrid.From(ell, WaterLevel).Ground(1, 1));
    }

    [Fact]
    public void Water_stands_at_its_height_less_89_6()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(0, 0).IsWater = true;
        terrain.At(0, 0).Height = -1;
        terrain.At(1, 0).IsWater = true;
        terrain.At(1, 0).Height = 0;

        var grid = TerrainGrid.From(terrain, WaterLevel);

        Assert.Equal(-89.6f, grid.Water(0, 0)!.Value, 3);
        Assert.Null(grid.Water(1, 0)); // the ground at 0 is above the surface
        Assert.Null(grid.Water(0, 1)); // no water flag
    }

    [Fact]
    public void GroundAt_blends_the_four_corners()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(1, 0).Height = 1;
        terrain.At(1, 1).Height = 1;

        var grid = TerrainGrid.From(terrain, WaterLevel);

        Assert.Equal(64f, grid.GroundAt(64, 64), 3);
        Assert.Equal(32f, grid.GroundAt(32, 100), 3);
        Assert.Equal(128f, grid.GroundAt(500, 500), 3); // outside reads the edge
    }

    [Fact]
    public void Edge_and_boundary_are_the_corners_flags()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(0, 0).IsEdgeTile = true;
        terrain.At(1, 1).IsBoundary = true;

        var grid = TerrainGrid.From(terrain, WaterLevel);

        Assert.True(grid.Edge(0, 0));
        Assert.False(grid.Edge(1, 1));
        Assert.True(grid.Boundary(1, 1));
        Assert.False(grid.Boundary(0, 0));
    }
}
