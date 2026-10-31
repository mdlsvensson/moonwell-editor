using System.Collections.Concurrent;
using Moonwell.GameData;
using MoonwellEditor.Assets;
using MoonwellEditor.Files;
using MoonwellEditor.Map;

namespace MoonwellEditor.App;

/// <summary>
/// The icons and models the page shows (Sven, 2026-10-27): the open map's own files first (its imports), then the
/// game's, from the Warcraft III install's storage. Icons go to the page as PNG; models and their textures as the bytes
/// the files hold. The install is the one the game data is read from (<see cref="Use"/>); until then, or without one,
/// nothing is found. The map view reads its textures, models and tables through it too.
/// </summary>
public sealed class GameAssets : IDisposable, IMapGameFiles
{
    // The icons decoded lately, by path as asked; the tree asks for the same few hundred again and again.
    private const int IconsKept = 4000;

    private readonly Func<IMapStore?> _map;
    private readonly ConcurrentDictionary<string, byte[]?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _lists = [];
    private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // An install put aside for another stays open until the window closes: a request may still be reading it.
    private readonly List<GameFiles> _retired = [];
    private GameFiles? _files;

    // The install's tables (TerrainArt\Terrain.slk and the like), opened when the map view first asks.
    private GameTables? _tables;

    /// <summary>Makes the assets; <paramref name="map"/> gives the open map.</summary>
    public GameAssets(Func<IMapStore?> map)
    {
        _map = map;
    }

    /// <summary>The Warcraft III install's folder; empty before there is one.</summary>
    public string Folder => _files?.Storage.Folder ?? "";

    /// <summary>
    /// Ends once the first install is open, or has failed to open. Awaiting it never throws; without any install it
    /// never ends, so await it only where game data is loaded.
    /// </summary>
    public Task Loading => _first.Task;

    /// <summary>Why the install could not be opened; null when it was, or while it opens.</summary>
    public string? Error { get; private set; }

    /// <summary>True once an install is open.</summary>
    public bool Ready => _files != null;

    /// <summary>Takes the files of an install open already (the game data's), in place of any before.</summary>
    public void Use(GameFiles files)
    {
        lock (_lists)
        {
            if (_files != null && _files != files)
            {
                _retired.Add(_files);
            }

            _files = files;
            _tables = null;
            _lists.Clear();
        }

        _icons.Clear();
        Error = null;
        _first.TrySetResult();
    }

    /// <summary>Notes why the install could not be opened.</summary>
    public void Failed(string error)
    {
        Error = error;
        _first.TrySetResult();
    }

    /// <summary>
    /// A file's bytes: the map's file of that name, else the game's in the graphics mode (<c>sd</c>, <c>hd</c>,
    /// <c>de</c>); null when neither has it.
    /// </summary>
    public byte[]? File(string path, string mode = "de") => FromMap(path) ?? _files?.Read(path, mode);

    /// <summary>A file of the game itself in a graphics mode, whatever the map holds by that name; null when it has none.</summary>
    public byte[]? GameFile(string path, string mode = "sd") => _files?.Read(path, mode);

    /// <summary>An icon (any texture) as PNG, in a graphics mode; null when there is none or it cannot be decoded.</summary>
    public byte[]? Icon(string path, string mode = "de")
    {
        var key = mode + "|" + path;
        if (_icons.TryGetValue(key, out var known))
        {
            return known;
        }

        var bytes = File(path, mode);
        var image = bytes == null ? null : Textures.Decode(bytes);
        var png = image == null ? null : Textures.Png(image);
        if (_files != null || png != null)
        {
            // A miss before the install is open is asked again later.
            if (_icons.Count >= IconsKept)
            {
                _icons.Clear();
            }

            _icons[key] = png;
        }

        return png;
    }

    /// <summary>Forgets the decoded icons, for a map opened or reloaded (its imports may differ).</summary>
    public void MapChanged() => _icons.Clear();

    /// <summary>
    /// The game's files of a kind, as object data writes their paths: <c>icon</c> (the command and passive buttons,
    /// <c>ReplaceableTextures\CommandButtons\BTNPeasant.blp</c>) or <c>model</c> (every model but portraits,
    /// <c>units\human\Peasant\Peasant.mdl</c>). Empty until the install is open.
    /// </summary>
    public IReadOnlyList<string> List(string kind, string mode = "de")
    {
        var files = _files;
        if (files == null)
        {
            return [];
        }

        lock (_lists)
        {
            if (_lists.TryGetValue(kind + "|" + mode, out var known))
            {
                return known;
            }

            var found = kind switch
            {
                "icon" => files.List("ReplaceableTextures/CommandButtons/", mode, ".blp", ".dds")
                    .Concat(files.List("ReplaceableTextures/PassiveButtons/", mode, ".blp", ".dds"))
                    .Select(path => Written(path, ".blp")),
                "model" => files.List("", mode, ".mdx")
                    .Where(path => !path.EndsWith("_portrait.mdx", StringComparison.OrdinalIgnoreCase))
                    .Select(path => Written(path, ".mdl")),
                _ => [],
            };
            return _lists[kind + "|" + mode] = found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <inheritdoc />
    IReadOnlyList<string> IMapGameFiles.List(string folder, string mode, string extension) =>
        _files?.List(folder, mode, extension) ?? [];

    /// <inheritdoc />
    public IReadOnlyList<MetaRow>? Table(string relative)
    {
        lock (_lists)
        {
            if (_files == null)
            {
                return null;
            }

            _tables ??= GameTables.OpenInstall(_files);
            return _tables.Table(relative);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _tables?.Dispose();
        _files?.Dispose();
        foreach (var files in _retired)
        {
            files.Dispose();
        }
    }

    // As World Editor writes a path into object data: back slashes, and the extension it names files by.
    private static string Written(string path, string extension) =>
        Path.ChangeExtension(path, extension).Replace('/', '\\');

    // The map's own file of that path, with either slash (imports are named war3mapImported\x.blp and the like). Object
    // data names a model .mdl, and the map keeps an imported one as .mdx, as the game keeps its own.
    private byte[]? FromMap(string path)
    {
        var map = _map();
        if (map == null)
        {
            return null;
        }

        try
        {
            return map.ReadImport(path)
                ?? (Path.GetExtension(path).Equals(".mdl", StringComparison.OrdinalIgnoreCase)
                    ? map.ReadImport(Path.ChangeExtension(path, ".mdx"))
                    : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or MapArchiveException)
        {
            // An import there but not readable (a protected map's bogus entry) is as good as none: the game's is next.
            return null;
        }
    }
}
