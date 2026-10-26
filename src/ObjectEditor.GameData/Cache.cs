using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ObjectEditor.Files;

namespace ObjectEditor.GameData;

/// <summary>
/// The game data of a folder saved as JSON in a cache folder, as <c>&lt;hash of the folder's path&gt;.json</c>, with
/// the newest write time and the number of the folder's files and the version of the code that made it: a cache whose
/// folder or code has changed since is not used.
/// </summary>
internal static class Cache
{
    // Raised whenever what is cached changes (these records, or what Moonwell's generator makes), so an older cache is
    // built again.
    private const int Format = 1;

    /// <summary>
    /// The version of the code that builds the data: this assembly's module id, which a deterministic build changes
    /// whenever its code does, Moonwell's linked generator files and embedded field names included. A cache another
    /// build of the code wrote is built again, so a changed generator never serves stale values.
    /// </summary>
    public static string CodeVersion { get; } = typeof(Cache).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>The cache's file for a game data folder.</summary>
    public static string PathOf(string folder, string cacheFolder)
    {
        var key = Encoding.UTF8.GetBytes(Normalized(folder).ToUpperInvariant());
        var hash = Convert.ToHexStringLower(SHA256.HashData(key)).Substring(0, 16);
        return Path.Combine(cacheFolder, hash + ".json");
    }

    /// <summary>The newest write time and the number of the folder's files, which change when any file does.</summary>
    public static (long Newest, int Files) Stamp(string folder)
    {
        long newest = 0;
        var files = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            newest = Math.Max(newest, file.LastWriteTimeUtc.Ticks);
            files++;
        }

        return (newest, files);
    }

    /// <summary>
    /// The cached data of the folder, or null when there is no cache, it is of another stamp, format or code version,
    /// or it cannot be read.
    /// </summary>
    public static GameData? Read(string path, string folder, (long Newest, int Files) stamp)
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
            || file.Newest != stamp.Newest || file.Files != stamp.Files
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

            kinds[kind] = new KindData(kind, cached.LevelField, cached.Fields, cached.Objects);
        }

        return new GameData(folder, kinds, file.Choices);
    }

    /// <summary>Saves the data, through a temporary file so a reader never sees half a cache.</summary>
    public static void Write(string path, GameData data, (long Newest, int Files) stamp)
    {
        var kinds = new Dictionary<string, CachedKind>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var kindData = data.Kind(kind);
            kinds[ObjectKinds.Key(kind)] = new CachedKind(kindData.LevelField, kindData.Fields, kindData.Objects);
        }

        var file = new CacheFile(
            Format, CodeVersion, Normalized(data.Source), stamp.Newest, stamp.Files, data.Choices, kinds);
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
        long Newest,
        int Files,
        IReadOnlyDictionary<string, ChoiceList> Choices,
        IReadOnlyDictionary<string, CachedKind> Kinds);

    /// <summary>One kind of the cache file, by its <see cref="ObjectKinds.Key"/>.</summary>
    internal sealed record CachedKind(
        string? LevelField,
        IReadOnlyList<FieldInfo> Fields,
        IReadOnlyList<StandardObject> Objects);
}
