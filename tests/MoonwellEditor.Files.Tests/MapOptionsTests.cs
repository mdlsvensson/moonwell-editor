using War3Net.Build.Extensions;
using War3Net.Build.Info;

namespace MoonwellEditor.Files.Tests;

public sealed class MapOptionsTests
{
    [Theory]
    [InlineData(GameDataSet.Default, GameDataVersion.TFT, "Default", "Frozen Throne", "custom_v1", "custom_v1")]
    [InlineData(GameDataSet.Custom, GameDataVersion.RoC, "Custom", "Reign of Chaos", "custom_v0", "custom_v0")]
    [InlineData(GameDataSet.Melee, GameDataVersion.RoC, "Melee", "Reign of Chaos", "melee_v0", "melee_v0")]
    [InlineData(GameDataSet.Melee, GameDataVersion.TFT, "Melee", "Frozen Throne", "base", "custom_v1")]
    [InlineData(GameDataSet.Custom, GameDataVersion.ForsakenKingdom, "Custom", "Forsaken Kingdom", "base", "base")]
    public void Name_the_option_and_the_tables_the_game_reads(
        GameDataSet set, GameDataVersion version, string setName, string versionName, string values, string objects)
    {
        Assert.Equal(new MapOptions(setName, versionName, values, objects), MapOptions.Of(set, version));
    }

    [Fact]
    public void Read_the_info_file_of_a_map_folder()
    {
        var folder = Directory.CreateTempSubdirectory("moonwell-editor-options").FullName;
        try
        {
            Assert.Null(MapOptions.Read(MapFolder.Open(folder)));

            MapInfo info;
            using (var reader = new BinaryReader(File.OpenRead(Path.Combine(Fixtures.Root, "map-info", "war3map.w3i"))))
            {
                info = reader.ReadMapInfo();
            }

            info.GameDataSet = GameDataSet.Melee;
            info.GameDataVersion = GameDataVersion.RoC;
            using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "war3map.w3i"))))
            {
                writer.Write(info);
            }

            Assert.Equal(new MapOptions("Melee", "Reign of Chaos", "melee_v0", "melee_v0"), MapOptions.Read(MapFolder.Open(folder)));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
