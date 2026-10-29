using Moonwell.GameData;
using MoonwellEditor.Files;
using MoonwellEditor.Model;

namespace MoonwellEditor.Map.Tests;

/// <summary>
/// The game data of the install on this computer, loaded once per run (its cache under the temp folder is kept between
/// runs, so only the first run of a build of the game builds it); null without an install.
/// </summary>
public static class TestGameData
{
    private static readonly Lazy<GameData.GameData?> Loaded = new(() => GameInstall.Find() is { } folder
        ? GameData.GameData.Load(folder, Path.Combine(Path.GetTempPath(), "MoonwellEditor.Map.Tests", "gamedata"))
        : null);

    /// <summary>The game data, or null without an install.</summary>
    public static GameData.GameData? Data => Loaded.Value;

    /// <summary>A session over <paramref name="store"/>, or null without an install.</summary>
    public static Session? Open(IMapStore store) => Data is { } data ? Session.Open(store, data) : null;
}
