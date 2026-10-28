using Moonwell.GameData;
using MoonwellEditor.Files;

namespace MoonwellEditor.GameData;

/// <summary>
/// The game's standard data: every kind's fields and standard objects and the values World Editor offers for its
/// choice types, worked out by Moonwell's game-data reader from the Warcraft III install (Sven, 2026-10-28), or from a
/// <c>war3.w3mod</c> export of it.
/// </summary>
public sealed class GameData
{
    private readonly Dictionary<ObjectKind, KindData> _kinds;

    internal GameData(
        string source,
        string? gameVersion,
        Dictionary<ObjectKind, KindData> kinds,
        IReadOnlyDictionary<string, ChoiceList> choices)
    {
        Source = source;
        GameVersion = gameVersion;
        _kinds = kinds;
        Choices = choices;
    }

    /// <summary>The install's (or the export's) folder the data was read from.</summary>
    public string Source { get; }

    /// <summary>The install's game version (<c>3.0.1.24371</c>); null for an export, which does not say.</summary>
    public string? GameVersion { get; }

    /// <summary>The values World Editor offers, by metadata type (<c>attackType</c>, <c>targetList</c>).</summary>
    public IReadOnlyDictionary<string, ChoiceList> Choices { get; }

    /// <summary>True when the folder holds a Warcraft III install (it has <c>.build.info</c>), not an export.</summary>
    public static bool IsInstall(string folder) => File.Exists(Path.Combine(folder, ".build.info"));

    /// <summary>
    /// Reads the game data of the install in <paramref name="folder"/>, or of the export there, as
    /// <see cref="Load(GameFiles, string)"/> does.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist, or holds neither an install nor an export.</exception>
    /// <exception cref="IOException">The install's storage cannot be read.</exception>
    public static GameData Load(string folder, string cacheFolder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"the folder {folder} does not exist");
        }

        if (IsInstall(folder))
        {
            using var files = GameFiles.Open(folder);
            return Load(files, cacheFolder);
        }

        if (!Directory.Exists(Path.Combine(folder, "units")))
        {
            throw new DirectoryNotFoundException(
                $"{folder} holds no Warcraft III install (it has no .build.info) and no war3.w3mod export");
        }

        var path = Cache.PathOf(folder, cacheFolder);
        var stamp = Cache.Stamp(folder);
        var cached = Cache.Read(path, folder, stamp);
        if (cached != null)
        {
            return cached;
        }

        using var tables = GameTables.OpenExport(folder);
        var data = Builder.Build(tables, folder, relative => ReadExport(folder, relative));
        Cache.Write(path, data, stamp);
        return data;
    }

    /// <summary>
    /// Reads the game data of an open install with Moonwell's game-data reader, or reads the cache in
    /// <paramref name="cacheFolder"/> when it was written for the same build of the game (the app passes
    /// <c>%LOCALAPPDATA%\MoonwellEditor\gamedata</c>). A build writes the cache; an update of the game is a new build.
    /// </summary>
    /// <exception cref="FileNotFoundException">The installed locale's files are not stored on this computer.</exception>
    public static GameData Load(GameFiles files, string cacheFolder)
    {
        var folder = files.Storage.Folder;
        var path = Cache.PathOf(folder, cacheFolder);
        var stamp = Cache.Stamp(files.Build);
        var cached = Cache.Read(path, folder, stamp);
        if (cached != null)
        {
            return cached;
        }

        using var tables = GameTables.OpenInstall(files);
        var data = Builder.Build(tables, folder, relative => files.Storage.Read("War3.w3mod:" + relative));
        Cache.Write(path, data, stamp);
        return data;
    }

    /// <summary>The fields and standard objects of one kind.</summary>
    public KindData Kind(ObjectKind kind) => _kinds[kind];

    // A file of an export by its path in the tables; null when there is none.
    private static byte[]? ReadExport(string folder, string relative)
    {
        var path = Path.Combine(folder, relative);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
