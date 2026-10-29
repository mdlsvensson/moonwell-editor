using Moonwell.GameData;
using MoonwellEditor.Files;

namespace MoonwellEditor.GameData;

/// <summary>
/// The game's standard data: every kind's fields and standard objects and the values World Editor offers for its
/// choice types, worked out by Moonwell's game-data reader from the Warcraft III install, and only from the install: an
/// export of its files may be incomplete (Sven, 2026-10-28).
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

    /// <summary>The install's folder the data was read from.</summary>
    public string Source { get; }

    /// <summary>The install's game version (<c>3.0.1.24371</c>).</summary>
    public string? GameVersion { get; }

    /// <summary>The values World Editor offers, by metadata type (<c>attackType</c>, <c>targetList</c>).</summary>
    public IReadOnlyDictionary<string, ChoiceList> Choices { get; }

    /// <summary>
    /// Checks that <paramref name="folder"/> holds a Warcraft III install: it has <c>.build.info</c>, as the game's
    /// folder does and an export does not.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">It does not.</exception>
    public static void RequireInstall(string folder)
    {
        if (!GameInstall.IsInstall(folder))
        {
            throw new DirectoryNotFoundException(
                Texts.Get("app.gameData.notInstall", ("folder", folder)));
        }
    }

    /// <summary>Reads the game data of the install in <paramref name="folder"/>, as <see cref="Load(GameFiles, string)"/> does.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder holds no install.</exception>
    /// <exception cref="IOException">The install's storage cannot be read.</exception>
    public static GameData Load(string folder, string cacheFolder)
    {
        RequireInstall(folder);
        using var files = GameFiles.Open(folder);
        return Load(files, cacheFolder);
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
        var data = Builder.Build(tables, folder);
        Cache.Write(path, data, stamp);
        return data;
    }

    /// <summary>The fields and standard objects of one kind.</summary>
    public KindData Kind(ObjectKind kind) => _kinds[kind];
}
