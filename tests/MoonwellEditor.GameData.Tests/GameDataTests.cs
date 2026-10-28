using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwell.GameData;
using MoonwellEditor.Files;
using Xunit.Abstractions;

namespace MoonwellEditor.GameData.Tests;

/// <summary>
/// The game data of the Warcraft III install on this computer (MOONWELL_WAR3, else the usual folder), loaded once into
/// a fresh cache folder (so the first load is a build with Moonwell's reader), for every test of
/// <see cref="GameDataTests"/> and <see cref="InstallDataTests"/>.
/// </summary>
public sealed class GameDataFixture : IDisposable
{
    public GameDataFixture()
    {
        var folder = GameInstall.Find();
        if (folder == null)
        {
            Reason = $"No Warcraft III install in {string.Join(", ", GameInstall.Places())}; set {GameInstall.Variable} to run this test";
            return;
        }

        Folder = folder;
        Cache = Path.Combine(Path.GetTempPath(), "MoonwellEditor.GameData.Tests", Guid.NewGuid().ToString("N"));
        var clock = Stopwatch.StartNew();
        Data = GameData.Load(folder, Cache);
        FirstLoad = clock.Elapsed;
    }

    /// <summary>Why the tests cannot run, or null when <see cref="Data"/> is loaded.</summary>
    public string? Reason { get; }

    /// <summary>The install's folder.</summary>
    public string Folder { get; } = "";

    /// <summary>The cache folder of this run, empty before the first load.</summary>
    public string Cache { get; } = "";

    /// <summary>The data of the first load, or null when the tests cannot run.</summary>
    public GameData? Data { get; }

    /// <summary>How long the first load (the build) took.</summary>
    public TimeSpan FirstLoad { get; }

    public void Dispose()
    {
        if (Cache.Length > 0 && Directory.Exists(Cache))
        {
            Directory.Delete(Cache, true);
        }
    }
}

public sealed class GameDataTests : IClassFixture<GameDataFixture>
{
    private readonly GameDataFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GameDataTests(GameDataFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void Known_values()
    {
        if (Skipped(out var data))
        {
            return;
        }

        Assert.Equal("420", data.Kind(ObjectKind.Unit).Object("hfoo")!.Values["uhpm"]);
        Assert.Equal("30", data.Kind(ObjectKind.Ability).Object("Amls")!.Values["acdn:1"]);
        Assert.Contains(data.Choices["attackType"].Entries, entry => entry.Value == "normal");
    }

    [Fact]
    public void Hero_fields_only_on_heroes()
    {
        if (Skipped(out var data))
        {
            return;
        }

        var units = data.Kind(ObjectKind.Unit);
        var strength = units.Field("ustr")!;
        Assert.True(units.Applies(strength, units.Object("Hpal")!));
        Assert.False(units.Applies(strength, units.Object("hfoo")!));
    }

    [Fact]
    public void A_field_listed_by_the_abilitys_id_belongs_to_it()
    {
        if (Skipped(out var data))
        {
            return;
        }

        // Cargo Capacity (Car1) is listed for Sch2 to Sch5 by their ids, though their code (Amtc) lists no field of its
        // own; World Editor shows it on Sch2 (Sven, 2026-10-28).
        var abilities = data.Kind(ObjectKind.Ability);
        var capacity = abilities.Field("Car1")!;
        Assert.True(abilities.Applies(capacity, abilities.Object("Sch2")!));
        // An ability that neither its id nor its code lists does not get it.
        Assert.False(abilities.Applies(capacity, abilities.Object("AHhb")!));
        // A field listed by the id takes the column from one listed by the code: AIls (code Alsh) has Idps on DataA,
        // not Lsh1.
        var ils = abilities.Object("AIls")!;
        Assert.True(abilities.Applies(abilities.Field("Idps")!, ils));
        Assert.False(abilities.Applies(abilities.Field("Lsh1")!, ils));
    }

    [Fact]
    public void Ability_levels()
    {
        if (Skipped(out var data))
        {
            return;
        }

        var abilities = data.Kind(ObjectKind.Ability);
        Assert.Equal("alev", abilities.LevelField);
        Assert.Equal(3, abilities.Levels(abilities.Object("AHhb")!));
        Assert.Equal("glvl", data.Kind(ObjectKind.Upgrade).LevelField);
        Assert.Null(data.Kind(ObjectKind.Unit).LevelField);
        Assert.Equal(0, data.Kind(ObjectKind.Unit).Levels(data.Kind(ObjectKind.Unit).Object("hfoo")!));
    }

    [Fact]
    public void Counts()
    {
        if (Skipped(out var data))
        {
            return;
        }

        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var kindData = data.Kind(kind);
            Assert.NotEmpty(kindData.Objects);
            Assert.NotEmpty(kindData.Fields);
            var counts = $"{kindData.Objects.Count} objects, {kindData.Fields.Count} fields";
            _output.WriteLine($"{ObjectKinds.Key(kind)}: {counts}");
        }

        Assert.True(data.Kind(ObjectKind.Unit).Objects.Count > 400);
    }

    [Fact]
    public void Graphics_modes_where_they_differ()
    {
        if (Skipped(out var data))
        {
            return;
        }

        // Every per-mode entry has the three modes, they are not all one value, and the standard value is the de one.
        var entries = 0;
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            foreach (var obj in data.Kind(kind).Objects)
            {
                foreach (var (key, modes) in obj.Modes)
                {
                    entries++;
                    Assert.Equal(new[] { "de", "hd", "sd" }, modes.Keys.Order(StringComparer.Ordinal));
                    Assert.False(modes["sd"] == modes["hd"] && modes["hd"] == modes["de"], $"{obj.Id} {key}");
                    Assert.Equal(modes["de"], obj.Values.GetValueOrDefault(key, ""));
                }
            }
        }

        _output.WriteLine($"{entries} values differ between graphics modes");
        var footman = data.Kind(ObjectKind.Unit).Object("hfoo")!;
        Assert.True(footman.Modes.ContainsKey("usca"));
        Assert.NotEqual(footman.Modes["usca"]["sd"], footman.Modes["usca"]["hd"]);
    }

    [Fact]
    public void World_editors_own_lists_and_skin_lists()
    {
        if (Skipped(out var data))
        {
            return;
        }

        // World Editor shows tilesets, and destructable and doodad categories, by name (its screens, 2026-10-27).
        var tilesets = data.Choices["tilesetList"];
        Assert.True(tilesets.Flags);
        Assert.Equal("All", tilesets.Entries.Single(entry => entry.Value == "*").Label);
        Assert.Equal("Ashenvale", tilesets.Entries.Single(entry => entry.Value == "A").Label);
        Assert.Equal("Undercity", tilesets.Entries.Single(entry => entry.Value == "u").Label);
        Assert.Equal("Cityscape Ruins", tilesets.Entries.Single(entry => entry.Value == "P").Label);
        Assert.Equal(
            "Trees/Destructibles",
            data.Choices["destructableCategory"].Entries.Single(entry => entry.Value == "D").Label);
        Assert.Equal("Props", data.Choices["doodadCategory"].Entries.Single(entry => entry.Value == "O").Label);
        var tilesetField = data.Kind(ObjectKind.Destructable).Field("btil")!;
        Assert.Equal(("choice", "tilesetList"), (tilesetField.Type, tilesetField.Choice));
        Assert.Equal("doodadCategory", data.Kind(ObjectKind.Doodad).Field("dcat")!.Choice);

        // A unit's skin lists hold ability ids, which World Editor shows by name.
        var skins = data.Kind(ObjectKind.Unit).Field("uabs")!;
        Assert.Equal(("objectList", "Ability", "abilitySkinList"), (skins.Type, skins.ListOf, skins.Meta));
        Assert.Equal("model", data.Kind(ObjectKind.Unit).Field("umdl")!.Meta);
    }

    [Fact]
    public void Upgrade_effects_and_suffixes()
    {
        if (Skipped(out var data))
        {
            return;
        }

        var upgrades = data.Kind(ObjectKind.Upgrade);
        Assert.Equal(
            [("Base", "Attack Dice Bonus - Base", "int"), ("Mod", "Attack Dice Bonus - Increment", "int")],
            upgrades.Effects["ratd"].Select(field => (field.DataType, field.Label, field.Type)));
        Assert.Equal("float", upgrades.Effects["rats"][0].Type);
        Assert.Equal(("objectRef", "Ability"), (upgrades.Effects["rlev"][2].Type, upgrades.Effects["rlev"][2].ListOf));

        // An upgrade is named by its first level's suffix, as World Editor names Glyph of Fortification (Upgrade 1).
        Assert.Equal("(Upgrade 1)", upgrades.Object("Rgfo")!.Suffix);
    }

    [Fact]
    public void A_suffix_is_the_first_entry_of_its_list_with_its_bracket_closed()
    {
        if (Skipped(out var data))
        {
            return;
        }

        // As World Editor shows them, in the tree and in the Text - Editor Suffix field (Sven, 2026-10-28).
        var units = data.Kind(ObjectKind.Unit);
        Assert.Equal("(ruined)", units.Object("nvr0")!.Suffix);
        Assert.Equal("(Morphed)", units.Object("Eevm")!.Suffix);
        var abilities = data.Kind(ObjectKind.Ability);
        Assert.Equal("(New)", abilities.Object("Afrc")!.Suffix);
        Assert.Equal("(Neutral Hostile 3)", abilities.Object("Awrg")!.Suffix);
    }

    [Fact]
    public void Object_details()
    {
        if (Skipped(out var data))
        {
            return;
        }

        var units = data.Kind(ObjectKind.Unit);
        var footman = units.Object("hfoo")!;
        Assert.Equal("Footman", footman.Name);
        Assert.Equal("human", footman.Race);
        Assert.False(footman.Hero);
        Assert.False(footman.Building);
        Assert.True(units.Object("Hpal")!.Hero);
        Assert.True(units.Object("htow")!.Building);
        Assert.Equal("AHhb", data.Kind(ObjectKind.Ability).Object("AHhb")!.Code);
        var hitPoints = units.Field("uhpm")!;
        Assert.Equal("int", hitPoints.Type);
        Assert.Equal("int", hitPoints.Store);
        Assert.Equal("attackType", units.Field("ua1t")!.Choice);
        Assert.Equal("choice", units.Field("ua1t")!.Type);
    }

    [Fact]
    public void Cache_is_used_second_time()
    {
        if (Skipped(out var data))
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        var cached = GameData.Load(_fixture.Folder, _fixture.Cache);
        var second = clock.Elapsed;
        var first = _fixture.FirstLoad;
        _output.WriteLine($"first load {first.TotalMilliseconds:F0} ms, cached load {second.TotalMilliseconds:F0} ms");
        Assert.True(second.Ticks * 5 <= _fixture.FirstLoad.Ticks, $"first {_fixture.FirstLoad}, second {second}");
        Assert.Equal(Snapshot(data), Snapshot(cached));
    }

    [Fact]
    public void Cached_load_is_fast()
    {
        if (Skipped(out _))
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        GameData.Load(_fixture.Folder, _fixture.Cache);
        var elapsed = clock.Elapsed;
        _output.WriteLine($"cached load {elapsed.TotalMilliseconds:F0} ms");
        Assert.True(elapsed < TimeSpan.FromSeconds(1.5), $"cached load took {elapsed}");
    }

    [Fact]
    public void Cache_of_other_code_or_stamp_is_rebuilt()
    {
        if (Skipped(out _))
        {
            return;
        }

        // Each copy of the cache gives the footman 999 hit points: a load that uses it says 999, a build says 420.
        // Left otherwise as written, the copy is used, so the test tells using a cache from building again.
        Assert.Equal("999", FootmanHitPoints(_ => { }));
        Assert.Equal("420", FootmanHitPoints(cache => cache["Code"] = Guid.NewGuid().ToString("N")));
        Assert.Equal("420", FootmanHitPoints(cache => cache["Stamp"] = "build 00000000000000000000000000000000"));
    }

    /// <summary>
    /// The footman's hit points after a load from a copy of the fixture's cache with the footman's hit points set to
    /// 999 and <paramref name="change"/> made to it.
    /// </summary>
    private string FootmanHitPoints(Action<JsonObject> change)
    {
        var folder = Path.Combine(Path.GetTempPath(), "MoonwellEditor.GameData.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Directory.GetFiles(_fixture.Cache, "*.json").Single();
            var cache = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
            var units = cache["Kinds"]!["unit"]!["Objects"]!.AsArray();
            var footman = units.Single(unit => (string?)unit!["Id"] == "hfoo")!;
            footman["Values"]!["uhpm"] = "999";
            change(cache);
            File.WriteAllText(Path.Combine(folder, Path.GetFileName(source)), cache.ToJsonString());
            return GameData.Load(_fixture.Folder, folder).Kind(ObjectKind.Unit).Object("hfoo")!.Values["uhpm"];
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private bool Skipped(out GameData data)
    {
        if (_fixture.Data == null)
        {
            _output.WriteLine(_fixture.Reason);
            data = null!;
            return true;
        }

        data = _fixture.Data;
        return false;
    }

    /// <summary>Everything a <see cref="GameData"/> holds, as JSON, for comparing two loads.</summary>
    /// <summary>The data as JSON, with the folder it was read from unless <paramref name="source"/> is false.</summary>
    internal static string Snapshot(GameData data, bool source = true)
    {
        var kinds = Enum.GetValues<ObjectKind>().Select(kind => new
        {
            Kind = ObjectKinds.Key(kind),
            data.Kind(kind).LevelField,
            data.Kind(kind).Fields,
            data.Kind(kind).Objects,
        });
        return source
            ? JsonSerializer.Serialize(new { data.Source, data.Choices, Kinds = kinds })
            : JsonSerializer.Serialize(new { data.Choices, Kinds = kinds });
    }
}
