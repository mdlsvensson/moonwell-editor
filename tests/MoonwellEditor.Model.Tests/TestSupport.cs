using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model.Tests;

/// <summary>
/// Opens a map as if War3Net could not write one of its object files back exactly (War3Net writes every known file
/// back exactly, so the tests stand in such a file): the write-back of <paramref name="lossyName"/> drops 4 bytes.
/// </summary>
public static class Lossy
{
    /// <summary>Opens <paramref name="folder"/> with <paramref name="lossyName"/> read as a lossy file.</summary>
    public static Session Open(string folder, GameData.GameData game, string lossyName = "war3map.w3a") =>
        Session.Open(MapFolder.Open(folder), game, (kind, bytes, name) => name == lossyName
            ? ObjectFile.Read(kind, bytes, name, read => read.ToBytes()[..^4])
            : ObjectFile.Read(kind, bytes, name));
}

/// <summary>
/// The game data folder named by <c>MOONWELL_EDITOR_GAME_DATA</c>, loaded once per test run for every test class of the
/// <see cref="GameDataCollection"/>. The cache lives under the system temp folder and is kept between runs, so only the
/// first run builds it.
/// </summary>
public sealed class GameDataFixture
{
    /// <summary>The environment variable that names the game data folder.</summary>
    public const string Variable = "MOONWELL_EDITOR_GAME_DATA";

    public GameDataFixture()
    {
        var folder = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(folder))
        {
            Reason = $"{Variable} is not set; set it to a game data folder (war3.w3mod) to run this test";
            return;
        }

        if (!Directory.Exists(folder))
        {
            Reason = $"{Variable} names {folder}, which does not exist; skipped";
            return;
        }

        var cache = Path.Combine(Path.GetTempPath(), "MoonwellEditor.Model.Tests", "gamedata");
        Data = GameData.GameData.Load(folder, cache);
    }

    /// <summary>Why the tests cannot run, or null when <see cref="Data"/> is loaded.</summary>
    public string? Reason { get; }

    /// <summary>The game data, or null when the tests cannot run.</summary>
    public GameData.GameData? Data { get; }
}

/// <summary>The test classes that share one <see cref="GameDataFixture"/>.</summary>
[CollectionDefinition(Name)]
public sealed class GameDataCollection : ICollectionFixture<GameDataFixture>
{
    /// <summary>The collection's name, for <c>[Collection]</c>.</summary>
    public const string Name = "Game data";
}

/// <summary>
/// A temporary copy of a fixture folder (or an empty folder), or of a fixture archive, deleted when disposed, so a test
/// can change the files.
/// </summary>
public sealed class TempMap : IDisposable
{
    private readonly string _root;

    private TempMap(string root, string folder, string map)
    {
        _root = root;
        Folder = folder;
        Map = map;
    }

    /// <summary>The map folder; for an archive, the folder that holds it.</summary>
    public string Folder { get; }

    /// <summary>The map: the folder, or the archive file's path.</summary>
    public string Map { get; }

    /// <summary>
    /// Copies the fixture <paramref name="fixture"/> (<c>we-3.0</c>, <c>moonwell-mymap</c>; null for an empty folder)
    /// into a new temporary folder, at <paramref name="inside"/> below it when given (such as <c>build/MyMap</c>).
    /// </summary>
    public static TempMap Of(string? fixture, string? inside = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "MoonwellEditor.Model.Tests", "maps", Guid.NewGuid().ToString("N"));
        var folder = inside == null ? Path.Combine(root, "map") : Path.Combine(root, inside);
        Directory.CreateDirectory(folder);
        if (fixture != null)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture)))
            {
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }
        }

        return new TempMap(root, folder, folder);
    }

    /// <summary>
    /// Copies the fixture archive <paramref name="fixture"/> (<c>moonwell-sample/Sample.w3x</c>) into a new temporary
    /// folder, at <paramref name="inside"/> below it when given (such as <c>MyMap/build</c>), keeping its file name.
    /// </summary>
    public static TempMap OfArchive(string fixture = "moonwell-sample/Sample.w3x", string? inside = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "MoonwellEditor.Model.Tests", "maps", Guid.NewGuid().ToString("N"));
        var folder = inside == null ? root : Path.Combine(root, inside);
        Directory.CreateDirectory(folder);
        var source = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        var map = Path.Combine(folder, Path.GetFileName(source));
        File.Copy(source, map);
        return new TempMap(root, folder, map);
    }

    /// <summary>The temporary folder that holds the map folder.</summary>
    public string Root => _root;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
