namespace MoonwellEditor.Map.Tests;

public sealed class CliffsTests(ITestOutputHelper output)
{
    [Fact]
    public void A_tile_one_level_higher_at_its_top_left_is_CliffsBAAA()
    {
        var files = InstallGameFiles.Open();
        if (files == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var terrain = Grids.Flat(2, 2);
        terrain.CliffTypes.Add(War3Net.Build.Environment.CliffType.L_Dirt);
        terrain.At(0, 1).CliffLevel = 3;

        var (pieces, tiles) = Cliffs.Of(terrain, files, "sd");

        var piece = Assert.Single(pieces);
        Assert.Equal(@"Doodads\Terrain\Cliffs\CliffsBAAA0.mdx", piece.Model);
        Assert.Equal((0, 0), (piece.Column, piece.Row));
        Assert.Equal(0f, piece.BaseZ);
        Assert.True(tiles.Covered(0, 0));
        Assert.True(tiles.Touches(0, 0));
        Assert.True(tiles.Touches(1, 1));
    }

    [Fact]
    public void A_ramp_takes_CliffTrans_models_at_its_sides()
    {
        var files = InstallGameFiles.Open();
        if (files == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        // A ramp up along y, from level 2 (rows 0 and 1) to 3 (row 2); its middle columns, 1 and 2, are ramp-flagged.
        var terrain = Grids.Flat(4, 3);
        terrain.CliffTypes.Add(War3Net.Build.Environment.CliffType.L_Dirt);
        for (var column = 0; column < 4; column++)
        {
            terrain.At(column, 2).CliffLevel = 3;
            terrain.At(column, 0).IsRamp = column is 1 or 2;
            terrain.At(column, 1).IsRamp = column is 1 or 2;
            terrain.At(column, 2).IsRamp = column is 1 or 2;
        }

        var (pieces, tiles) = Cliffs.Of(terrain, files, "sd");

        Assert.Equal(
            [(0, 0, @"Doodads\Terrain\CliffTrans\CliffTransBHLA0.mdx"), (2, 0, @"Doodads\Terrain\CliffTrans\CliffTransHBAL0.mdx")],
            pieces.Select(piece => (piece.Column, piece.Row, piece.Model)).ToArray());
        Assert.True(tiles.Covered(0, 0)); // under the side's model
        Assert.True(tiles.Covered(0, 1));
        Assert.False(tiles.Covered(1, 1)); // the ramp's middle is ground
        Assert.False(tiles.Covered(1, 0));
    }

    [Fact]
    public void BaseZ_is_the_lowest_cliff_level()
    {
        var files = InstallGameFiles.Open();
        if (files == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var terrain = Grids.Flat(2, 2);
        terrain.CliffTypes.Add(War3Net.Build.Environment.CliffType.L_Dirt);
        foreach (var tile in terrain.TerrainTiles)
        {
            tile.CliffLevel = 4;
        }

        terrain.At(1, 1).CliffLevel = 5;

        var piece = Assert.Single(Cliffs.Of(terrain, files, "sd").Pieces);

        Assert.Equal(256f, piece.BaseZ);
        Assert.Equal(@"Doodads\Terrain\Cliffs\CliffsABAA0.mdx", piece.Model);
    }

    [Theory]
    [InlineData(InstallMaps.LostTemple)]
    [InlineData(InstallMaps.Stromguarde)]
    [InlineData(InstallMaps.WarChasers)]
    [InlineData(InstallMaps.EkrezemsMaze)]
    public void Every_cliff_and_ramp_finds_its_model(string map)
    {
        var files = InstallGameFiles.Open();
        var store = InstallMaps.Open(map);
        if (files == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var terrain = MapReader.Read(store).Terrain!;
        foreach (var mode in new[] { "sd", "hd" })
        {
            var (pieces, _) = Cliffs.Of(terrain, files, mode);
            var missing = pieces.Select(piece => piece.Model).Distinct()
                .Where(model => files.File(model, mode) == null).ToList();
            output.WriteLine($"{mode}: {pieces.Count} pieces ({pieces.Count(piece => piece.Model.Contains("Trans"))} ramp sides), {pieces.Select(piece => piece.Model).Distinct().Count()} models");
            Assert.Empty(missing);
            Assert.NotEmpty(pieces);
        }
    }

    [Fact]
    public void Each_cliff_type_has_its_texture()
    {
        var files = InstallGameFiles.Open();
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (files == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var terrain = MapReader.Read(store).Terrain!;
        foreach (var mode in new[] { "sd", "hd" })
        {
            var textures = Cliffs.Textures(terrain, files, mode);
            Assert.Equal(terrain.CliffTypes.Count, textures.Count);
            Assert.All(textures, texture => Assert.NotNull(files.File(texture, mode)));
        }
    }
}
