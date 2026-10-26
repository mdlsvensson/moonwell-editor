using System.Text.Json;
using Data = ObjectEditor.GameData.GameData;

namespace ObjectEditor.App;

/// <summary>Where the game data stands: which folder, whether it is loaded, and why it failed if it did.</summary>
/// <param name="Source">The game data folder being used, or null when none is set.</param>
/// <param name="Loaded">True once the data is ready for the commands that need it.</param>
/// <param name="Error">Why the load failed, or null.</param>
public sealed record GameDataState(string? Source, bool Loaded, string? Error);

/// <summary>
/// The commands the page can call, and the game data they work on. The window gives it what it cannot do itself (a
/// way to ask the user for a folder, a way to load the game data), so it can be tested without a window. Each task
/// adds its own commands here.
/// </summary>
public sealed class Commands
{
    private readonly Settings _settings;
    private readonly Func<string?> _askFolder;
    private readonly Func<string, Data> _load;
    private readonly object _lock = new();

    private string? _source;
    private Data? _data;
    private string? _error;
    private int _generation;

    /// <summary>Makes the commands.</summary>
    /// <param name="settings">Where the game data folder is remembered.</param>
    /// <param name="askFolder">Asks the user for a folder; gives null when they cancel. It may be called on any thread.</param>
    /// <param name="load">Loads the game data from a folder; the app uses <c>GameData.Load</c> with its cache folder.</param>
    public Commands(Settings settings, Func<string?> askFolder, Func<string, Data> load)
    {
        _settings = settings;
        _askFolder = askFolder;
        _load = load;
    }

    /// <summary>Raised, on a background thread, when a load ends (well or badly).</summary>
    public event Action<GameDataState>? GameDataChanged;

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
    public Task<object?> Run(string command, JsonElement args) =>
        command switch
        {
            "ping" => Task.FromResult<object?>("pong"),
            "gameData" => Task.FromResult<object?>(State()),
            "chooseGameData" => Task.FromResult<object?>(ChooseGameData()),
            _ => throw new UnknownCommandException(command),
        };

    private GameDataState State()
    {
        lock (_lock)
        {
            return new GameDataState(_source, _data != null, _error);
        }
    }

    private GameDataState ChooseGameData()
    {
        var folder = _askFolder();
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
                }
            }

            GameDataChanged?.Invoke(State());
        });
    }
}
