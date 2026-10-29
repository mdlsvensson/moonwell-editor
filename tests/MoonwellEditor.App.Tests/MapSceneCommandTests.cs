using System.Text.Json;
using MoonwellEditor.App;
using MoonwellEditor.Files;

namespace MoonwellEditor.App.Tests;

public sealed class MapSceneCommandTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "MoonwellEditor.App.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task mapScene_without_a_map_is_noMap()
    {
        var commands = new Commands(
            new Settings(Path.Combine(_root, "settings")),
            _ => throw new InvalidOperationException("no dialog"),
            _ => throw new InvalidOperationException());
        var bridge = new Bridge(commands.Run);

        var reply = JsonDocument.Parse(await bridge.Handle("{\"id\":1,\"command\":\"mapScene\",\"args\":{\"mode\":\"sd\"}}"))
            .RootElement;

        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("noMap", reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(commands.Terrain("sd"));
    }

    [Fact]
    public void An_import_the_archive_cannot_give_is_not_found()
    {
        Directory.CreateDirectory(_root);
        using var assets = new GameAssets(() => new Unreadable(_root));

        Assert.Null(assets.File(@"war3mapImported\Box.mdx"));
    }

    // A map archive whose every import is there but cannot be read out of it.
    private sealed class Unreadable(string path) : IMapStore
    {
        public string Path => path;

        public bool IsArchive => true;

        public byte[]? Read(string name) => null;

        public byte[]? ReadImport(string import) => throw new MapArchiveException(path, "bad compression");

        public string? WriteProblem(string name) => null;

        public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files, string? stamp = null) =>
            throw new InvalidOperationException();

        public string Stamp() => "";
    }

    [Fact]
    public void An_import_named_mdl_is_found_as_mdx()
    {
        Directory.CreateDirectory(Path.Combine(_root, "map", "war3mapImported"));
        File.WriteAllBytes(Path.Combine(_root, "map", "war3mapImported", "Box.mdx"), [1, 2, 3]);
        var map = MapFolder.Open(Path.Combine(_root, "map"));
        using var assets = new GameAssets(() => map);

        Assert.Equal([1, 2, 3], assets.File(@"war3mapImported\Box.mdl"));
        Assert.Equal([1, 2, 3], assets.File(@"war3mapImported\Box.mdx"));
        Assert.Null(assets.File(@"war3mapImported\Ball.mdl"));
    }
}
