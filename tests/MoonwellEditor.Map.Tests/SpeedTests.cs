using System.Diagnostics;
using MoonwellEditor.Files;
using War3Net.Build.Extensions;

namespace MoonwellEditor.Map.Tests;

/// <summary>
/// How long the scene takes to build, printed for the plan's speed check; the bound asserted is far above what is
/// expected, so only a scene gone badly wrong fails it.
/// </summary>
public sealed class SpeedTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(InstallMaps.LostTemple)]
    [InlineData(InstallMaps.EkrezemsMaze)]
    public void An_install_maps_scene_builds_in_time(string map)
    {
        var files = InstallGameFiles.Open();
        var store = InstallMaps.Open(map);
        var session = store == null ? null : TestGameData.Open(store);
        if (files == null || store == null || session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        MapScene.Build(store, session, files, "sd"); // the files' first read is not the scene's cost
        var clock = Stopwatch.StartNew();
        var (view, terrain) = MapScene.Build(store, session, files, "hd");
        clock.Stop();

        output.WriteLine(
            $"{Path.GetFileName(map)}: {clock.ElapsedMilliseconds} ms, {view.Columns} by {view.Rows} corners, "
            + $"{view.Things.Count} things, {view.Cliffs.Count} cliff pieces, {terrain.Length / 1024} KB of terrain");
        Assert.InRange(clock.ElapsedMilliseconds, 0, 20000);
    }

    [Fact]
    public void The_largest_maps_scene_builds_in_time()
    {
        var files = InstallGameFiles.Open();
        using var folder = new TempFolder();
        var store = MapFolder.Open(folder.Path);
        var session = TestGameData.Open(store);
        if (files == null || session == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        // 480 by 480 tiles, World Editor's largest: 481 by 481 corners, hills and a cliff level every 16 corners.
        var terrain = Grids.Flat(481, 481);
        terrain.TerrainTypes.Add(War3Net.Build.Environment.TerrainType.L_Dirt);
        terrain.CliffTypes.Add(War3Net.Build.Environment.CliffType.L_Dirt);
        for (var i = 0; i < terrain.TerrainTiles.Count; i++)
        {
            var corner = terrain.TerrainTiles[i];
            corner.Height = MathF.Sin(i * 0.01f);
            corner.CliffLevel = 2 + ((i / 481 / 16) % 2);
        }

        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(terrain);
            }

            folder.Write("war3map.w3e", stream.ToArray());
        }

        var clock = Stopwatch.StartNew();
        var (view, bytes) = MapScene.Build(store, session, files, "hd");
        clock.Stop();

        output.WriteLine($"481 by 481: {clock.ElapsedMilliseconds} ms, {view.Cliffs.Count} cliff pieces, {bytes.Length / 1024} KB");
        Assert.Equal(481, view.Columns);
        Assert.Equal(8 + (481 * 481 * 12) + (480 * 480 * 9) + (480 * 480 * 16), bytes.Length);
        Assert.InRange(clock.ElapsedMilliseconds, 0, 30000);
    }
}
