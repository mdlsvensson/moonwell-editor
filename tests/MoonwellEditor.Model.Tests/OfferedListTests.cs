using System.Text;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

/// <summary>The lists World Editor offers for a ground texture and a shadow (Sven, 2026-10-27).</summary>
[Collection(GameDataCollection.Name)]
public sealed class OfferedListTests(GameDataFixture fixture, ITestOutputHelper output)
{
    // Three rows of UberSplatData.slk as the install has them: INIT, then two splats by code.
    private const string SplatTable = """
        ID;PWXL;N;E
        B;X3;Y4;D0
        C;X1;Y1;K"Name"
        C;X2;K"comment"
        C;X3;K"file"
        C;X1;Y2;K"INIT"
        C;X2;K"INIT"
        C;X3;K"TEST"
        C;X1;Y3;K"HTOW"
        C;X2;K"TownHallUberSplat"
        C;X3;K"HumanTownHallUberSplat"
        C;X1;Y4;K"HSMA"
        C;X2;K"HumanUberSplat"
        C;X3;K"HumanUberSplat"
        E
        """;

    [Fact]
    public void Strings_are_read_as_keys_and_texts_without_quotes()
    {
        var strings = OfferedLists.Strings(Encoding.UTF8.GetBytes(
            "[WorldEditStrings]\r\nWESTRING_UBERSPLAT_HTOW=Town Hall\r\n// a note\r\nWESTRING_QUOTED=\"Quoted\"\r\n"
            + "WESTRING_UBERSPLAT_HTOW=Second\r\n"));

        Assert.Equal("Town Hall", strings["WESTRING_UBERSPLAT_HTOW"]);
        Assert.Equal("Quoted", strings["westring_quoted"]);
        Assert.Equal(2, strings.Count);
    }

    [Fact]
    public void Splats_are_listed_by_World_Editor_names_after_None()
    {
        var strings = new Dictionary<string, string> { ["WESTRING_UBERSPLAT_HSMA"] = "Human Small Building" };

        var list = OfferedLists.Splats(Encoding.UTF8.GetBytes(SplatTable), strings);

        Assert.NotNull(list);
        Assert.False(list.Flags);
        // HTOW has no name here, so it shows its code; INIT is not a splat.
        Assert.Equal(
            [("", "None"), ("HTOW", "HTOW"), ("HSMA", "Human Small Building")],
            list.Entries.Select(entry => (entry.Value, entry.Label)));
        Assert.Null(OfferedLists.Splats(null, strings));
    }

    [Fact]
    public void Shadows_are_listed_under_buildings_and_destructables_by_the_objects_that_use_them()
    {
        if (fixture.Data == null)
        {
            output.WriteLine(fixture.Reason);
            return;
        }

        var list = OfferedLists.Shadows(fixture.Data);
        var house = list.Entries.Single(entry => entry.Value == "ShadowHouse");
        var tree = list.Entries.First(entry => entry.Group == OfferedLists.Destructables);

        Assert.Equal(("", "None", null), (list.Entries[0].Value, list.Entries[0].Label, list.Entries[0].Group));
        Assert.Equal(OfferedLists.Buildings, house.Group);
        Assert.StartsWith("Farm", house.Label);
        Assert.NotEqual("", tree.Label);
        // Each shadow once per heading.
        Assert.Equal(
            list.Entries.Count,
            list.Entries.Select(entry => (entry.Group, AssetNames.Key(entry.Value))).Distinct().Count());
    }
}
