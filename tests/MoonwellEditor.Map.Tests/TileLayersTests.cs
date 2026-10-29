using War3Net.Build.Environment;

namespace MoonwellEditor.Map.Tests;

public sealed class TileLayersTests(ITestOutputHelper output)
{
    [Fact]
    public void Variations_16_and_17_are_as_common_as_0_so_the_ground_variation_has_five_bits()
    {
        var files = InstallGameFiles.Open();
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (files == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var terrain = MapReader.Read(store).Terrain!;
        var (_, tiles) = Cliffs.Of(terrain, files, "sd");
        var columns = (int)terrain.Width + 1;
        var counts = new int[32];
        for (var index = 0; index < terrain.TerrainTiles.Count; index++)
        {
            if (!tiles.Touches(index % columns, index / columns))
            {
                counts[Cliffs.Raw(terrain.TerrainTiles[index]) & 0x1F]++;
            }
        }

        output.WriteLine(string.Join(" ", counts.Select((count, value) => $"{value}:{count}")));
        // World Editor gives a new tile 0, 4, 8, 12, 16 or 17 most often: if bit 4 were a cliff bit (War3Net's split),
        // 16 and 17 would not be as common as 0 on ground away from cliffs.
        Assert.InRange(counts[16], counts[0] * 0.8, counts[0] * 1.2);
        Assert.InRange(counts[17], counts[0] * 0.8, counts[0] * 1.2);
        Assert.True(counts[1] * 4 < counts[0]);
    }

    [Fact]
    public void One_texture_draws_its_variation_piece()
    {
        Assert.Equal([new TileLayer(2, 19)], Layers(variation: 3, extended: true));
        Assert.Equal([new TileLayer(2, 15)], Layers(variation: 16, extended: true));
        Assert.Equal([new TileLayer(2, 0)], Layers(variation: 17, extended: true));
        Assert.Equal([new TileLayer(2, 0)], Layers(variation: 0, extended: false));
        Assert.Equal([new TileLayer(2, 15)], Layers(variation: 5, extended: false));

        static IReadOnlyList<TileLayer> Layers(int variation, bool extended)
        {
            var terrain = Grids.Flat(2, 2);
            foreach (var corner in terrain.TerrainTiles)
            {
                corner.Texture = 2;
            }

            terrain.At(0, 0).Variation = variation & 0x0F;
            terrain.At(0, 0).CliffVariation = variation >> 4;
            return TileLayers.From(terrain, Textures(extended), Flat(terrain)).At(0, 0);
        }
    }

    [Fact]
    public void Mixed_corners_draw_lowest_first_then_by_corner_mask()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(0, 0).Texture = 1; // bottom left
        terrain.At(1, 0).Texture = 3; // bottom right
        terrain.At(0, 1).Texture = 3; // top left
        terrain.At(1, 1).Texture = 1; // top right

        var layers = TileLayers.From(terrain, Textures(extended: false), Flat(terrain)).At(0, 0);

        Assert.Equal([new TileLayer(1, 0), new TileLayer(3, 1 + 8)], layers);
    }

    [Fact]
    public void A_corner_by_a_cliff_takes_its_cliff_types_ground_tile()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.CliffTypes.Add(CliffType.L_Dirt);
        terrain.At(1, 1).CliffLevel = 3;

        var layers = TileLayers.From(terrain, Textures(extended: false), Cliffs.Of(terrain, new NoGameFiles(), "sd").Tiles)
            .At(0, 0);

        Assert.Equal([new TileLayer(3, 0)], layers); // the cliff type's ground tile is texture 3
    }

    [Fact]
    public void A_corner_naming_a_texture_the_map_does_not_list_draws_its_first()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(1, 1).Texture = 40; // the map lists four textures and the blight

        var layers = TileLayers.From(terrain, Textures(extended: false), Flat(terrain)).At(0, 0);

        Assert.Equal([new TileLayer(0, 0)], layers);
    }

    [Fact]
    public void A_blighted_corner_takes_the_blight_texture()
    {
        var terrain = Grids.Flat(2, 2);
        terrain.At(1, 1).IsBlighted = true;

        var layers = TileLayers.From(terrain, Textures(extended: false), Flat(terrain)).At(0, 0);

        Assert.Equal([new TileLayer(0, 0), new TileLayer(4, 4)], layers); // blight is the last texture, 4
    }

    [Fact]
    public void Lost_Temples_textures_are_found()
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
            var textures = TerrainTextures.Read(terrain, files, mode);
            Assert.Equal(terrain.TerrainTypes.Count + 1, textures.All.Count);
            Assert.Equal(terrain.TerrainTypes.Count, textures.Blight);
            Assert.All(textures.All, texture => Assert.NotNull(files.File(texture.Path, mode)));
            Assert.Contains(textures.All, texture => texture.Extended);
            // Outside Classic the ground is the HD textures, the _diffuse files (2048 by 1024 where Classic's are 512).
            Assert.All(textures.All, texture => Assert.Equal(mode != "sd", texture.Path.Contains("_diffuse")));
            Assert.Equal(terrain.CliffTypes.Count, textures.CliffGround.Count);
            Assert.All(textures.CliffGround, ground => Assert.InRange(ground, 0, textures.All.Count - 1));
        }
    }

    // Four textures and the blight, with cliff type 0's ground tile texture 3.
    private static TerrainTextures Textures(bool extended) => new(
        [
            new TerrainTexture("Ldrt", "a", extended), new TerrainTexture("Ldro", "b", extended),
            new TerrainTexture("Ldrg", "c", extended), new TerrainTexture("Lgrs", "d", extended),
            new TerrainTexture("blight", "e", false),
        ],
        4,
        [3]);

    private static CliffTiles Flat(MapEnvironment terrain) => Cliffs.Of(terrain, new NoGameFiles(), "sd").Tiles;
}
