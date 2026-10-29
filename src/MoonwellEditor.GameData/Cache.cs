using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonwell.GameData;
using MoonwellEditor.Files;

namespace MoonwellEditor.GameData;

/// <summary>
/// The game data of an install saved as JSON in a cache folder, as <c>&lt;hash of the folder's path&gt;.json</c>,
/// with a stamp of the build it was read from (its build key, which every update of the game changes) and the version
/// of the code that made it: a cache whose game or code has changed since is not used.
/// </summary>
internal static class Cache
{
    // Raised whenever what is cached changes (these records, or what Moonwell's reader makes), so an older cache is
    // built again.
    private const int Format = 13;

    /// <summary>
    /// The version of the code that builds the data: the module ids of this assembly and of Moonwell's reader
    /// (<c>Moonwell.GameData</c>, with its embedded field names), which a deterministic build changes whenever its
    /// code does. A cache another build of either wrote is built again, so a new reader never serves stale values.
    /// </summary>
    public static string CodeVersion { get; } =
        typeof(Cache).Assembly.ManifestModule.ModuleVersionId.ToString("N")
        + typeof(GameTables).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>The cache's file for a game data folder.</summary>
    public static string PathOf(string folder, string cacheFolder)
    {
        var key = Encoding.UTF8.GetBytes(Normalized(folder).ToUpperInvariant());
        var hash = Convert.ToHexStringLower(SHA256.HashData(key)).Substring(0, 16);
        return Path.Combine(cacheFolder, hash + ".json");
    }

    /// <summary>An install's stamp: its build key.</summary>
    public static string Stamp(GameBuild build) => "build " + build.Key;

    /// <summary>
    /// The cached data of the folder, or null when there is no cache, it is of another stamp, format or code version,
    /// or it cannot be read.
    /// </summary>
    public static GameData? Read(string path, string folder, string stamp)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        CacheFile? file;
        try
        {
            using var stream = File.OpenRead(path);
            file = JsonSerializer.Deserialize<CacheFile>(stream);
        }
        catch (Exception failure)
            when (failure is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        if (file?.Kinds == null || file.Choices == null || file.Format != Format || file.Code != CodeVersion
            || file.Stamp != stamp
            || !string.Equals(file.Folder, Normalized(folder), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var kinds = new Dictionary<ObjectKind, KindData>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            if (!file.Kinds.TryGetValue(ObjectKinds.Key(kind), out var cached))
            {
                return null;
            }

            kinds[kind] = new KindData(kind, cached.LevelField, cached.Fields, cached.Objects, cached.Effects);
        }

        return new GameData(folder, file.GameVersion, kinds, file.Choices);
    }

    /// <summary>Saves the data, through a temporary file so a reader never sees half a cache.</summary>
    public static void Write(string path, GameData data, string stamp)
    {
        var kinds = new Dictionary<string, CachedKind>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var kindData = data.Kind(kind);
            kinds[ObjectKinds.Key(kind)] = new CachedKind(
                kindData.LevelField, kindData.Fields, kindData.Objects, kindData.Effects);
        }

        var file = new CacheFile(
            Format, CodeVersion, Normalized(data.Source), stamp, data.GameVersion, data.Choices, kinds);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, file);
        }

        File.Move(temporary, path, true);
    }

    private static string Normalized(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    /// <summary>The cache file.</summary>
    internal sealed record CacheFile(
        int Format,
        string Code,
        string Folder,
        string Stamp,
        string? GameVersion,
        IReadOnlyDictionary<string, ChoiceList> Choices,
        IReadOnlyDictionary<string, CachedKind> Kinds);

    /// <summary>One kind of the cache file, by its <see cref="ObjectKinds.Key"/>.</summary>
    internal sealed record CachedKind(
        string? LevelField,
        IReadOnlyList<FieldInfo> Fields,
        IReadOnlyList<StandardObject> Objects,
        IReadOnlyDictionary<string, IReadOnlyList<UpgradeEffectField>>? Effects);
}
