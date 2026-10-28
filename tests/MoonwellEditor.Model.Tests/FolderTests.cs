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

        // A unit of no playable race is the naga's, or Neutral Hostile or Passive by its hostilePal, whatever its race
        // (World Editor's screens, 2026-10-27).
        Assert.Equal(
            ["Neutral Hostile", "Melee", "Units"],
            Folders.Of(ObjectKind.Unit, Standard("nfor", "creeps", hostile: true)));
        Assert.Equal(["Neutral Passive", "Melee", "Units"], Folders.Of(ObjectKind.Unit, Standard("nalb", "critters")));
        Assert.Equal(
            ["Neutral Passive", "Melee", "Heroes"],
            Folders.Of(ObjectKind.Unit, Standard("Npbm", "other", hero: true)));
        Assert.Equal(
            ["Neutral - Naga", "Campaign", "Units"],
            Folders.Of(ObjectKind.Unit, Standard("nmyr", "naga", campaign: true, hostile: true)));
        Assert.Equal(["Hidden in World Editor"], Folders.Of(ObjectKind.Unit, Standard("zshv", "other", hidden: true)));
    }

    [Fact]
    public void A_custom_object_is_filed_by_its_own_values_where_the_map_sets_them()
    {
        var footman = Standard("hfoo", "human");
        var none = (string _) => (string?)null;

        // As its base, under its own id and name; a capital letter makes a hero.
        var copy = Folders.AsFiled(ObjectKind.Unit, "h000", "Guard", "", footman, none);
        Assert.Equal(("h000", "Guard"), (copy.Id, copy.Name));
        Assert.Equal(["Human", "Melee", "Units"], Folders.Of(ObjectKind.Unit, copy));
        Assert.Equal(
            ["Human", "Melee", "Heroes"],
            Folders.Of(ObjectKind.Unit, Folders.AsFiled(ObjectKind.Unit, "H000", "Guard", "", footman, none)));

        // Its race, campaign and building where the map sets them.
        var set = new Dictionary<string, string> { ["urac"] = "orc", ["ucam"] = "1", ["ubdg"] = "1" };
        Assert.Equal(
            ["Orc", "Campaign", "Buildings"],
            Folders.Of(
                ObjectKind.Unit,
                Folders.AsFiled(ObjectKind.Unit, "h001", "Hut", "", footman, set.GetValueOrDefault)));

        // A hidden base does not hide the custom object; an item by its class; an ability by Hero Ability.
        var hidden = Standard("zshv", "other", hidden: true);
        Assert.False(Folders.AsFiled(ObjectKind.Unit, "n000", "Shiva", "", hidden, none).Hidden);
        Assert.Equal(
            ["Charged"],
            Folders.Of(
                ObjectKind.Item,
                Folders.AsFiled(
                    ObjectKind.Item, "I000", "Potion", "", Standard("phea", "", category: "Permanent"),
                    new Dictionary<string, string> { ["icla"] = "Charged" }.GetValueOrDefault)));
        Assert.Equal(
            ["Human", "Heroes"],
            Folders.Of(
                ObjectKind.Ability,
                Folders.AsFiled(
                    ObjectKind.Ability, "A000", "Bolt", "", Standard("AHtb", "human", category: "unit"),
                    new Dictionary<string, string> { ["aher"] = "1" }.GetValueOrDefault)));
    }

    [Fact]
    public void Other_kinds_list_a_folder_by_name_after_worlds_editors_sort_column()
    {
        // Compared as written, as World Editor lists Call To Arms (Town Hall) before Call to Arms (Peasant).
        var abilities = Folders.Sorted(
            ObjectKind.Ability,
            [
                Standard("Amil", "human", category: "unit", name: "Call to Arms", suffix: "(Peasant)"),
                Standard("Aflk", "human", category: "unit", name: "Flak Cannons"),
                Standard("Amic", "human", category: "unit", name: "Call To Arms", suffix: "(Town Hall)"),
            ]);
        Assert.Equal(["Amic", "Amil", "Aflk"], abilities.Select(standard => standard.Id));

        // Buffs of hero abilities come before the others; tree walls (doodClass A) before canopy trees (B) and the
        // rest (_).
        var buffs = Folders.Sorted(
            ObjectKind.Buff,
            [
                Standard("Bclf", "human", category: "0", name: "Cloud", sort: "unit"),
                Standard("BHtc", "human", category: "0", name: "Thunder Clap", sort: "hero"),
                Standard("BHab", "human", category: "0", name: "Brilliance Aura", sort: "hero"),
            ]);
        Assert.Equal(["BHab", "BHtc", "Bclf"], buffs.Select(standard => standard.Id));
        var destructables = Folders.Sorted(
            ObjectKind.Destructable,
            [
                Standard("LTbr", "", category: "D", name: "Barrel", sort: "_"),
                Standard("ATtc", "", category: "D", name: "Ashenvale Canopy Tree", sort: "B"),
                Standard("LTlt", "", category: "D", name: "Summer Tree Wall", sort: "A"),
            ]);
        Assert.Equal(["LTlt", "ATtc", "LTbr"], destructables.Select(standard => standard.Id));
    }

    [Fact]
    public void Units_of_a_folder_go_by_their_unit_class()
    {
        var sorted = Folders.Sorted(
            ObjectKind.Unit,
            [
                Standard("hfoo", "human", category: "HUnit02"),
                Standard("hxxx", "human"),
                Standard("hpea", "human", category: "HUnit01"),
                Standard("htow", "human", building: true, category: "HBuilding01"),
            ]);

        Assert.Equal(["hpea", "hfoo", "hxxx", "htow"], sorted.Select(standard => standard.Id));
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
        Assert.Equal(["Trees/Destructibles"], Folders.Of(ObjectKind.Destructable, Standard("LTlt", "", category: "D")));
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
        Assert.Equal(["Orc", "Buff"], Folders.Of(ObjectKind.Buff, Standard("Boar", "orc", category: "0")));
        Assert.Equal(["Special", "Effect"], Folders.Of(ObjectKind.Buff, Standard("Xbof", "other", category: "1")));
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
        // As World Editor's tree shows them: 926 units, Neutral Hostile 299 (280 melee, 19 campaign) and Neutral
        // Passive 197 (143 and 54), and the two it hides apart.
        Assert.Equal(
            [
                "Human", "Orc", "Undead", "Night Elf", "Neutral - Naga", "Neutral Hostile", "Neutral Passive",
                "Hidden in World Editor",
            ],
            Groups(units, []));
        Assert.Equal(926, units.Count(entry => entry.Path[0] is not ("Custom" or "Hidden in World Editor")));
        Assert.Equal(280, units.Count(entry => entry.Path.Take(2).SequenceEqual(["Neutral Hostile", "Melee"])));
        Assert.Equal(19, units.Count(entry => entry.Path.Take(2).SequenceEqual(["Neutral Hostile", "Campaign"])));
        Assert.Equal(143, units.Count(entry => entry.Path.Take(2).SequenceEqual(["Neutral Passive", "Melee"])));
        Assert.Equal(54, units.Count(entry => entry.Path.Take(2).SequenceEqual(["Neutral Passive", "Campaign"])));
        Assert.Equal(["Units", "Buildings", "Special"], Groups(units, ["Neutral Hostile", "Melee"]));
        Assert.Equal(19, units.Count(entry => entry.Path[0] == "Neutral - Naga"));
        Assert.Equal(["Melee", "Campaign"], Groups(units, ["Night Elf"]));
        Assert.Equal(["Units", "Buildings", "Heroes", "Special"], Groups(units, ["Night Elf", "Melee"]));

        Assert.Equal(
            ["Permanent", "Charged", "Power Up", "Artifact", "Purchasable", "Campaign", "Miscellaneous", "Equipment"],
            Groups(session.Tree(ObjectKind.Item), []));
        Assert.Equal(
            ["Trees/Destructibles", "Pathing Blockers", "Bridges/Ramps"],
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
        Assert.Equal(["Buff", "Effect"], Groups(buffs, ["Special"]));
        Assert.Equal(
            ["Avatar", "Banish", "Blizzard", "Blizzard (Caster)", "Brilliance Aura", "Devotion Aura"],
            buffs.Where(entry => entry.Path.SequenceEqual(["Human", "Buff"])).Select(entry => entry.Name).Take(6));
        Assert.Equal(
            ["Ashenvale Tree Wall", "Barrens Tree Wall", "Black Citadel Tree Wall"],
            session.Tree(ObjectKind.Destructable).Where(entry => entry.Path[0] == "Trees/Destructibles")
                .Select(entry => entry.Name).Take(3));

        Assert.Equal(
            ["Human", "Orc", "Undead", "Night Elf", "Special"],
            Groups(session.Tree(ObjectKind.Upgrade), []));

        // A folder lists its units as World Editor does, by their unitClass (Sven, 2026-10-27).
        Assert.Equal(
            ["hpea", "hfoo", "hkni", "hrif", "hmtm", "hgyr", "hgry", "hmpr", "hsor", "hmtt", "hspt", "hdhw"],
            Objects(units, ["Human", "Melee", "Units"]));
        Assert.Equal(
            [
                "htow", "hkee", "hcas", "hhou", "halt", "hbar", "hlum", "hbla", "harm", "hars", "hgra", "hwtw", "hgtw",
                "hctw", "hatw", "hvlt",
            ],
            Objects(units, ["Human", "Melee", "Buildings"]));
        Assert.Equal(["Hpal", "Hamg", "Hmkg", "Hblm"], Objects(units, ["Human", "Melee", "Heroes"]));

        // unitui.slk files the High Elf Archmage in the campaign, though the skin text says otherwise, and World Editor
        // shows a campaign hero by its proper name; its campaign heroes go by class, then by name (Sven's screenshot).
        var kelen = units.Single(entry => entry.Id == "Haah");
        Assert.Equal(["Human", "Campaign", "Heroes"], kelen.Path);
        Assert.Equal("Kelen the Seeker", kelen.Name);
        Assert.Equal(
            [
                "Garek", "Landen", "Leonid", "Anasterian Sunstrider", "Thalorien Dawnseeker", "Kelen the Seeker",
                "Admiral Proudmoore", "Antonidas", "Antonidas", "Arthas", "Arthas", "Dagren the Orcslayer",
                "Halahk the Lifebringer", "Ilastar", "Jaina", "Jennalla Deemspring", "Kael", "Lord Garithos",
                "Lord Maxwell Tyrosus", "Lord Nicholas Buzan", "Magroth the Defender", "Muradin Bronzebeard",
                "Renault Mograine", "Sally Whitemane", "Sir Gregory Edmunson", "Sylvanas Windrunner", "Tomas", "Uther",
            ],
            units.Where(entry => entry.Path.SequenceEqual(["Human", "Campaign", "Heroes"])).Select(entry => entry.Name));

        // Each object carries its icon for the tree; a custom one its base's unless the map changes it.
        Assert.EndsWith("BTNPeasant.blp", units.Single(entry => entry.Id == "hpea").Icon, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("", units[0].Icon);
        Assert.Equal("", session.Tree(ObjectKind.Doodad)[^1].Icon);

        // The map's custom objects stay first.
        Assert.Equal("Custom", units[0].Path[0]);
    }

    /// <summary>The folders right under <paramref name="parent"/>, in the order the tree first shows them.</summary>
    private static List<string> Groups(IReadOnlyList<TreeEntry> tree, string[] parent) =>
        tree.Where(entry => entry.Path.Count > parent.Length && entry.Path.Take(parent.Length).SequenceEqual(parent))
            .Where(entry => entry.Path[0] != "Custom")
            .Select(entry => entry.Path[parent.Length])
            .Distinct()
            .ToList();

    /// <summary>The ids of the objects in the folder <paramref name="path"/>, in tree order.</summary>
    private static List<string> Objects(IReadOnlyList<TreeEntry> tree, string[] path) =>
        tree.Where(entry => entry.Path.SequenceEqual(path)).Select(entry => entry.Id).ToList();

    private static StandardObject Standard(
        string id,
        string race,
        bool campaign = false,
        bool hero = false,
        bool building = false,
        bool special = false,
        string category = "",
        bool hidden = false,
        bool hostile = false,
        string? name = null,
        string suffix = "",
        string sort = "") =>
        new(id, name ?? id, suffix, race, campaign, hero, building, special, category, hidden, null,
            new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            Hostile: hostile, Sort: sort);

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
