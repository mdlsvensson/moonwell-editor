using MoonwellEditor.Files;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

/// <summary>
/// The values and objects the editor shows follow the map's Data Set and Data Version, as World Editor's do (Sven,
/// 2026-10-27). The numbers are the ones seen in the game (wc3-cs docs/game-facts.md, Object data, 10-18).
/// </summary>
[Collection(GameDataCollection.Name)]
public sealed class DataSetTests(GameDataFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void A_map_reads_its_data_sets_values_and_objects()
    {
        if (fixture.Data is not { } game)
        {
            output.WriteLine(fixture.Reason);
            return;
        }

        Assert.Equal("350", HitPoints(game, null), StringComparer.Ordinal);
        Assert.Equal("260", HitPoints(game, (GameDataSet.Custom, GameDataVersion.RoC)), StringComparer.Ordinal);
        Assert.Equal("335", HitPoints(game, (GameDataSet.Custom, GameDataVersion.TFT)), StringComparer.Ordinal);
        Assert.Equal("355", HitPoints(game, (GameDataSet.Melee, GameDataVersion.RoC)), StringComparer.Ordinal);

        // An object the set leaves out does not exist there, and World Editor does not list it.
        Assert.Contains(Tree(game, null), entry => entry.Id == "ebsh");
        Assert.DoesNotContain(Tree(game, (GameDataSet.Custom, GameDataVersion.RoC)), entry => entry.Id == "ebsh");
        Assert.DoesNotContain(Tree(game, (GameDataSet.Custom, GameDataVersion.TFT)), entry => entry.Id == "fapo");
    }

    // The Shaman's hit points under the option (null: the map has no info file, so the base tables).
    private static string HitPoints(GameData.GameData game, (GameDataSet Set, GameDataVersion Version)? option)
    {
        using var map = MapWith(option);
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        return session.Object(ObjectKind.Unit, "oshm")!.Fields.Single(field => field.Id == "uhpm").Value;
    }

    private static IReadOnlyList<TreeEntry> Tree(GameData.GameData game, (GameDataSet Set, GameDataVersion Version)? option)
    {
        using var map = MapWith(option);
        return Session.Open(MapFolder.Open(map.Folder), game).Tree(ObjectKind.Unit);
    }

    // The we-3.0 map with an info file of the option (from the map-info fixture), or none.
    private static TempMap MapWith((GameDataSet Set, GameDataVersion Version)? option)
    {
        var map = TempMap.Of("we-3.0");
        if (option is { } chosen)
        {
            MapInfo info;
            using (var reader = new BinaryReader(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "map-info", "war3map.w3i"))))
            {
                info = reader.ReadMapInfo();
            }

            info.GameDataSet = chosen.Set;
            info.GameDataVersion = chosen.Version;
            using var writer = new BinaryWriter(File.Create(Path.Combine(map.Folder, "war3map.w3i")));
            writer.Write(info);
        }

        return map;
    }
}
