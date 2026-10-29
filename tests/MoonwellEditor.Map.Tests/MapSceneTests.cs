using System.Numerics;
using MoonwellEditor.Files;
using War3Net.Build.Extensions;
using War3Net.Build.Widget;
using War3Net.Common.Extensions;

namespace MoonwellEditor.Map.Tests;

public sealed class MapSceneTests(ITestOutputHelper output)
{
    [Fact]
    public void TerrainBytes_round_trip()
    {
        var terrain = Grids.Flat(3, 3);
        terrain.At(1, 1).Height = 0.5f;
        terrain.At(2, 2).CliffLevel = 3;
        terrain.At(0, 0).IsWater = true;
        terrain.At(0, 0).Height = -1;
        terrain.At(0, 0).IsEdgeTile = true;
        terrain.At(1, 0).IsBoundary = true;
        terrain.At(0, 1).Texture = 1;
        var grid = TerrainGrid.From(terrain, -89.6f);
        var cliffs = Cliffs.Of(terrain, new NoGameFiles(), "sd").Tiles;
        var textures = new TerrainTextures(
            [new TerrainTexture("Ldrt", "a", false), new TerrainTexture("Ldro", "b", false), new TerrainTexture("blight", "c", false)],
            2,
            []);
        var layers = TileLayers.From(terrain, textures, cliffs);

        var bytes = TerrainBytes.Write(grid, layers, cliffs);

        using var reader = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        var ground = Floats(reader, 9);
        var relief = Floats(reader, 9);
        var water = Floats(reader, 9);
        var layerBytes = reader.ReadBytes(4 * 8);
        var tileBytes = reader.ReadBytes(4);
        Assert.Equal(bytes.Length, reader.BaseStream.Position);
        Assert.Equal(64f, ground[4]);
        Assert.Equal(128f, ground[8]);
        Assert.Equal(64f, relief[4]);
        Assert.Equal(0f, relief[8]);
        Assert.Equal(-89.6f, water[0], 3);
        Assert.True(float.IsNaN(water[1]));
        Assert.Equal([0, 0, 1, 8, 255, 255, 255, 255], layerBytes[..8].Select(b => (int)b)); // tile 0, 0
        Assert.Equal(0b011, tileBytes[0]); // drawn, outside the playable area
        Assert.Equal(0b101, tileBytes[1]); // drawn, boundary
        Assert.Equal(0b000, tileBytes[3]); // under the cliff at its top right
    }

    [Fact]
    public void The_scene_follows_the_session()
    {
        var files = InstallGameFiles.Open();
        var lostTemple = InstallMaps.Open(InstallMaps.LostTemple);
        if (files == null || lostTemple == null || TestGameData.Data == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        using var folder = new TempFolder();
        folder.Write("war3map.w3e", lostTemple.Read("war3map.w3e")!);
        var units = new MapUnits(MapWidgetsFormatVersion.v8, MapWidgetsSubVersion.v11, true);
        foreach (var (id, number) in new[] { ("hfoo", 1), ("h000", 2) })
        {
            units.Units.Add(new UnitData
            {
                TypeId = id.FromRawcode(), SkinId = id.FromRawcode(), Position = new Vector3(0, 0, 0),
                Scale = Vector3.One, HeroLevel = 1, CreationNumber = number,
            });
        }

        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(units);
            }

            folder.Write("war3mapUnits.doo", stream.ToArray());
        }

        var store = MapFolder.Open(folder.Path);
        var session = TestGameData.Open(store)!;
        session.Create(ObjectKind.Unit, "hfoo", "h000");

        var before = MapScene.Build(store, session, files, "sd").View;
        session.Set(ObjectKind.Unit, "hfoo", "umdl", 0, @"units\human\Knight\Knight.mdl");
        session.Delete(ObjectKind.Unit, "h000");
        var after = MapScene.Build(store, session, files, "sd").View;

        Assert.NotNull(before.Things[1].Model);
        Assert.Equal(@"units\human\Knight\Knight.mdl", after.Things[0].Model);
        Assert.Null(after.Things[1].Model);
    }

    [Fact]
    public void A_map_without_terrain_says_so()
    {
        var files = InstallGameFiles.Open();
        using var folder = new TempFolder();
        var store = MapFolder.Open(folder.Path);
        var session = TestGameData.Open(store);
        if (files == null || session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var (view, terrain) = MapScene.Build(store, session, files, "sd");

        Assert.Equal(0, view.Columns);
        Assert.Empty(terrain);
        Assert.Contains(view.Problems, problem => problem.Contains("war3map.w3e"));
    }

    [Fact]
    public void Lost_Temples_scene_is_whole()
    {
        var files = InstallGameFiles.Open();
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        var session = store == null ? null : TestGameData.Open(store);
        if (files == null || store == null || session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var (view, terrain) = MapScene.Build(store, session, files, "hd");

        Assert.Equal(161, view.Columns);
        Assert.Single(view.Tileset); // Lost Temple's is I
        Assert.Equal((8 + (161 * 161 * 12)) + (160 * 160 * 9), terrain.Length);
        Assert.NotEmpty(view.Cliffs);
        Assert.NotEmpty(view.Things);
        Assert.Equal(@"ReplaceableTextures\Water\Water", view.Water.Texture);
        Assert.True(view.Water.Frames > 1);
        Assert.Empty(view.Problems);
    }

    [Fact]
    public void A_things_kind_goes_over_the_bridge_by_name()
    {
        var thing = new PlacedThing(1, ThingKind.StartLocation, "sloc", "", null, 0, 0, 0, 0, 1, 1, 1, null, null, 0, 0);

        var json = System.Text.Json.JsonSerializer.Serialize(
            thing, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Contains("\"kind\":\"StartLocation\"", json);
    }

    private static float[] Floats(BinaryReader reader, int count) =>
        Enumerable.Range(0, count).Select(_ => reader.ReadSingle()).ToArray();
}
