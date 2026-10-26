using MoonwellEditor.Files;
using MoonwellEditor.GameData;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class FolderTests(GameDataFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void Units_go_by_race_then_melee_or_campaign_then_type()
    {
        Assert.Equal(["Night Elf", "Melee", "Units"], Folders.Of(ObjectKind.Unit, Standard("earc", "nightelf")));
        Assert.Equal(
            ["Human", "Campaign", "Heroes"],
            Folders.Of(ObjectKind.Unit, Standard("Hart", "human", campaign: true, hero: true)));
        Assert.Equal(
            ["Orc", "Melee", "Buildings"],
            Folders.Of(ObjectKind.Unit, Standard("ogre", "orc", building: true)));
        Assert.Equal(
            ["Undead", "Melee", "Special"],
            Folders.Of(ObjectKind.Unit, Standard("uloc", "undead", special: true, hero: true)));
        Assert.Equal(["Creeps", "Melee", "Units"], Folders.Of(ObjectKind.Unit, Standard("nfor", "creeps")));
        Assert.Equal(["Other", "Melee", "Units"], Folders.Of(ObjectKind.Unit, Standard("nxxx", "")));
    }

    [Fact]
    public void Items_go_by_class()
    {
        Assert.Equal(["Power Up"], Folders.Of(ObjectKind.Item, Standard("manh", "", category: "PowerUp")));
        Assert.Equal(["Equipment"], Folders.Of(ObjectKind.Item, Standard("I0xx", "", category: "Equipment")));
        Assert.Equal(["Permanent"], Folders.Of(ObjectKind.Item, Standard("ratf", "", category: "Permanent")));
    }

    [Fact]
    public void Destructables_and_doodads_go_by_category()
    {
        Assert.Equal(["Trees/Destructables"], Folders.Of(ObjectKind.Destructable, Standard("LTlt", "", category: "D")));
        Assert.Equal(["Pathing Blockers"], Folders.Of(ObjectKind.Destructable, Standard("YTpb", "", category: "P")));
        Assert.Equal(["Bridges/Ramps"], Folders.Of(ObjectKind.Destructable, Standard("LTw0", "", category: "B")));
        string[][] doodads =
        [
            ["O", "Props"], ["S", "Structures"], ["W", "Water"], ["C", "Cliff/Terrain"], ["E", "Environment"],
            ["Z", "Cinematic"],
        ];
        foreach (var pair in doodads)
        {
            Assert.Equal([pair[1]], Folders.Of(ObjectKind.Doodad, Standard("YOs1", "", category: pair[0])));
        }
    }

    [Fact]
    public void Abilities_go_by_race_then_units_heroes_or_items()
    {
        Assert.Equal(["Human", "Heroes"], Folders.Of(ObjectKind.Ability, Standard("AHhb", "human", category: "hero")));
        Assert.Equal(
            ["Night Elf", "Units"],
            Folders.Of(ObjectKind.Ability, Standard("Aeat", "nightelf", category: "unit")));
        Assert.Equal(
            ["Neutral Hostile", "Heroes"],
            Folders.Of(ObjectKind.Ability, Standard("ANsi", "creeps", category: "hero")));
        Assert.Equal(
            ["Neutral Passive", "Units"],
            Folders.Of(ObjectKind.Ability, Standard("ANen", "naga", category: "unit")));
        Assert.Equal(["Special", "Items"], Folders.Of(ObjectKind.Ability, Standard("AIat", "other", category: "item")));
        Assert.Equal(["Special", "Units"], Folders.Of(ObjectKind.Ability, Standard("Aawa", "other", category: "")));
        Assert.Equal(
            ["Hidden in World Editor"],
            Folders.Of(ObjectKind.Ability, Standard("Aloc", "undead", category: "unit", hidden: true)));
    }

    [Fact]
    public void Buffs_go_by_race_then_buff_or_effect_and_upgrades_by_race()
    {
        Assert.Equal(["Orc", "Buffs"], Folders.Of(ObjectKind.Buff, Standard("Boar", "orc", category: "0")));
        Assert.Equal(["Special", "Effects"], Folders.Of(ObjectKind.Buff, Standard("Xbof", "other", category: "1")));
        Assert.Equal(["Undead"], Folders.Of(ObjectKind.Upgrade, Standard("Rusp", "undead")));
        Assert.Equal(["Special"], Folders.Of(ObjectKind.Upgrade, Standard("Rnsw", "naga")));
        Assert.Equal(["Special"], Folders.Of(ObjectKind.Upgrade, Standard("Rxxx", "unknown")));
    }

    [Fact]
    public void Tree_folders_come_in_world_editors_order()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        var units = session.Tree(ObjectKind.Unit);
        Assert.Equal(["Human", "Orc", "Undead", "Night Elf"], Groups(units, []).Take(4));
        Assert.Equal(["Melee", "Campaign"], Groups(units, ["Night Elf"]));
        Assert.Equal(["Units", "Buildings", "Heroes", "Special"], Groups(units, ["Night Elf", "Melee"]));

        Assert.Equal(
            ["Permanent", "Charged", "Power Up", "Artifact", "Purchasable", "Campaign", "Miscellaneous", "Equipment"],
            Groups(session.Tree(ObjectKind.Item), []));
        Assert.Equal(
            ["Trees/Destructables", "Pathing Blockers", "Bridges/Ramps"],
            Groups(session.Tree(ObjectKind.Destructable), []));
        Assert.Equal(
            ["Props", "Structures", "Water", "Cliff/Terrain", "Environment", "Cinematic"],
            Groups(session.Tree(ObjectKind.Doodad), []));

        var abilities = session.Tree(ObjectKind.Ability);
        Assert.Equal(
            [
                "Human", "Orc", "Undead", "Night Elf", "Neutral Hostile", "Neutral Passive", "Special",
                "Hidden in World Editor",
            ],
            Groups(abilities, []));
        Assert.Equal(11, abilities.Count(entry => entry.Path[0] == "Neutral Passive"));
        Assert.Equal(243, abilities.Count(entry => entry.Path[0] == "Neutral Hostile"));
        Assert.Equal(23, abilities.Count(entry => entry.Path[0] == "Hidden in World Editor"));
        Assert.Equal(["Hidden in World Editor"], abilities.Single(entry => entry.Id == "Aloc").Path);
        Assert.Equal(["Units", "Heroes", "Items"], Groups(abilities, ["Special"]));

        var buffs = session.Tree(ObjectKind.Buff);
        Assert.Equal(["Human", "Orc", "Undead", "Night Elf", "Special"], Groups(buffs, []));
        Assert.Equal(["Buffs", "Effects"], Groups(buffs, ["Special"]));

        Assert.Equal(
            ["Human", "Orc", "Undead", "Night Elf", "Special"],
            Groups(session.Tree(ObjectKind.Upgrade), []));

        // The map's custom objects stay first.
        Assert.Equal(["Custom"], units[0].Path);
    }

    /// <summary>The folders right under <paramref name="parent"/>, in the order the tree first shows them.</summary>
    private static List<string> Groups(IReadOnlyList<TreeEntry> tree, string[] parent) =>
        tree.Where(entry => entry.Path.Count > parent.Length && entry.Path.Take(parent.Length).SequenceEqual(parent))
            .Where(entry => entry.Path[0] != "Custom")
            .Select(entry => entry.Path[parent.Length])
            .Distinct()
            .ToList();

    private static StandardObject Standard(
        string id,
        string race,
        bool campaign = false,
        bool hero = false,
        bool building = false,
        bool special = false,
        string category = "",
        bool hidden = false) =>
        new(id, id, "", race, campaign, hero, building, special, category, hidden, null,
            new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>());

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
