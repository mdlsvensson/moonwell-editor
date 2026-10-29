using Moonwell.GameData;
using MoonwellEditor.Files;

namespace MoonwellEditor.Map.Tests;

/// <summary>
/// Blizzard's maps in the Warcraft III install on this computer (MOONWELL_WAR3, else the usual folder), read from its
/// storage and written once per run to a temp file, so a test opens them as any map archive. Null without an install.
/// </summary>
public static class InstallMaps
{
    /// <summary>Lost Temple: 161 by 161 corners, the map the game facts measured.</summary>
    public const string LostTemple = "Maps/FrozenThrone/(4)LostTemple.w3x";

    /// <summary>Stromguarde: bridges over water.</summary>
    public const string Stromguarde = "Maps/(6)Stromguarde.w3m";

    /// <summary>Ekrezem's Maze: many destructables.</summary>
    public const string EkrezemsMaze = "Maps/FrozenThrone/Community/2023Season2/(8)EkrezemsMaze_S2_v1.1.w3x";

    /// <summary>WarChasers: a JASS script that names units, destructables, regions and cameras.</summary>
    public const string WarChasers = "Maps/Scenario/(4)WarChasers.w3m";

    /// <summary>Forgotten Hollow: a Lua script that names units, destructables, items, regions and cameras.</summary>
    public const string ForgottenHollow = "Maps/ForsakenKingdom/Scenario/(1)ForgottenHollow.w3x";

    /// <summary>What a test prints when it checks nothing for want of an install.</summary>
    public const string NoInstall = "No Warcraft III install found; set MOONWELL_WAR3 to run this test";

    private static readonly object Lock = new();
    private static readonly Lazy<GameFiles?> Files = new(
        () => GameInstall.Find() is { } folder ? GameFiles.Open(folder) : null);

    /// <summary>The install's files, or null without one. Read them under <see cref="Reading"/>.</summary>
    public static GameFiles? Game => Files.Value;

    /// <summary>The lock the tests take to read <see cref="Game"/>, which one reader at a time uses.</summary>
    public static object Reading => Lock;

    /// <summary>The map at <paramref name="path"/> in the install's storage, opened; null without an install.</summary>
    public static IMapStore? Open(string path)
    {
        var files = Files.Value;
        if (files == null)
        {
            return null;
        }

        var target = Path.Combine(Path.GetTempPath(), "MoonwellEditor.Map.Tests", "maps", Path.GetFileName(path));
        lock (Lock)
        {
            if (!File.Exists(target))
            {
                var bytes = files.Read(path, "sd") ?? throw new FileNotFoundException("Not in the install: " + path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target + ".part", bytes);
                File.Move(target + ".part", target, true);
            }
        }

        return MapStore.Open(target);
    }
}

/// <summary>An empty temporary folder, deleted when disposed.</summary>
public sealed class TempFolder : IDisposable
{
    /// <summary>Makes the folder.</summary>
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MoonwellEditor.Map.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>Writes a file into the folder.</summary>
    public void Write(string name, byte[] bytes) => File.WriteAllBytes(System.IO.Path.Combine(Path, name), bytes);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}
