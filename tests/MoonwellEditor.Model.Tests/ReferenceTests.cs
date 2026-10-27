using MoonwellEditor.Files;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class ReferenceTests(GameDataFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void Standard_objects_that_name_an_object_use_it()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of(null);
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        // The Paladin's hero abilities hold Holy Light.
        var paladin = Assert.Single(session.UsedBy(ObjectKind.Ability, "AHhb"), each => each.Id == "Hpal");
        Assert.Equal("unit", paladin.Kind);
        Assert.Equal(game.Kind(ObjectKind.Unit).Object("Hpal")!.Name, paladin.Name);
        Assert.Contains(paladin.Fields, label => label == game.Kind(ObjectKind.Unit).Field("uhab")!.Label);

        // The Barracks trains Footmen.
        Assert.Contains(session.UsedBy(ObjectKind.Unit, "hfoo"), each => each.Id == "hbar" && each.Kind == "unit");
        Assert.Empty(session.UsedBy(ObjectKind.Unit, "zzzz"));
    }

    [Fact]
    public void Map_changes_count()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of(null);
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        // A custom unit given Holy Light uses it; the Paladin no longer does once the map takes it away from its hero
        // abilities and their skin list (which names abilities too).
        var custom = session.Create(ObjectKind.Unit, "hfoo", null);
        session.Set(ObjectKind.Unit, custom, "uabi", 0, "AHhb");
        session.Set(ObjectKind.Unit, "Hpal", "uhab", 0, "AHds");
        session.Set(ObjectKind.Unit, "Hpal", "uhas", 0, "AHds");

        var users = session.UsedBy(ObjectKind.Ability, "AHhb");
        var mine = Assert.Single(users, each => each.Id == custom);
        Assert.Equal("unit", mine.Kind);
        Assert.DoesNotContain(users, each => each.Id == "Hpal");
        Assert.Contains(session.UsedBy(ObjectKind.Ability, "AHds"), each => each.Id == "Hpal");
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
