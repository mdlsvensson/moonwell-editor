using MoonwellEditor.Files;

namespace MoonwellEditor.Map.Tests;

public sealed class MapReaderTests(ITestOutputHelper output)
{
    [Fact]
    public void Lost_Temple_reads_every_file()
    {
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var files = MapReader.Read(store);

        Assert.Equal(160u, files.Terrain!.Width); // War3Net counts tiles: 161 corners a row
        Assert.Equal(160u, files.Terrain.Height);
        Assert.NotNull(files.Doodads);
        Assert.NotNull(files.Units);
        Assert.NotNull(files.Info);
        Assert.Empty(files.Problems);
    }

    [Fact]
    public void A_missing_file_means_none_of_that_kind()
    {
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        using var folder = new TempFolder();
        folder.Write("war3map.w3e", store.Read("war3map.w3e")!);

        var files = MapReader.Read(MapFolder.Open(folder.Path));

        Assert.NotNull(files.Terrain);
        Assert.Null(files.Doodads);
        Assert.Null(files.Units);
        Assert.Null(files.Regions);
        Assert.Null(files.Cameras);
        Assert.Null(files.Info);
        Assert.Empty(files.Problems);
    }

    [Fact]
    public void An_unreadable_file_is_a_problem()
    {
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        using var folder = new TempFolder();
        foreach (var name in new[] { "war3map.w3e", "war3map.doo", "war3map.w3i" })
        {
            folder.Write(name, store.Read(name)!);
        }

        folder.Write("war3mapUnits.doo", new byte[10]);

        var files = MapReader.Read(MapFolder.Open(folder.Path));

        Assert.Null(files.Units);
        var problem = Assert.Single(files.Problems);
        Assert.Contains("war3mapUnits.doo", problem);
        Assert.NotNull(files.Terrain);
        Assert.NotNull(files.Doodads);
        Assert.NotNull(files.Info);
    }

    [Fact]
    public void Reading_writes_nothing()
    {
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var files = MapReader.Read(new NoWrites(store));

        Assert.NotNull(files.Terrain);
    }

    // A map whose every write fails the test.
    private sealed class NoWrites(IMapStore inner) : IMapStore
    {
        public string Path => inner.Path;

        public bool IsArchive => inner.IsArchive;

        public byte[]? Read(string name) => inner.Read(name);

        public byte[]? ReadImport(string path) => inner.ReadImport(path);

        public string? WriteProblem(string name) => inner.WriteProblem(name);

        public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files, string? stamp = null) =>
            throw new InvalidOperationException("The map view wrote to the map.");

        public string Stamp() => inner.Stamp();
    }
}
