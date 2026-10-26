using System.Text.Json;
using ObjectEditor.Files;
using ObjectEditor.Model;
using Data = ObjectEditor.GameData.GameData;

namespace ObjectEditor.App;

/// <summary>Where the game data stands: which folder, whether it is loaded, and why it failed if it did.</summary>
/// <param name="Source">The game data folder being used, or null when none is set.</param>
/// <param name="Loaded">True once the data is ready for the commands that need it.</param>
/// <param name="Error">Why the load failed, or null.</param>
/// <param name="Version">
/// Grows by one each time the state changes, so the page can drop a state older than the one it has (a reply that
/// was on its way when a newer event came).
/// </param>
public sealed record GameDataState(string? Source, bool Loaded, string? Error, int Version);

/// <summary>Where the open map stands, sent with the <c>sessionChanged</c> event.</summary>
/// <param name="Dirty">True when there are edits that are not saved.</param>
/// <param name="ChangedObjects">
/// How many objects differ from the map as opened or last saved (see <see cref="Session.ChangedObjects"/>).
/// </param>
public sealed record SessionState(bool Dirty, int ChangedObjects);

/// <summary>What the app was started with, for the page to act on once it is ready.</summary>
/// <param name="Map">
/// The map folder given as the app's first command-line argument, which the page opens once the game data is ready;
/// null when there was none, or once the page has asked.
/// </param>
public sealed record Startup(string? Map);

/// <summary>The map that was opened or reloaded.</summary>
/// <param name="Name">The map folder's name.</param>
/// <param name="Path">The map folder's full path.</param>
/// <param name="Problems">What was wrong with its files (see <see cref="Session.Problems"/>).</param>
/// <param name="Counts">How many custom objects it has, by kind key (<c>unit</c>).</param>
public sealed record MapInfo(
    string Name,
    string Path,
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, int> Counts);

/// <summary>The open map and where it stands, for a page that was loaded again (the <c>currentMap</c> command).</summary>
/// <param name="Map">The open map, as <c>openMap</c> gave it.</param>
/// <param name="Session">Where it stands: whether it has unsaved edits.</param>
public sealed record CurrentMap(MapInfo Map, SessionState Session);

/// <summary>
/// The commands the page can call, the game data they work on and the open map. The window gives it what it cannot
/// do itself (a way to ask the user for a folder, a way to load the game data), so it can be tested without a window.
/// The window runs one command at a time; the map's commands rely on that.
/// </summary>
public sealed class Commands
{
    private readonly Settings _settings;
    private readonly Func<string, string?> _askFolder;
    private readonly Func<string, Data> _load;
    private readonly object _lock = new();

    // The map folder from the command line, until the page asks for it.
    private string? _startupMap;

    private string? _source;
    private Data? _data;
    private string? _error;
    private int _generation;
    private int _version;

    // The open map; null until one is opened.
    private MapFolder? _folder;
    private Session? _session;

    /// <summary>Makes the commands.</summary>
    /// <param name="settings">Where the game data folder is remembered.</param>
    /// <param name="askFolder">
    /// Asks the user for a folder, with the text given for the dialog; gives null when they cancel. It may be called on
    /// any thread.
    /// </param>
    /// <param name="load">Loads the game data from a folder; the app uses <c>GameData.Load</c> with its cache folder.</param>
    /// <param name="startupMap">
    /// The map folder the app was started with (its first command-line argument), which the <c>startup</c> command
    /// gives once; null for none.
    /// </param>
    public Commands(
        Settings settings,
        Func<string, string?> askFolder,
        Func<string, Data> load,
        string? startupMap = null)
    {
        _settings = settings;
        _askFolder = askFolder;
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

    /// <summary>True when a map is open with edits that are not saved.</summary>
    public bool Dirty => _session?.Dirty ?? false;

    /// <summary>The background load that is running or ran last. Awaiting it never throws.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Starts loading the game data from the folder in the settings, if there is one.</summary>
    /// <returns>The background load, which ends when the data is loaded or has failed.</returns>
    public Task StartLoading()
    {
        var folder = _settings.GameDataFolder;
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
                throw new CommandException("gameDataMissing", "No game data folder is chosen yet.");
            }

            throw _error == null
                ? new CommandException("gameDataLoading", "The game data is still loading.")
                : new CommandException("gameDataFailed", $"The game data did not load: {_error}");
        }
    }

    /// <summary>Runs one command by name.</summary>
    /// <param name="command">The command's name.</param>
    /// <param name="args">The command's arguments, as the page sent them.</param>
    /// <returns>The command's result.</returns>
    /// <exception cref="UnknownCommandException">There is no command of that name.</exception>
    /// <exception cref="CommandException">
    /// The command failed for a reason the page can act on: an <see cref="EditException"/>'s code,
    /// <c>changedOnDisk</c>, <c>noMap</c>, <c>unsaved</c> (<c>openMap</c> or <c>reload</c> would drop unsaved edits
    /// and the page did not say to discard them), <c>badRequest</c> for a missing or wrong argument, or a game data
    /// code (see <see cref="NeedGameData"/>).
    /// </exception>
    public Task<object?> Run(string command, JsonElement args)
    {
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
    }

    private object? RunNow(string command, JsonElement args) =>
        command switch
        {
            "ping" => "pong",
            "gameData" => State(),
            "startup" => TakeStartup(),
            "chooseGameData" => ChooseGameData(),
            "openMap" => OpenMap(OptionalText(args, "path"), Flag(args, "discard")),
            "currentMap" => Current(),
            "tree" => Map().Tree(Kind(args)),
            "object" => ObjectOf(Kind(args), Text(args, "id")),
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
            "choices" => NeedGameData().Choices,
            "closeAsking" => Raise(CloseAsking),
            "closeWindow" => Raise(CloseApproved),
            _ => throw new UnknownCommandException(command),
        };

    // The map from the command line the first time, so a page that is loaded again does not open it again.
    private Startup TakeStartup()
    {
        var map = _startupMap;
        _startupMap = null;
        return new Startup(map);
    }

    // Opens a map folder, asking for one when the page gives no path. Null when the user cancels the dialog. The game
    // data and the unsaved edits are checked first, so the user is not asked for a folder that cannot be opened yet.
    private MapInfo? OpenMap(string? path, bool discard)
    {
        var data = NeedGameData();
        RefuseUnsaved(discard, "open another map");
        path ??= _askFolder("Choose a map folder (a World Editor folder map, or a Moonwell project's map folder)");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var folder = MapFolder.Open(path);
        var session = Session.Open(folder, data);
        _folder = folder;
        _session = session;
        try
        {
            _settings.RememberMap(folder.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A settings file that cannot be written only means the map is not in the recent list next time.
        }

        return Changed(Info(folder, session));
    }

    private MapInfo Reload(bool discard)
    {
        var session = Map();
        RefuseUnsaved(discard, "reload the map");
        session.Reload();
        return Changed(Info(_folder!, session));
    }

    // Unsaved edits are dropped only when the page says so: its dialog asked the user, who chose Discard (or saved). A
    // page that was loaded again, and so does not know about them, cannot drop them by mistake.
    private void RefuseUnsaved(bool discard, string action)
    {
        if (!discard && Dirty)
        {
            throw new CommandException(
                "unsaved", $"The map has unsaved changes; save or discard them before you {action}.");
        }
    }

    // The open map for a page that was loaded again (after a reload of the page, or a crash of its process).
    private CurrentMap? Current()
    {
        var session = _session;
        return session == null
            ? null
            : new CurrentMap(Info(_folder!, session), new SessionState(session.Dirty, session.ChangedObjects()));
    }

    private static MapInfo Info(MapFolder folder, Session session)
    {
        var counts = new Dictionary<string, int>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            counts[ObjectKinds.Key(kind)] = session.Tree(kind).Count(entry => entry.BaseId != null);
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.Path));
        return new MapInfo(name, folder.Path, session.Problems, counts);
    }

    // An object of a read-only kind is shown too: its view says why it cannot be edited.
    private ObjectView ObjectOf(ObjectKind kind, string id) =>
        Map().Object(kind, id)
        ?? throw new CommandException("noObject", $"There is no {ObjectKinds.Key(kind)} {id}.");

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

    private object? DeleteObject(JsonElement args)
    {
        Map().Delete(Kind(args), Text(args, "id"));
        return Changed<object?>(null);
    }

    private static object? Raise(Action? handler)
    {
        handler?.Invoke();
        return null;
    }

    private Session Map() => _session ?? throw new CommandException("noMap", "No map is open.");

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
        return ObjectKinds.FromKey(key) ?? throw BadRequest($"There is no kind \"{key}\".");
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

    private GameDataState State()
    {
        lock (_lock)
        {
            return new GameDataState(_source, _data != null, _error, _version);
        }
    }

    private GameDataState ChooseGameData()
    {
        var folder = _askFolder("Choose the game data folder (a war3.w3mod export)");
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
                    _settings.GameDataFolder = folder;
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
