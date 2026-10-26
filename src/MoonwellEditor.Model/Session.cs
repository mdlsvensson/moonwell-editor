using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>
/// An open map (a folder or an archive) with the game's standard data. It keeps the map's object files and strings file
/// as read, edits them in place (see <c>Edits.cs</c>) and resolves an object's values on demand, each time it is
/// asked.
/// </summary>
public sealed partial class Session
{
    /// <summary>The problem a Moonwell project's built map gets.</summary>
    internal const string BuildOutputNote =
        "This is Moonwell's build output, rewritten by every build: edit the project's map/ folder";

    /// <summary>The strings file's name.</summary>
    private const string StringsName = "war3map.wts";

    private readonly IMapStore _store;
    private readonly GameData.GameData _game;
    private readonly Func<ObjectKind, byte[], string, ObjectFile> _read;

    // What was read from the map at open (or reload); see Load. A save updates the baseline and the stamp.
    private Dictionary<ObjectKind, MapKind> _kinds;
    private WtsFile? _strings;
    private List<string> _problems;
    private Dictionary<string, byte[]?> _baseline;
    private string _stamp;
    private bool _unreadable;
    private bool _stringsReadOnly;
    private History _history = new();

    private Session(
        IMapStore store, GameData.GameData game, Func<ObjectKind, byte[], string, ObjectFile> read, Loaded loaded)
    {
        _store = store;
        _game = game;
        _read = read;
        _kinds = loaded.Kinds;
        _strings = loaded.Strings;
        _problems = loaded.Problems;
        _baseline = loaded.Baseline;
        _stamp = loaded.Stamp;
        _unreadable = loaded.Unreadable;
        _stringsReadOnly = loaded.StringsReadOnly;
        _search = new SearchIndex(game, _kinds, _strings);
        _savedObjects = Describe();
    }

    /// <summary>
    /// The problems found when opening: one for each object file War3Net cannot read or write back exactly or the
    /// store may not write (its kind is then read only), one when the store may not write the strings file (it is
    /// then never changed: edited text goes into the object files), and a note when the map is a Moonwell project's
    /// build output.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The strings file as edited, or null when the map has none.</summary>
    internal WtsFile? Strings => _strings;

    /// <summary>A kind's main or skin file as edited, or null when there is none.</summary>
    internal ObjectFile? FileOf(ObjectKind kind, bool skin) => skin ? _kinds[kind].Skin : _kinds[kind].Main;

    /// <summary>
    /// Opens a map, a folder or an archive (see <see cref="MapStore.Open"/>). Any of its files may be missing: a kind
    /// without files has only its standard objects, and a map without <c>war3map.wts</c> shows <c>TRIGSTR_n</c>
    /// values as they are.
    /// </summary>
    public static Session Open(IMapStore map, GameData.GameData game) =>
        Open(map, game, (kind, bytes, name) => ObjectFile.Read(kind, bytes, name));

    /// <summary>
    /// <see cref="Open(IMapStore, GameData.GameData)"/> with the object files read by <paramref name="read"/>, which
    /// <see cref="Reload"/> uses too: tests pass one that stands for a file War3Net cannot write back exactly.
    /// </summary>
    internal static Session Open(
        IMapStore map, GameData.GameData game, Func<ObjectKind, byte[], string, ObjectFile> read) =>
        new(map, game, read, Load(map, read));

    /// <summary>
    /// Reads the map's object files (with <paramref name="read"/>) and strings file, keeping each one's bytes as
    /// the baseline a save compares with. The stamp is taken first, so a change made while reading is seen at the
    /// next save. Throws what the store throws when the map cannot be read (see <see cref="Reload"/>).
    /// </summary>
    private static Loaded Load(IMapStore map, Func<ObjectKind, byte[], string, ObjectFile> read)
    {
        var stamp = map.Stamp();
        var problems = new List<string>();
        if (IsMoonwellBuildOutput(map.Path))
        {
            problems.Add(BuildOutputNote);
        }

        var baseline = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var kinds = new Dictionary<ObjectKind, MapKind>();
        var unreadable = false;
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var mainName = "war3map." + ObjectKinds.Extension(kind);
            var skinName = "war3mapSkin." + ObjectKinds.Extension(kind);
            baseline[mainName] = map.Read(mainName);
            baseline[skinName] = map.Read(skinName);
            var kindProblems = new List<string>();
            var main = ReadFile(kind, mainName, baseline[mainName], kindProblems, read);
            var skin = ReadFile(kind, skinName, baseline[skinName], kindProblems, read);
            foreach (var name in new[] { mainName, skinName })
            {
                if (map.WriteProblem(name) is { } writeProblem)
                {
                    kindProblems.Add(writeProblem);
                }
            }

            problems.AddRange(kindProblems);
            unreadable |= (baseline[mainName] != null && main == null) || (baseline[skinName] != null && skin == null);
            var problem = kindProblems.Count == 0 ? null : string.Join(" ", kindProblems);
            kinds[kind] = new MapKind(mainName, main, skinName, skin, problem);
        }

        var stringBytes = map.Read(StringsName);
        baseline[StringsName] = stringBytes;
        var strings = stringBytes == null ? null : WtsFile.Read(stringBytes);
        var stringsProblem = map.WriteProblem(StringsName);
        if (stringsProblem != null)
        {
            problems.Add(stringsProblem + " Edited text goes into the object files instead.");
        }

        return new Loaded(kinds, strings, problems, baseline, stamp, unreadable, stringsProblem != null);
    }

    /// <summary>
    /// The kind's tree: the map's custom objects first, in file order, then every standard object by id.
    /// </summary>
    public IReadOnlyList<TreeEntry> Tree(ObjectKind kind)
    {
        var map = _kinds[kind];
        var data = _game.Kind(kind);
        var resolver = new Resolver(kind, data, _strings);
        var tree = new List<TreeEntry>();
        foreach (var custom in map.Customs())
        {
            var standard = data.Object(custom.BaseId);
            tree.Add(new TreeEntry(
                custom.Id,
                custom.BaseId,
                resolver.Name(standard, custom, custom.Id),
                resolver.Suffix(standard, custom),
                ["Custom"],
                custom.HasMods));
        }

        var originals = map.Originals();
        foreach (var standard in data.Objects)
        {
            var changes = originals.GetValueOrDefault(standard.Id);
            tree.Add(new TreeEntry(
                standard.Id,
                null,
                resolver.Name(standard, changes, standard.Id),
                resolver.Suffix(standard, changes),
                PathOf(kind, standard),
                changes?.HasMods ?? false));
        }

        return tree;
    }

    /// <summary>
    /// A custom object of the map or a standard object, with every field that applies to it; null when the kind has
    /// no object with this id.
    /// </summary>
    public ObjectView? Object(ObjectKind kind, string id)
    {
        var map = _kinds[kind];
        var data = _game.Kind(kind);
        var found = map.Find(id);
        var standard = found is { Custom: true } ? data.Object(found.BaseId) : data.Object(id);
        if (standard == null && found is not { Custom: true })
        {
            return null;
        }

        return new Resolver(kind, data, _strings).View(id, standard, found, map.ReadOnly(found));
    }

    /// <summary>Where a standard object sits in the tree; see <see cref="TreeEntry.Path"/>.</summary>
    internal static IReadOnlyList<string> PathOf(ObjectKind kind, StandardObject standard)
    {
        var race = standard.Race.Length > 0 ? standard.Race : "Other";
        if (kind != ObjectKind.Unit)
        {
            return [race];
        }

        var type = standard.Special ? "Special"
            : standard.Hero ? "Heroes"
            : standard.Building ? "Buildings"
            : "Units";
        return [race, standard.Campaign ? "Campaign" : "Melee", type];
    }

    /// <summary>
    /// Reads one object file from its bytes; null when the map has none (null bytes). A problem is added to
    /// <paramref name="problems"/>: a file War3Net read but cannot write back exactly is still returned, to show (its
    /// kind is then read only, so it is never saved); a file War3Net cannot read gives null.
    /// </summary>
    private static ObjectFile? ReadFile(
        ObjectKind kind,
        string name,
        byte[]? bytes,
        List<string> problems,
        Func<ObjectKind, byte[], string, ObjectFile> read)
    {
        if (bytes == null)
        {
            return null;
        }

        try
        {
            return read(kind, bytes, name);
        }
        catch (ObjectFileException e)
        {
            problems.Add(e.Message);
            return e.Parsed;
        }
    }

    /// <summary>
    /// Whether the map (a folder or an archive file) is a Moonwell build output: the folder it is in is called
    /// <c>build</c> and a <c>.csproj</c> sits beside that (in the project's own folder).
    /// </summary>
    private static bool IsMoonwellBuildOutput(string path)
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(path));
        if (parent == null || !string.Equals(parent.Name, "build", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var project = parent.Parent;
        return project != null && Directory.EnumerateFiles(project.FullName, "*.csproj").Any();
    }

    /// <summary>What <see cref="Load"/> read from the map.</summary>
    /// <param name="Kinds">Each kind's object files.</param>
    /// <param name="Strings">The strings file, or null.</param>
    /// <param name="Problems">The problems found.</param>
    /// <param name="Baseline">Each file's bytes as read (null when the map has none), by name.</param>
    /// <param name="Stamp">The map's <see cref="IMapStore.Stamp"/> before reading.</param>
    /// <param name="Unreadable">True when the map has an object file War3Net cannot read.</param>
    /// <param name="StringsReadOnly">True when the store may not write the strings file.</param>
    private sealed record Loaded(
        Dictionary<ObjectKind, MapKind> Kinds,
        WtsFile? Strings,
        List<string> Problems,
        Dictionary<string, byte[]?> Baseline,
        string Stamp,
        bool Unreadable,
        bool StringsReadOnly);
}

/// <summary>
/// One kind's object files as the map has them: the main and the skin file, either of which may be missing. When one
/// is a problem the kind is read only; a file War3Net read but cannot write back exactly is kept for showing, one it
/// cannot read is missing here.
/// </summary>
internal sealed class MapKind(string mainName, ObjectFile? main, string skinName, ObjectFile? skin, string? problem)
{
    /// <summary>The main file's name (<c>war3map.w3u</c>).</summary>
    public string MainName { get; } = mainName;

    /// <summary>
    /// The main file, or null when the map has none or War3Net cannot read it. An edit sets it when the map has
    /// none.
    /// </summary>
    public ObjectFile? Main { get; set; } = main;

    /// <summary>The skin file's name (<c>war3mapSkin.w3u</c>).</summary>
    public string SkinName { get; } = skinName;

    /// <summary>
    /// The skin file, or null when the map has none or War3Net cannot read it. An edit sets it when the map has
    /// none.
    /// </summary>
    public ObjectFile? Skin { get; set; } = skin;

    /// <summary>Why the kind is read only (its files' problems), or null.</summary>
    public string? Problem { get; } = problem;

    /// <summary>
    /// The map's records of the object with this id: its custom object when it has one, else its changes to the
    /// standard object of that id; null when it has neither.
    /// </summary>
    public MapObject? Find(string id)
    {
        var main = Main?.Custom.FirstOrDefault(entry => entry.Id == id);
        var skin = Skin?.Custom.FirstOrDefault(entry => entry.Id == id);
        if (main != null || skin != null)
        {
            return new MapObject(id, (main ?? skin)!.BaseId, true, main, skin);
        }

        main = Main?.Original.FirstOrDefault(entry => entry.BaseId == id);
        skin = Skin?.Original.FirstOrDefault(entry => entry.BaseId == id);
        return main != null || skin != null ? new MapObject(id, id, false, main, skin) : null;
    }

    /// <summary>The custom objects: the main file's in its order, then those only the skin file has.</summary>
    public IReadOnlyList<MapObject> Customs()
    {
        var ids = new List<string>();
        foreach (var entry in (Main?.Custom ?? []).Concat(Skin?.Custom ?? []))
        {
            if (!ids.Contains(entry.Id))
            {
                ids.Add(entry.Id);
            }
        }

        return ids.Select(id => Find(id)!).ToList();
    }

    /// <summary>The map's changes to standard objects, by the standard object's id.</summary>
    public Dictionary<string, MapObject> Originals()
    {
        // The first entry of an id in each file, as Find takes it.
        var originals = new Dictionary<string, MapObject>(StringComparer.Ordinal);
        foreach (var entry in Main?.Original ?? [])
        {
            originals.TryAdd(entry.BaseId, new MapObject(entry.BaseId, entry.BaseId, false, entry, null));
        }

        foreach (var entry in Skin?.Original ?? [])
        {
            if (!originals.TryGetValue(entry.BaseId, out var found))
            {
                originals[entry.BaseId] = new MapObject(entry.BaseId, entry.BaseId, false, null, entry);
            }
            else if (found.Skin == null)
            {
                originals[entry.BaseId] = found with { Skin = entry };
            }
        }

        return originals;
    }

    /// <summary>
    /// Why an object of this kind cannot be edited: the kind's problem, or a record of the object in a version 3 file
    /// whose set count is not 1 (what more sets would mean is not known). Null when it can be edited.
    /// </summary>
    public string? ReadOnly(MapObject? found)
    {
        if (Problem != null)
        {
            return Problem;
        }

        if (found == null)
        {
            return null;
        }

        foreach (var (file, entry, name) in new[] { (Main, found.Main, MainName), (Skin, found.Skin, SkinName) })
        {
            if (file is { Version: >= 3 } && entry != null && entry.SetCount != 1)
            {
                return $"The record of {found.Id} in {name} has a set count of {entry.SetCount}; "
                    + "only a set count of 1 can be edited.";
            }
        }

        return null;
    }
}

/// <summary>
/// The map's records of one object: its entry in the main file and in the skin file (either may be missing).
/// </summary>
/// <param name="Id">The object's id.</param>
/// <param name="BaseId">The standard object it is based on (its own id for changes to a standard object).</param>
/// <param name="Custom">True for a custom object, false for changes to a standard object.</param>
/// <param name="Main">The entry in the main file, or null.</param>
/// <param name="Skin">The entry in the skin file, or null.</param>
internal sealed record MapObject(string Id, string BaseId, bool Custom, ObjectEntry? Main, ObjectEntry? Skin)
{
    /// <summary>True when either entry has a modification.</summary>
    public bool HasMods => (Main?.Mods.Count ?? 0) + (Skin?.Mods.Count ?? 0) > 0;
}
