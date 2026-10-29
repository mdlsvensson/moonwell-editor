using System.Text.Json;
using MoonwellEditor.Files;
using MoonwellEditor.Map;
using MoonwellEditor.Model;
using ChoiceList = MoonwellEditor.GameData.ChoiceList;
using Data = MoonwellEditor.GameData.GameData;

namespace MoonwellEditor.App;

/// <summary>Where the game data stands: which folder, whether it is loaded, and why it failed if it did.</summary>
/// <param name="Source">The install's folder being used, or null when none is chosen or found.</param>
/// <param name="Loaded">True once the data is ready for the commands that need it.</param>
/// <param name="Error">Why the load failed, or null.</param>
/// <param name="Version">
/// Grows by one each time the state changes, so the page can drop a state older than the one it has (a reply that
/// was on its way when a newer event came).
/// </param>
/// <param name="Game">The install's game version once loaded (<c>3.0.1.24371</c>); null before.</param>
public sealed record GameDataState(string? Source, bool Loaded, string? Error, int Version, string? Game = null);

/// <summary>Where the open map stands, sent with the <c>sessionChanged</c> event.</summary>
/// <param name="Dirty">True when there are edits that are not saved.</param>
/// <param name="ChangedObjects">
/// How many objects differ from the map as opened or last saved (see <see cref="Session.ChangedObjects"/>).
/// </param>
public sealed record SessionState(bool Dirty, int ChangedObjects);

/// <summary>What the app was started with, for the page to act on once it is ready.</summary>
/// <param name="Map">
/// The map (a folder or an archive file) given as the app's first command-line argument, which the page opens once the
/// game data is ready; null when there was none, or once the page has asked.
/// </param>
public sealed record Startup(string? Map);

/// <summary>What the About page shows: the versions of the editor and of what it runs on.</summary>
/// <param name="Version">The editor's version (<c>0.1.0</c>), set in Directory.Build.props.</param>
/// <param name="Moonwell">The version of the Moonwell packages the editor is built with.</param>
/// <param name="DotNet">The .NET runtime's version.</param>
/// <param name="WebView2">The WebView2 runtime's version; null when it cannot be read.</param>
public sealed record About(string Version, string Moonwell, string DotNet, string? WebView2);

/// <summary>The map that was opened or reloaded.</summary>
/// <param name="Name">The map folder's name, or the archive's file name (<c>Arena.w3x</c>).</param>
/// <param name="Path">The map folder's or the archive file's full path.</param>
/// <param name="Archive">True for an archive (<c>.w3x</c>, <c>.w3m</c>), false for a folder.</param>
/// <param name="Problems">What was wrong with its files (see <see cref="Session.Problems"/>).</param>
/// <param name="Counts">How many custom objects it has, by kind key (<c>unit</c>).</param>
/// <param name="Title">The map's name from its info file (Scenario > Map Properties); null when it has none.</param>
/// <param name="Expansion">True for a Frozen Throne map, false for Reign of Chaos; null when unknown.</param>
/// <param name="Options">
/// Its game data option (World Editor's Data Set and Data Version, and the tables the game reads under it); null when
/// its <c>war3map.w3i</c> is missing or cannot be read.
/// </param>
public sealed record MapInfo(
    string Name,
    string Path,
    bool Archive,
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, int> Counts,
    MapOptions? Options,
    string? Title = null,
    bool? Expansion = null);

/// <summary>The open map and where it stands, for a page that was loaded again (the <c>currentMap</c> command).</summary>
/// <param name="Map">The open map, as <c>openMap</c> gave it.</param>
/// <param name="Session">Where it stands: whether it has unsaved edits.</param>
public sealed record CurrentMap(MapInfo Map, SessionState Session);

/// <summary>One field of a kind, for the table's column chooser (the <c>fields</c> command).</summary>
/// <param name="Id">The field id (<c>uhpm</c>).</param>
/// <param name="Label">World Editor's label for the field.</param>
/// <param name="Category">The metadata's category (<c>stats</c>); empty for none.</param>
/// <param name="Sort">The metadata's sort key, World Editor's order within the category; empty for none.</param>
/// <param name="Type">The kind of value: <c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>, <c>choice</c> ...</param>
/// <param name="PerLevel">True when the field has a value for each level.</param>
/// <param name="Choice">For a <c>choice</c> field, the key of its list in <c>choices</c>; null for other types.</param>
/// <param name="ListOf">For an object list or reference, what it holds; null for other types.</param>
/// <param name="Meta">The metadata's type as written (<c>model</c>), which says how World Editor shows the field.</param>
public sealed record FieldDescription(
    string Id,
    string Label,
    string Category,
    string Sort,
    string Type,
    bool PerLevel,
    string? Choice,
    string? ListOf,
    string Meta = "");

/// <summary>The game's files of a kind, for the icon and model pickers (the <c>assetList</c> command).</summary>
/// <param name="Ready">True once the Warcraft III install is open.</param>
/// <param name="Error">Why it could not be opened; null when it was, or while it opens.</param>
/// <param name="Folder">The install's folder.</param>
/// <param name="Paths">The files, as object data writes their paths; empty until the install is open.</param>
public sealed record AssetList(bool Ready, string? Error, string Folder, IReadOnlyList<string> Paths);

/// <summary>The window's state, for the page's title bar (the <c>windowState</c> command and event).</summary>
/// <param name="Maximized">True when the window is maximized.</param>
public sealed record WindowState(bool Maximized);

/// <summary>What <c>setCells</c> answers with.</summary>
/// <param name="Changed">How many cells changed (a cell that already had its value changes nothing).</param>
public sealed record CellsChanged(int Changed);

/// <summary>
/// The commands the page can call, the game data they work on and the open map. The window gives it what it cannot
/// do itself (a way to ask the user for a folder or an archive, a way to load the game data), so it can be tested
/// without a window.
/// The window runs one command at a time; the map's commands rely on that.
/// </summary>
public sealed class Commands
{
    private readonly Settings _settings;
    private readonly Func<string, string?> _askFolder;
    private readonly Func<string, string?> _askArchive;
    private readonly Func<string, Data> _load;
    private readonly Func<string?> _findGame;
    private readonly object _lock = new();

    // The map from the command line, until the page asks for it.
    private string? _startupMap;

    private string? _source;
    private Data? _data;

    // The names World Editor shows files by, made once per game data.
    private (Data Data, Dictionary<string, Dictionary<string, List<string>>> Names)? _assetNames;
    private (Data Data, IReadOnlyDictionary<string, ChoiceList> Lists)? _choices;
    private string? _error;
    private int _generation;
    private int _version;

    // The open map; null until one is opened.
    private IMapStore? _store;
    private Session? _session;

    // The terrain bytes of the last map scene, for the page's /terrain request; null before one.
    private TerrainCopy? _terrain;

    /// <summary>Makes the commands.</summary>
    /// <param name="settings">Where the Warcraft III folder chosen is remembered.</param>
    /// <param name="askFolder">
    /// Asks the user for a folder, with the text given for the dialog; gives null when they cancel. It may be called on
    /// any thread.
    /// </param>
    /// <param name="load">
    /// Loads the game data from an install's folder; the app uses <c>GameData.Load</c> with its cache folder, which
    /// refuses any other folder.
    /// </param>
    /// <param name="startupMap">
    /// The map (a folder or an archive file) the app was started with (its first command-line argument), which the
    /// <c>startup</c> command gives once; null for none.
    /// </param>
    /// <param name="askArchive">
    /// Asks the user for a map archive (a <c>.w3x</c> or <c>.w3m</c> file), with the text given for the dialog; gives
    /// null when they cancel. It may be called on any thread. Null for none: <c>openMap</c> with <c>archive</c> then
    /// opens nothing, as if the user cancelled.
    /// </param>
    /// <param name="findGame">
    /// Finds the Warcraft III install when none is chosen (the app uses <c>GameInstall.Find</c>); null when there is
    /// none. Null for no search.
    /// </param>
    public Commands(
        Settings settings,
        Func<string, string?> askFolder,
        Func<string, Data> load,
        string? startupMap = null,
        Func<string, string?>? askArchive = null,
        Func<string?>? findGame = null)
    {
        _findGame = findGame ?? (() => null);
        _settings = settings;
        _askFolder = askFolder;
        _askArchive = askArchive ?? (_ => null);
        _load = load;
        _startupMap = string.IsNullOrEmpty(startupMap) ? null : startupMap;
    }

    /// <summary>Raised, on a background thread, when a load ends (well or badly).</summary>
    public event Action<GameDataState>? GameDataChanged;

    /// <summary>
    /// Raised, on the thread the command runs on, after a map is opened or reloaded and after every edit, undo, redo
    /// and save.
    /// </summary>
    public event Action<SessionState>? SessionChanged;

    /// <summary>
    /// Raised, on the thread the command runs on, when the page says it is asking the user what to do with the
    /// unsaved edits before the window closes (the <c>closeAsking</c> command, its answer to <c>closeRequested</c>).
    /// </summary>
    public event Action? CloseAsking;

    /// <summary>
    /// Raised, on the thread the command runs on, when the page says the window may close: the edits were saved, or
    /// the user chose to discard them (the <c>closeWindow</c> command).
    /// </summary>
    public event Action? CloseApproved;

    /// <summary>Raised when the page's title bar minimizes the window (the <c>minimizeWindow</c> command).</summary>
    public event Action? MinimizeAsked;

    /// <summary>
    /// Raised when the page's title bar maximizes the window, or restores it when it is maximized (the
    /// <c>maximizeWindow</c> command).
    /// </summary>
    public event Action? MaximizeAsked;

    /// <summary>
    /// Raised when the page's title bar closes the window (the <c>closeClicked</c> command): the window closes as with
    /// any close, asking the page about unsaved edits first.
    /// </summary>
    public event Action? CloseClicked;

    /// <summary>
    /// Raised when the page puts on a theme (the <c>frameColors</c> command): its title bar colour and its border colour,
    /// as <c>#rrggbb</c>, for the window's frame.
    /// </summary>
    public event Action<string, string>? FrameColorsChanged;

    /// <summary>
    /// The steps the page can be scaled to, in percent (the <c>uiScale</c> command; Sven, 2026-10-27: 80, 100 and 120
    /// for now).
    /// </summary>
    public static readonly int[] UiScales = [80, 100, 120];

    /// <summary>Raised when the page asks for another scale (the <c>uiScale</c> command), in percent.</summary>
    public event Action<int>? UiScaleChanged;

    /// <summary>The icons and models of the game and the open map; null when the window has none (the tests).</summary>
    public GameAssets? Assets { get; set; }

    /// <summary>The open map's files; null until a map is open.</summary>
    public IMapStore? Store => _store;

    /// <summary>Whether the window is maximized, for the page's title bar (the <c>windowState</c> command).</summary>
    public Func<bool> Maximized { get; set; } = () => false;

    /// <summary>True when a map is open with edits that are not saved.</summary>
    public bool Dirty => _session?.Dirty ?? false;

    /// <summary>The background load that is running or ran last. Awaiting it never throws.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts loading the game data from the folder in the settings, else from the install it finds; with neither the
    /// page asks for the folder.
    /// </summary>
    /// <returns>The background load, which ends when the data is loaded or has failed.</returns>
    public Task StartLoading()
    {
        var folder = _settings.GameFolder ?? _findGame();
        if (!string.IsNullOrEmpty(folder))
        {
            Begin(folder, false);
        }

        return Loading;
    }

    /// <summary>The loaded game data, for the commands that need it.</summary>
    /// <exception cref="CommandException"><c>gameDataMissing</c> with no folder set, <c>gameDataLoading</c> while it loads, <c>gameDataFailed</c> when the load failed.</exception>
    public Data NeedGameData()
    {
        lock (_lock)
        {
            if (_data != null)
            {
                return _data;
            }

            if (_source == null)
            {
                throw new CommandException("gameDataMissing", Texts.Get("app.error.gameDataMissing"));
            }

            throw _error == null
                ? new CommandException("gameDataLoading", Texts.Get("app.error.gameDataLoading"))
                : new CommandException("gameDataFailed", Texts.Get("app.error.gameDataFailed", ("error", _error)));
        }
    }

    /// <summary>Runs one command by name.</summary>
    /// <param name="command">The command's name.</param>
    /// <param name="args">The command's arguments, as the page sent them.</param>
    /// <returns>The command's result.</returns>
    /// <exception cref="UnknownCommandException">There is no command of that name.</exception>
    /// <exception cref="CommandException">
    /// The command failed for a reason the page can act on: an <see cref="EditException"/>'s code,
    /// <c>changedOnDisk</c>, <c>mapFile</c> (a save or reload could not read or write the map; see
    /// <see cref="MapFileException"/>), <c>noMap</c>, <c>unsaved</c> (<c>openMap</c> or <c>reload</c> would drop unsaved edits
    /// and the page did not say to discard them), <c>badRequest</c> for a missing or wrong argument, or a game data
    /// code (see <see cref="NeedGameData"/>).
    /// </exception>
    public Task<object?> Run(string command, JsonElement args)
    {
        // The lists World Editor offers from the install's files (ground textures) wait for the install to open.
        if (command == "choices" && Assets is { Ready: false, Error: null } assets && Loaded)
        {
            return assets.Loading.ContinueWith(_ => Run(command, args), TaskScheduler.Default).Unwrap();
        }

        try
        {
            return Task.FromResult(RunNow(command, args));
        }
        catch (EditException exception)
        {
            throw new CommandException(exception.Code, exception.Message);
        }
        catch (ChangedOnDiskException exception)
        {
            throw new CommandException("changedOnDisk", exception.Message);
        }
        catch (MapFileException exception)
        {
            throw new CommandException("mapFile", exception.Message);
        }
    }

    private object? RunNow(string command, JsonElement args) =>
        command switch
        {
            "ping" => "pong",
            "gameData" => State(),
            "startup" => TakeStartup(),
            "about" => AboutApp(),
            "chooseGameData" => ChooseGameData(),
            "openMap" => OpenMap(OptionalText(args, "path"), Flag(args, "archive"), Flag(args, "discard")),
            "currentMap" => Current(),
            "tree" => Map().Tree(Kind(args)),
            "object" => ObjectOf(Kind(args), Text(args, "id")),
            "usedBy" => Map().UsedBy(Kind(args), Text(args, "id")),
            "setField" => SetField(args),
            "resetField" => ResetField(args),
            "createObject" => Changed(Map().Create(Kind(args), Text(args, "baseId"), OptionalText(args, "id"))),
            "copyObject" => Changed(Map().Copy(Kind(args), Text(args, "fromId"), OptionalText(args, "id"))),
            "nextId" => Map().NextId(Kind(args), Text(args, "baseId")),
            "deleteObject" => DeleteObject(args),
            "undo" => Changed(Map().Undo()),
            "redo" => Changed(Map().Redo()),
            "save" => Changed(Map().Save()),
            "reload" => Reload(Flag(args, "discard")),
            "search" => Map().Search(Text(args, "query"), Number(args, "limit")),
            "choices" => ChoicesOf(NeedGameData()),
            "assetNames" => AssetNamesOf(NeedGameData()),
            "fields" => Fields(Kind(args)),
            "table" => TableOf(args),
            "setCells" => SetCells(args),
            "closeAsking" => Raise(CloseAsking),
            "closeWindow" => Raise(CloseApproved),
            "minimizeWindow" => Raise(MinimizeAsked),
            "maximizeWindow" => Raise(MaximizeAsked),
            "closeClicked" => Raise(CloseClicked),
            "windowState" => new WindowState(Maximized()),
            "frameColors" => FrameColors(Text(args, "surface"), Text(args, "border")),
            "uiScale" => UiScale(Number(args, "percent")),
            "assetList" => AssetsOf(Text(args, "kind"), OptionalText(args, "mode") ?? "de"),
            "mapScene" => MapSceneOf(OptionalText(args, "mode") ?? "de"),
            _ => throw new UnknownCommandException(command),
        };

    // The map from the command line the first time, so a page that is loaded again does not open it again.
    private Startup TakeStartup()
    {
        var map = _startupMap;
        _startupMap = null;
        return new Startup(map);
    }

    private static About AboutApp()
    {
        string? webView2;
        try
        {
            webView2 = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            webView2 = null;
        }

        return new About(
            VersionOf(typeof(Commands)),
            VersionOf(typeof(ObjectKind)),
            Environment.Version.ToString(),
            webView2);
    }

    // An assembly's version as three numbers (0.1.0), as the project sets it.
    private static string VersionOf(Type type) => type.Assembly.GetName().Version?.ToString(3) ?? "";

    // Opens a map, a folder or an archive file. With no path given it asks for one: an archive in the Open File dialog
    // when the page says archive, else a folder. Null when the user cancels the dialog. The game data and the unsaved
    // edits are checked first, so the user is not asked for a map that cannot be opened yet. A map that cannot be
    // opened (none there, an archive War3Net cannot read) throws, and the page gets "failed" with the reason.
    private MapInfo? OpenMap(string? path, bool archive, bool discard)
    {
        var data = NeedGameData();
        RefuseUnsaved(discard, "app.error.unsavedOpen");
        path ??= archive
            ? _askArchive(Texts.Get("app.choose.archive"))
            : _askFolder(Texts.Get("app.choose.folder"));
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var store = MapStore.Open(path);
        var session = Session.Open(store, data);
        _store = store;
        _terrain = null;
        Assets?.MapChanged();
        _session = session;
        try
        {
            _settings.RememberMap(store.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A settings file that cannot be written only means the map is not in the recent list next time.
        }

        return Changed(Info(store, session));
    }

    private MapInfo Reload(bool discard)
    {
        var session = Map();
        RefuseUnsaved(discard, "app.error.unsavedReload");
        session.Reload();
        _terrain = null;
        Assets?.MapChanged();
        return Changed(Info(_store!, session));
    }

    // Unsaved edits are dropped only when the page says so: its dialog asked the user, who chose Discard (or saved). A
    // page that was loaded again, and so does not know about them, cannot drop them by mistake.
    private void RefuseUnsaved(bool discard, string text)
    {
        if (!discard && Dirty)
        {
            throw new CommandException("unsaved", Texts.Get(text));
        }
    }

    // The open map for a page that was loaded again (after a reload of the page, or a crash of its process).
    private CurrentMap? Current()
    {
        var session = _session;
        return session == null
            ? null
            : new CurrentMap(Info(_store!, session), new SessionState(session.Dirty, session.ChangedObjects()));
    }

    private static MapInfo Info(IMapStore store, Session session)
    {
        var counts = new Dictionary<string, int>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            counts[Kinds.Key(kind)] = session.Tree(kind).Count(entry => entry.BaseId != null);
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(store.Path));
        var header = MapOptions.Header(store);
        return new MapInfo(
            name,
            store.Path,
            store.IsArchive,
            session.Problems,
            counts,
            MapOptions.Read(store),
            header is { } known ? session.Text(known.Name) : null,
            header?.Expansion);
    }

    // An object of a read-only kind is shown too: its view says why it cannot be edited.
    private ObjectView ObjectOf(ObjectKind kind, string id) =>
        Map().Object(kind, id)
        ?? throw new CommandException(
            "noObject", Texts.Get("app.error.noObject", ("kind", Kinds.Key(kind)), ("id", id)));

    private ObjectView SetField(JsonElement args)
    {
        var session = Map();
        var kind = Kind(args);
        var id = Text(args, "id");
        session.Set(kind, id, Text(args, "field"), Number(args, "level"), Text(args, "value"));
        return Changed(ObjectOf(kind, id));
    }

    private ObjectView ResetField(JsonElement args)
    {
        var session = Map();
        var kind = Kind(args);
        var id = Text(args, "id");
        session.Reset(kind, id, Text(args, "field"), Number(args, "level"));
        return Changed(ObjectOf(kind, id));
    }

    /// <summary>
    /// The game data's lists, with the lists World Editor offers for fields the data keeps as text (the <c>choices</c>
    /// command): <c>shadowTexture</c> from the standard objects, and <c>uberSplat</c> from the install's splat table
    /// and World Editor's strings, when the install is open (Sven, 2026-10-27).
    /// </summary>
    private IReadOnlyDictionary<string, ChoiceList> ChoicesOf(Data data)
    {
        lock (_lock)
        {
            if (_choices is { } made && ReferenceEquals(made.Data, data))
            {
                return made.Lists;
            }
        }

        var lists = new Dictionary<string, ChoiceList>(data.Choices, StringComparer.Ordinal)
        {
            ["shadowTexture"] = OfferedLists.Shadows(data),
        };
        var assets = Assets;
        if (assets is { Ready: true })
        {
            var splats = OfferedLists.Splats(
                assets.File(@"Splats\UberSplatData.slk", "sd"),
                OfferedLists.Strings(assets.File(@"UI\WorldEditStrings.txt", "sd")));
            if (splats != null)
            {
                lists["uberSplat"] = splats;
            }
        }

        lock (_lock)
        {
            // Without the install the splats are asked for again next time.
            if (assets is not { Ready: false })
            {
                _choices = (data, lists);
            }
        }

        return lists;
    }

    // The kind's fields in World Editor's order: by category, then by sort key, then by id.
    /// <summary>
    /// The names World Editor shows an icon, model or texture by (<see cref="AssetNames.Of"/>), by sort of file, then
    /// by file key (the <c>assetNames</c> command).
    /// </summary>
    private Dictionary<string, Dictionary<string, List<string>>> AssetNamesOf(Data data)
    {
        lock (_lock)
        {
            if (_assetNames is { } made && ReferenceEquals(made.Data, data))
            {
                return made.Names;
            }
        }

        var names = AssetNames.Of(data);
        lock (_lock)
        {
            _assetNames = (data, names);
        }

        return names;
    }

    private List<FieldDescription> Fields(ObjectKind kind)
    {
        var fields = new List<FieldDescription>();
        foreach (var field in NeedGameData().Kind(kind).Fields)
        {
            fields.Add(new FieldDescription(
                field.Id,
                field.Label,
                field.Category,
                field.Sort,
                field.Type,
                field.PerLevel,
                field.Choice,
                field.ListOf,
                field.Meta));
        }

        fields.Sort((a, b) =>
        {
            var byCategory = string.CompareOrdinal(a.Category, b.Category);
            if (byCategory != 0)
            {
                return byCategory;
            }

            var bySort = string.CompareOrdinal(a.Sort, b.Sort);
            return bySort != 0 ? bySort : string.CompareOrdinal(a.Id, b.Id);
        });
        return fields;
    }

    private IReadOnlyList<TableRow> TableOf(JsonElement args)
    {
        var session = Map();
        var kind = Kind(args);
        var ids = new List<string>();
        foreach (var item in ArrayOf(args, "ids"))
        {
            ids.Add(item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw BadRequest("The argument ids must be a list of text."));
        }

        var columns = new List<Column>();
        foreach (var item in ArrayOf(args, "columns"))
        {
            columns.Add(new Column(Text(item, "field"), Number(item, "level")));
        }

        return session.Table(kind, ids, columns);
    }

    private CellsChanged SetCells(JsonElement args)
    {
        var session = Map();
        var kind = Kind(args);
        var edits = new List<CellEdit>();
        foreach (var item in ArrayOf(args, "edits"))
        {
            // The value must be there, as text or null: a missing one is not taken for a reset.
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("value", out _))
            {
                throw BadRequest("Each edit needs an id, a field, a level and a value (text, or null to reset).");
            }

            edits.Add(new CellEdit(
                Text(item, "id"), Text(item, "field"), Number(item, "level"), OptionalText(item, "value")));
        }

        var changed = session.SetCells(kind, edits);
        return changed == 0 ? new CellsChanged(0) : Changed(new CellsChanged(changed));
    }

    private object? DeleteObject(JsonElement args)
    {
        Map().Delete(Kind(args), Text(args, "id"));
        return Changed<object?>(null);
    }

    private AssetList AssetsOf(string kind, string mode)
    {
        if (kind is not ("icon" or "model"))
        {
            throw BadRequest($"The kind {kind} is not icon or model.");
        }

        var assets = Assets;
        return assets == null
            ? new AssetList(false, Texts.Get("app.assets.none"), "", [])
            : new AssetList(assets.Ready, assets.Error, assets.Folder, assets.List(kind, mode));
    }

    /// <summary>
    /// The terrain bytes of the last <c>mapScene</c> in <paramref name="mode"/> (see <c>TerrainBytes</c>); null before
    /// one, or after a map was opened since.
    /// </summary>
    public byte[]? Terrain(string mode) => Volatile.Read(ref _terrain) is { } copy && copy.Mode == mode ? copy.Bytes : null;

    // The map view's scene of the open map in a graphics mode, its terrain kept for the page's /terrain request. It
    // reads the map and writes nothing.
    private MapSceneView MapSceneOf(string mode)
    {
        var session = Map();
        if (mode is not ("sd" or "hd" or "de"))
        {
            throw BadRequest($"The graphics mode {mode} is not sd, hd or de.");
        }

        var assets = Assets ?? throw new CommandException("noAssets", Texts.Get("app.assets.none"));
        var (view, terrain) = MapScene.Build(_store!, session, assets, mode);
        Volatile.Write(ref _terrain, new TerrainCopy(mode, terrain));
        return view;
    }

    private object? UiScale(int percent)
    {
        if (Array.IndexOf(UiScales, percent) < 0)
        {
            throw BadRequest($"The scale {percent}% is not one of {string.Join(", ", UiScales)}.");
        }

        UiScaleChanged?.Invoke(percent);
        return null;
    }

    private object? FrameColors(string surface, string border)
    {
        if (!IsColor(surface) || !IsColor(border))
        {
            throw BadRequest("frameColors takes two colours as #rrggbb.");
        }

        FrameColorsChanged?.Invoke(surface, border);
        return null;
    }

    private static bool IsColor(string text) =>
        text.Length == 7 && text[0] == '#' && text.Skip(1).All(char.IsAsciiHexDigit);

    private static object? Raise(Action? handler)
    {
        handler?.Invoke();
        return null;
    }

    private Session Map() => _session ?? throw new CommandException("noMap", Texts.Get("app.error.noMap"));

    // Tells the page where the map stands now, and passes the command's result on.
    private T Changed<T>(T result)
    {
        var session = Map();
        SessionChanged?.Invoke(new SessionState(session.Dirty, session.ChangedObjects()));
        return result;
    }

    private static ObjectKind Kind(JsonElement args)
    {
        var key = Text(args, "kind");
        return Kinds.FromKey(key) ?? throw BadRequest($"There is no kind \"{key}\".");
    }

    private static string Text(JsonElement args, string name) =>
        OptionalText(args, name) ?? throw BadRequest($"The argument {name} is missing.");

    // A text argument, or null when it is missing or null.
    private static string? OptionalText(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw BadRequest($"The argument {name} must be text.");
    }

    // The items of a list argument.
    private static JsonElement.ArrayEnumerator ArrayOf(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray();
        }

        throw BadRequest($"The argument {name} must be a list.");
    }

    // A yes/no argument; false when it is missing or null.
    private static bool Flag(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw BadRequest($"The argument {name} must be true or false."),
        };
    }

    private static int Number(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        throw BadRequest($"The argument {name} must be a whole number.");
    }

    private static CommandException BadRequest(string message) => new("badRequest", message);

    // True once game data is loaded.
    private bool Loaded
    {
        get
        {
            lock (_lock)
            {
                return _data != null;
            }
        }
    }

    private GameDataState State()
    {
        lock (_lock)
        {
            return new GameDataState(_source, _data != null, _error, _version, _data?.GameVersion);
        }
    }

    private GameDataState ChooseGameData()
    {
        var folder = _askFolder(Texts.Get("app.choose.game"));
        if (!string.IsNullOrEmpty(folder))
        {
            Begin(folder, true);
        }

        return State();
    }

    // Loads on a background task. The folder is saved in the settings only once it has loaded, so a wrong choice does
    // not replace a working one. A newer load makes an older one that is still running count for nothing.
    private void Begin(string folder, bool save)
    {
        int mine;
        lock (_lock)
        {
            mine = ++_generation;
            _version++;
            _source = folder;
            _data = null;
            _error = null;
        }

        Loading = Task.Run(() =>
        {
            try
            {
                var data = _load(folder);
                lock (_lock)
                {
                    if (mine != _generation)
                    {
                        return;
                    }

                    _data = data;
                    _version++;
                }

                if (save)
                {
                    _settings.GameFolder = folder;
                    _settings.Save();
                }
            }
            catch (Exception exception)
            {
                lock (_lock)
                {
                    if (mine != _generation)
                    {
                        return;
                    }

                    _error = exception.Message;
                    _version++;
                }
            }

            GameDataChanged?.Invoke(State());
        });
    }
}

/// <summary>A map scene's terrain bytes and the graphics mode they were built in.</summary>
/// <param name="Mode">The graphics mode.</param>
/// <param name="Bytes">The bytes.</param>
internal sealed record TerrainCopy(string Mode, byte[] Bytes);
