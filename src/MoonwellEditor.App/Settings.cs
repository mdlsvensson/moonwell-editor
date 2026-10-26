using System.Text.Json;

namespace MoonwellEditor.App;

/// <summary>
/// What the app remembers between runs: the game data folder and the maps opened lately. It is kept as JSON in
/// <c>settings.json</c> in one folder (the app uses <c>%LOCALAPPDATA%\MoonwellEditor</c>). A missing or damaged file
/// gives the defaults, so a bad settings file never stops the app from starting.
/// </summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    /// <summary>Reads the settings from <c>settings.json</c> in <paramref name="folder"/>.</summary>
    /// <param name="folder">The folder that holds (or will hold) the file.</param>
    public Settings(string folder)
    {
        _path = Path.Combine(folder, "settings.json");
        var stored = Read(_path);
        GameDataFolder = stored?.GameDataFolder;
        RecentMaps = stored?.RecentMaps ?? [];
    }

    /// <summary>The game data folder (a <c>war3.w3mod</c> export), or null before the user has chosen one.</summary>
    public string? GameDataFolder { get; set; }

    /// <summary>How many maps <see cref="RememberMap"/> keeps.</summary>
    public const int RecentMapsKept = 10;

    /// <summary>The maps opened lately, newest first (see <see cref="RememberMap"/>).</summary>
    public List<string> RecentMaps { get; }

    /// <summary>
    /// Puts a map (a folder or an archive file) first in <see cref="RecentMaps"/> and saves the settings. The same map
    /// spelled with other letter case is taken out first, and only the newest <see cref="RecentMapsKept"/> are kept.
    /// </summary>
    /// <param name="map">The map folder's or archive file's path.</param>
    public void RememberMap(string map)
    {
        RecentMaps.RemoveAll(known => string.Equals(known, map, StringComparison.OrdinalIgnoreCase));
        RecentMaps.Insert(0, map);
        if (RecentMaps.Count > RecentMapsKept)
        {
            RecentMaps.RemoveRange(RecentMapsKept, RecentMaps.Count - RecentMapsKept);
        }

        Save();
    }

    /// <summary>Writes the settings, creating the folder if it is not there yet.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var stored = new Stored { GameDataFolder = GameDataFolder, RecentMaps = RecentMaps };

        // Written to a side file first, so a crash half way never leaves a broken settings file.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, Json));
        File.Move(temporary, _path, true);
    }

    private static Stored? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class Stored
    {
        public string? GameDataFolder { get; set; }

        public List<string>? RecentMaps { get; set; }
    }
}
