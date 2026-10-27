using MoonwellEditor.Files;

namespace MoonwellEditor.GameData;

/// <summary>
/// The game's standard data: every kind's fields and standard objects and the values World Editor offers for its
/// choice types, worked out by Moonwell's game-data reader from a game data folder (a <c>war3.w3mod</c> export).
/// </summary>
public sealed class GameData
{
    private readonly Dictionary<ObjectKind, KindData> _kinds;

    internal GameData(
        string source,
        Dictionary<ObjectKind, KindData> kinds,
        IReadOnlyDictionary<string, ChoiceList> choices)
    {
        Source = source;
        _kinds = kinds;
        Choices = choices;
    }

    /// <summary>The game data folder the data was read from.</summary>
    public string Source { get; }

    /// <summary>The values World Editor offers, by metadata type (<c>attackType</c>, <c>targetList</c>).</summary>
    public IReadOnlyDictionary<string, ChoiceList> Choices { get; }

    /// <summary>
    /// Reads the game data folder with Moonwell's game-data reader, or reads the cache in
    /// <paramref name="cacheFolder"/> when the folder's files are unchanged since the cache was written (the app passes
    /// <c>%LOCALAPPDATA%\MoonwellEditor\gamedata</c>). A build writes the cache.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static GameData Load(string folder, string cacheFolder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"the game data folder {folder} does not exist");
        }

        var path = Cache.PathOf(folder, cacheFolder);
        var stamp = Cache.Stamp(folder);
        var cached = Cache.Read(path, folder, stamp);
        if (cached != null)
        {
            return cached;
        }

        var data = Builder.Build(folder);
        Cache.Write(path, data, stamp);
        return data;
    }

    /// <summary>The fields and standard objects of one kind.</summary>
    public KindData Kind(ObjectKind kind) => _kinds[kind];
}
