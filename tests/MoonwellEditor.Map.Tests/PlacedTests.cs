using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using MoonwellEditor.Files;
using War3Net.Build.Widget;
using War3Net.Common.Extensions;

namespace MoonwellEditor.Map.Tests;

public sealed partial class PlacedTests(ITestOutputHelper output)
{
    private const float WaterLevel = -89.6f;

    [Theory]
    [InlineData(InstallMaps.WarChasers, "war3map.j")]
    [InlineData(InstallMaps.ForgottenHollow, "war3map.lua")]
    public void Every_name_in_World_Editors_script_is_made(string map, string script)
    {
        var store = InstallMaps.Open(map);
        var session = store == null ? null : TestGameData.Open(store);
        if (store == null || session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var files = MapReader.Read(store);
        var grid = TerrainGrid.From(files.Terrain!, WaterLevel);
        var made = Placed.Things(files, grid, session, "sd").Select(thing => thing.Ref)
            .Concat(Placed.Regions(files.Regions).Select(region => region.Ref))
            .Concat(Placed.Cameras(files.Cameras).Select(camera => camera.Ref))
            .ToHashSet();
        var named = Names().Matches(Encoding.UTF8.GetString(store.Read(script)!))
            .Select(match => match.Groups[1].Value).Distinct().ToList();

        output.WriteLine($"{named.Count} names in the script, {made.Count} made");
        Assert.NotEmpty(named);
        var unmade = named.Where(name => !made.Contains(name)).ToList();
        Assert.True(unmade.Count == 0, "Not made: " + string.Join(", ", unmade.Take(20)));
    }

    [Theory]
    [InlineData(InstallMaps.LostTemple)]
    [InlineData(InstallMaps.Stromguarde)]
    [InlineData(InstallMaps.EkrezemsMaze)]
    [InlineData(InstallMaps.WarChasers)]
    [InlineData(InstallMaps.ForgottenHollow)]
    public void Every_placed_thing_finds_its_model(string map)
    {
        var files = InstallGameFiles.Open();
        var store = InstallMaps.Open(map);
        var session = store == null ? null : TestGameData.Open(store);
        if (files == null || store == null || session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var read = MapReader.Read(store);
        var grid = TerrainGrid.From(read.Terrain!, WaterLevel);
        foreach (var mode in new[] { "sd", "hd" })
        {
            var things = Placed.Things(read, grid, session, mode);
            // A model the map's own data names may be missing (Forgotten Hollow's 3DPoint.mdl); a standard one may not.
            var missing = things.Where(thing => thing.Model != null && !Found(thing.Model, mode) && !OwnModel(thing))
                .Select(thing => thing.Model!).Distinct().ToList();
            output.WriteLine($"{mode}: {things.Count} things, {things.Count(thing => thing.Model == null)} markers"
                + (missing.Count > 0 ? "; missing " + string.Join(", ", missing.Take(10)) : ""));
            Assert.NotEmpty(things);
            Assert.Empty(missing);
        }

        // The game's file, or the map's import (named .mdl in object data, kept as .mdx).
        bool Found(string model, string mode) => files.File(model, mode) != null || store.ReadImport(model) != null
            || store.ReadImport(Path.ChangeExtension(model, ".mdx")) != null;

        bool OwnModel(PlacedThing thing)
        {
            var (kind, field) = thing.Kind switch
            {
                ThingKind.Item => (ObjectKind.Item, "ifil"),
                ThingKind.Destructable => (ObjectKind.Destructable, "bfil"),
                ThingKind.Doodad => (ObjectKind.Doodad, "dfil"),
                _ => (ObjectKind.Unit, "umdl"),
            };
            return session.Object(kind, thing.TypeId)?.Fields.Any(each => each.Id == field && each.Changed) == true;
        }
    }

    [Fact]
    public void A_type_the_map_no_longer_has_is_a_marker()
    {
        using var folder = new TempFolder();
        var session = TestGameData.Open(MapFolder.Open(folder.Path));
        if (session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var units = Units(Unit("X000"), Unit("uDNR"), Item("iDNR"));

        var things = Placed.Things(Files(units: units), Flat(), session, "sd");

        Assert.All(things, thing => Assert.Null(thing.Model));
        Assert.Equal(["X000", "uDNR", "iDNR"], things.Select(thing => thing.Name));
        Assert.Equal([ThingKind.Unit, ThingKind.Unit, ThingKind.Item], things.Select(thing => thing.Kind));
    }

    [Fact]
    public void An_imported_model_is_named()
    {
        using var folder = new TempFolder();
        var session = TestGameData.Open(MapFolder.Open(folder.Path));
        if (session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        session.Create(ObjectKind.Unit, "hfoo", "h000");
        session.Set(ObjectKind.Unit, "h000", "umdl", 0, @"war3mapImported\Box.mdx");

        var thing = Assert.Single(Placed.Things(Files(units: Units(Unit("h000"))), Flat(), session, "sd"));

        Assert.Equal(@"war3mapImported\Box.mdx", thing.Model);
        Assert.Equal("gg_unit_h000_0001", thing.Ref);
    }

    [Fact]
    public void A_fixed_rotation_bridge_stands_where_the_game_puts_it()
    {
        using var folder = new TempFolder();
        var session = TestGameData.Open(MapFolder.Open(folder.Path));
        if (session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var doodads = new MapDoodads(MapWidgetsFormatVersion.v8, MapWidgetsSubVersion.v11, true);
        doodads.Doodads.Add(new DoodadData
        {
            TypeId = "YT16".FromRawcode(),
            SkinId = "YT16".FromRawcode(),
            Position = new Vector3(0, 0, 0),
            Rotation = MathF.PI / 2,
            Scale = Vector3.One,
            State = DoodadState.Normal,
            CreationNumber = 4,
        });
        var fixedRotation = float.Parse(
            session.Object(ObjectKind.Destructable, "YT16")!.Fields.First(field => field.Id == "bfxr").Value,
            System.Globalization.CultureInfo.InvariantCulture);

        var bridge = Assert.Single(Placed.Things(Files(doodads: doodads), Flat(), session, "sd"));

        Assert.Equal(ThingKind.Destructable, bridge.Kind);
        Assert.Equal(32f, bridge.X);
        Assert.Equal(0f, bridge.Y);
        Assert.Equal(fixedRotation * MathF.PI / 180f, bridge.Facing, 4);
        Assert.Equal("gg_dest_YT16_0004", bridge.Ref);
    }

    [Fact]
    public void A_thing_keeps_its_variation()
    {
        using var folder = new TempFolder();
        var session = TestGameData.Open(MapFolder.Open(folder.Path));
        if (session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var doodads = new MapDoodads(MapWidgetsFormatVersion.v8, MapWidgetsSubVersion.v11, true);
        doodads.Doodads.Add(new DoodadData
        {
            TypeId = "NTtw".FromRawcode(), SkinId = "NTtw".FromRawcode(), Variation = 3, Scale = Vector3.One,
            State = DoodadState.Normal, CreationNumber = 1,
        });

        var tree = Assert.Single(Placed.Things(Files(doodads: doodads), Flat(), session, "sd"));

        Assert.Equal(3, tree.Variation);
    }

    [Fact]
    public void Units_stand_on_the_ground_plus_their_fly_height()
    {
        using var folder = new TempFolder();
        var session = TestGameData.Open(MapFolder.Open(folder.Path));
        if (session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var terrain = Grids.Flat(2, 2);
        foreach (var corner in terrain.TerrainTiles)
        {
            corner.Height = 1;
        }

        var flyHeight = float.Parse(
            session.Object(ObjectKind.Unit, "hgry")!.Fields.First(field => field.Id == "umvh").Value,
            System.Globalization.CultureInfo.InvariantCulture);

        var gryphon = Assert.Single(Placed.Things(
            Files(units: Units(Unit("hgry"))), TerrainGrid.From(terrain, WaterLevel), session, "sd"));

        Assert.True(flyHeight > 0);
        Assert.Equal(128f + flyHeight, gryphon.Z, 3);
    }

    [Fact]
    public void Region_and_camera_names_become_identifiers()
    {
        Assert.Equal("gg_unit_hfoo_0001", TriggerNames.Unit("hfoo", 1));
        Assert.Equal("gg_item_ratf_0012", TriggerNames.Item("ratf", 12));
        Assert.Equal("gg_dest_LTlt_0004", TriggerNames.Destructable("LTlt", 4));
        Assert.Equal("gg_rct_Spawn_Point", TriggerNames.Region("Spawn Point"));
        Assert.Equal("gg_cam_Intro", TriggerNames.Camera("Intro"));
    }

    // A name World Editor's script gives a placed thing: assigned something other than nil or null (the map's own code
    // may name things the map does not have, as Forgotten Hollow's elevator regions, checked for nil).
    [GeneratedRegex(@"\b(gg_(?:unit|item|dest|rct|cam)_\w+)\s*=\s*(?!nil\b|null\b)")]
    private static partial Regex Names();

    private static TerrainGrid Flat() => TerrainGrid.From(Grids.Flat(2, 2), WaterLevel);

    private static MapFiles Files(MapUnits? units = null, MapDoodads? doodads = null) =>
        new(null, doodads, units, null, null, null, []);

    private static MapUnits Units(params UnitData[] units)
    {
        var file = new MapUnits(MapWidgetsFormatVersion.v8, MapWidgetsSubVersion.v11, true);
        var number = 1;
        foreach (var unit in units)
        {
            unit.CreationNumber = number++;
            file.Units.Add(unit);
        }

        return file;
    }

    private static UnitData Unit(string id) => new()
    {
        TypeId = id.FromRawcode(),
        SkinId = id.FromRawcode(),
        Position = new Vector3(64, 64, 0),
        Scale = Vector3.One,
        OwnerId = 0,
        HeroLevel = 1,
    };

    private static UnitData Item(string id)
    {
        var item = Unit(id);
        item.HeroLevel = 0;
        return item;
    }
}
