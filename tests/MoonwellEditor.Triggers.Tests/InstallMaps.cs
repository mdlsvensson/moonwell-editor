using Moonwell.GameData;
using MoonwellEditor.Files;

namespace MoonwellEditor.Triggers.Tests;

/// <summary>
/// Blizzard's maps in the Warcraft III install on this computer (MOONWELL_WAR3, else the usual folder), read from its
/// storage and written once per run to a temp file, and the catalog of its trigger functions. Null without an install.
/// </summary>
public static class InstallMaps
{
    /// <summary>Lost Temple: one Melee Initialization trigger.</summary>
    public const string LostTemple = "Maps/FrozenThrone/(4)LostTemple.w3x";

    /// <summary>WarChasers: a JASS map with many GUI triggers and variables.</summary>
    public const string WarChasers = "Maps/Scenario/(4)WarChasers.w3m";

    /// <summary>Forgotten Hollow: a Lua map, its triggers nearly all custom text.</summary>
    public const string ForgottenHollow = "Maps/ForsakenKingdom/Scenario/(1)ForgottenHollow.w3x";

    /// <summary>What a test prints when it checks nothing for want of an install.</summary>
    public const string NoInstall = "No Warcraft III install found; set MOONWELL_WAR3 to run this test";

    private static readonly object Lock = new();
    private static readonly Lazy<GameFiles?> Files = new(
        () => GameInstall.Find() is { } folder ? GameFiles.Open(folder) : null);

    private static readonly Lazy<TriggerCatalog?> Shared = new(
        () => Files.Value == null ? null : TriggerCatalog.Read(ReadGame));

    /// <summary>The catalog of the install's trigger functions, or null without an install.</summary>
    public static TriggerCatalog? Catalog => Shared.Value;

    /// <summary>A file of the game, in Classic.</summary>
    public static byte[]? ReadGame(string path)
    {
        lock (Lock)
        {
            return Files.Value?.Read(path, "sd");
        }
    }

    /// <summary>Every map of the install, as paths in its storage; empty without one.</summary>
    public static IReadOnlyList<string> All()
    {
        lock (Lock)
        {
            return Files.Value?.List("Maps/", "sd", ".w3x", ".w3m") ?? [];
        }
    }

    /// <summary>The map at <paramref name="path"/> in the install's storage, opened; null without an install.</summary>
    public static IMapStore? Open(string path)
    {
        var files = Files.Value;
        if (files == null)
        {
            return null;
        }

        var target = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MoonwellEditor.Triggers.Tests",
            "maps",
            path.Replace('/', '_').Replace('\\', '_'));
        lock (Lock)
        {
            if (!File.Exists(target))
            {
                var bytes = files.Read(path, "sd") ?? throw new FileNotFoundException("Not in the install: " + path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target + ".part", bytes);
                File.Move(target + ".part", target, true);
            }
        }

        return MapStore.Open(target);
    }
}
