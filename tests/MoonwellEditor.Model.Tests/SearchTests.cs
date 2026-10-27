using System.Diagnostics;
using MoonwellEditor.Files;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class SearchTests(GameDataFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void Id_exact_first()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        var hits = session.Search("hfoo", 10);

        Assert.NotEmpty(hits);
        Assert.Equal(new Hit("unit", "hfoo", "Footman", "Human · Melee · Units", null), hits[0]);

        // The custom object of the map is found by its id too, and a limit cuts the list.
        Assert.Equal("h000", session.Search("H000", 10)[0].Id);
        Assert.Single(session.Search("foot", 1));
        Assert.Empty(session.Search("", 10));
        Assert.Empty(session.Search("   ", 10));
        Assert.Empty(session.Search("foot", 0));
    }

    [Fact]
    public void Order_is_id_prefix_contains_field_and_custom_first()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var custom = session.Create(ObjectKind.Unit, "hfoo", null);
        session.Set(ObjectKind.Unit, custom, "unam", 0, "Footman Deluxe");

        var hits = session.Search("footman", 500).Where(hit => hit.Kind == "unit" && hit.Field == null).ToList();

        // Name prefix: the custom one first at equal rank, then the standard Footman.
        Assert.Equal(custom, hits[0].Id);
        Assert.Equal("hfoo", hits[1].Id);

        // A name that only contains the word comes after the names that start with it.
        var all = session.Search("man", 2000).Where(hit => hit.Field == null).ToList();
        var firstContains = all.FindIndex(hit => !hit.Name.StartsWith("man", StringComparison.OrdinalIgnoreCase));
        var lastPrefix = all.FindLastIndex(hit => hit.Name.StartsWith("man", StringComparison.OrdinalIgnoreCase));
        Assert.True(lastPrefix < firstContains || firstContains < 0, "names starting with the word come first");

        // Field matches come after every object match.
        var mixed = session.Search("hit", 5000);
        var firstField = mixed.ToList().FindIndex(hit => hit.Field != null);
        Assert.True(firstField >= 0);
        Assert.All(mixed.Skip(firstField), hit => Assert.NotNull(hit.Field));
    }

    [Fact]
    public void Words_all_match()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        var both = session.Search("foot hum", 50);
        Assert.Contains(both, hit => hit.Kind == "unit" && hit.Id == "hfoo");
        Assert.DoesNotContain(both, hit => hit.Name.Contains("Rifleman"));

        // Every word has to match, in any order.
        Assert.Contains(session.Search("hum foot", 50), hit => hit.Id == "hfoo");
        Assert.DoesNotContain(session.Search("foot zzzzqq", 50), hit => hit.Id == "hfoo");
        Assert.DoesNotContain(session.Search("foot orc", 50), hit => hit.Id == "hfoo");
        Assert.Contains(session.Search("FOOT", 50), hit => hit.Id == "hfoo");
    }

    [Fact]
    public void Finds_field_labels()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var hitPoints = game.Kind(ObjectKind.Unit).Field("uhpm")!;

        var hits = session.Search("hit points", 2000);

        var field = Assert.Single(hits, hit => hit.Kind == "unit" && hit.Field == "uhpm");
        Assert.Equal("uhpm", field.Id);
        Assert.Equal(hitPoints.Label, field.Name);
        Assert.Equal($"{hitPoints.Label} · uhpm", field.Detail);

        // One hit for each field of each kind, not for each object.
        var fields = hits.Where(hit => hit.Field != null).ToList();
        Assert.Equal(fields.Count, fields.Select(hit => (hit.Kind, hit.Id)).Distinct().Count());
        Assert.All(fields, hit => Assert.Equal(hit.Id, hit.Field));

        // By the field's id too.
        Assert.Contains(session.Search("uhpm", 50), hit => hit.Field == "uhpm" && hit.Kind == "unit");
    }

    [Fact]
    public void Finds_custom_values()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        Assert.DoesNotContain(session.Search("champ", 50), hit => hit.Id == "h000");

        // A name the map sets is found, and the index follows the edit, its undo and its redo.
        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Champion");
        var found = Assert.Single(session.Search("champ", 50), hit => hit.Id == "h000");
        Assert.Equal("Champion", found.Name);
        Assert.Equal("unit", found.Kind);
        Assert.Null(found.Field);
        Assert.True(session.Undo());
        Assert.DoesNotContain(session.Search("champ", 50), hit => hit.Id == "h000");
        Assert.True(session.Redo());
        Assert.Contains(session.Search("champ", 50), hit => hit.Id == "h000");
        session.Reset(ObjectKind.Unit, "h000", "unam", 0);
        Assert.DoesNotContain(session.Search("champ", 50), hit => hit.Id == "h000");

        // Another value of a custom object, and a value of a standard object the map changes.
        var units = game.Kind(ObjectKind.Unit);
        var text = units.Fields.First(field => field.Type == "string" && field.Id != "unam"
            && units.Applies(field, units.Object("hfoo")!));
        session.Set(ObjectKind.Unit, "h000", text.Id, 0, "Zorgblat");
        session.Set(ObjectKind.Unit, "hfoo", text.Id, 0, "Quuxblatt");
        Assert.Equal(new[] { "h000" }, session.Search("zorgblat", 50).Select(hit => hit.Id));
        Assert.Equal(new[] { "hfoo" }, session.Search("quuxblatt", 50).Select(hit => hit.Id));

        // A standard value the map does not set is not searched.
        Assert.DoesNotContain(session.Search("420", 50), hit => hit.Id == "hfoo");

        // New, copied and deleted objects.
        var copy = session.Copy(ObjectKind.Unit, "h000", null);
        Assert.Equal(new[] { "h000", copy }, session.Search("zorgblat", 50).Select(hit => hit.Id).Order());
        session.Delete(ObjectKind.Unit, copy);
        Assert.Equal(new[] { "h000" }, session.Search("zorgblat", 50).Select(hit => hit.Id));
        Assert.True(session.Undo());
        Assert.Equal(2, session.Search("zorgblat", 50).Count);
    }

    [Fact]
    public void Reload_rebuilds_the_index()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Champion");
        session.Save();
        Assert.Contains(session.Search("champ", 50), hit => hit.Id == "h000");

        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Warlord");
        Assert.Contains(session.Search("warlord", 50), hit => hit.Id == "h000");

        session.Reload();

        Assert.DoesNotContain(session.Search("warlord", 50), hit => hit.Id == "h000");
        Assert.Contains(session.Search("champ", 50), hit => hit.Id == "h000");
    }

    [Fact]
    public void Fast_enough()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        string[] queries =
            ["the", "foo", "hit", "orc", "ski", "man", "ing", "lig", "ste", "arm", "dam", "att", "e", "zzz", "h00"];
        foreach (var query in queries)
        {
            session.Search(query, 50);   // warm up
        }

        var worst = TimeSpan.Zero;
        var worstQuery = "";
        foreach (var query in queries)
        {
            var watch = Stopwatch.StartNew();
            var hits = session.Search(query, 50);
            watch.Stop();
            output.WriteLine($"{query}: {watch.Elapsed.TotalMilliseconds:F3} ms, {hits.Count} hits");
            if (watch.Elapsed > worst)
            {
                (worst, worstQuery) = (watch.Elapsed, query);
            }
        }

        output.WriteLine($"worst: {worstQuery} {worst.TotalMilliseconds:F3} ms");
#if DEBUG
        const double limit = 150;   // Debug builds are unoptimised; the 30 ms bound is for Release.
#else
        const double limit = 30;
#endif
        Assert.True(worst.TotalMilliseconds < limit, $"{worstQuery} took {worst.TotalMilliseconds:F1} ms");
    }

    private bool Skipped(out GameData.GameData game)
    {
        if (fixture.Data == null)
        {
            output.WriteLine(fixture.Reason);
            game = null!;
            return true;
        }

        game = fixture.Data;
        return false;
    }
}
