using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

/// <summary>The names World Editor shows files by, as its screens showed them on 2026-10-27 (Definitive Edition).</summary>
[Collection(GameDataCollection.Name)]
public sealed class AssetNameTests(GameDataFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void Icons_and_models_are_named_by_the_objects_that_use_them()
    {
        if (fixture.Data == null)
        {
            output.WriteLine(fixture.Reason);
            return;
        }

        var names = AssetNames.Of(fixture.Data);
        List<string> Named(string group, string path) => names[group].GetValueOrDefault(AssetNames.Key(path)) ?? [];

        // Units in the order of their table: the Paladin before the campaign heroes, which show by their proper names.
        Assert.Equal(
            [
                "Paladin", "Admiral Proudmoore", "Dagren the Orcslayer", "Halahk the Lifebringer",
                "Magroth the Defender", "Lord Nicholas Buzan",
            ],
            Named("icon", @"ReplaceableTextures\CommandButtons\BTNHeroPaladin.blp").Take(6));
        Assert.Equal(["Peasant"], Named("model", @"units\human\Peasant\Peasant"));
        Assert.Equal(["Castle", "Keep", "Town Hall"], Named("model", @"buildings\human\TownHall\TownHall"));
        Assert.Equal(
            ["Castle", "Guard Tower", "Keep", "Town Hall", "Watch Tower", "Night Elf Battleship", "Night Elf Frigate"],
            Named("model", @"Abilities\Weapons\GuardTowerMissile\GuardTowerMissile.mdl"));
        Assert.Equal(
            ["Archmage <Special>", "Blood Mage <Special>", "Mountain King <Special>", "Paladin <Special>"],
            Named("model", @"Objects\Spawnmodels\Human\HumanLargeDeathExplode\HumanLargeDeathExplode.mdl").Take(4));

        // A unit with other models by version is not named by its model: the Priest's shows its path.
        Assert.Empty(Named("model", @"units\human\Priest\Priest"));
        Assert.Equal(["Priest <Caster>", "Emissary <Caster>"], Named("icon", Value("unit", "hmpr", "ucua")));

        // Abilities by name and suffix, tagged by their field; a buff's target art by the buffs; a tree wall's model.
        Assert.Equal(
            ["Holy Light <Normal>", "Holy Light <Research>"],
            Named("icon", @"ReplaceableTextures\CommandButtons\BTNHolyBolt.blp").Take(2));
        // Command Aura's strings are under [Boac], not BOac: Moonwell.GameData reads them whatever the case from
        // 0.3.0-preview.2, as World Editor does.
        Assert.Equal(
            [
                "Brilliance Aura <Target>", "Devotion Aura <Target>", "War Drums <Target>", "Command Aura <Target>",
                "Endurance Aura <Target>",
            ],
            Named("model", @"Abilities\Spells\Other\GeneralAuraTarget\GeneralAuraTarget.mdl").Take(5));
        Assert.Equal(
            ["Ashenvale Tree Wall <Base>", "Felwood Tree Wall <Base>", "Icecrown Tree Wall <Base>"],
            Named("model", @"Doodads\Terrain\AshenTree\AshenTree"));

        // A doodad shows no suffix (World Editor's Wooden Coffin; the data's suffix reads "(Default)").
        Assert.Equal(["Wooden Coffin <Base>"], Named("model", Value("doodad", "UOcw", "dfil")));

        // An upgrade's icon shows the items that share it, never the upgrade.
        Assert.Equal(["|cffaeaeaeSteel Sword|r"], Named("icon", Value("upgrade", "Rhme", "gar1:1")));
    }

    private string Value(string kind, string id, string key) =>
        fixture.Data!.Kind(Files.ObjectKinds.FromKey(kind)!.Value).Object(id)!.Values[key];
}
