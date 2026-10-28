using Moonwell.GameData;
using War3Net.Build.Extensions;
using War3Net.Build.Info;

namespace MoonwellEditor.Files;

/// <summary>
/// The map's game data option, World Editor's Data Set and Data Version, and the tables the game reads under it (see
/// docs/game-facts.md in wc3-cs, "Object data", seen 10-18).
/// </summary>
/// <param name="DataSet">World Editor's name for the data set: Default, Custom or Melee.</param>
/// <param name="DataVersion">World Editor's name for the data version: Reign of Chaos, Frozen Throne or Forsaken Kingdom.</param>
/// <param name="Values">The tables the game takes the standard values from: <c>base</c>, <c>custom_v0</c>, <c>custom_v1</c> or <c>melee_v0</c>.</param>
/// <param name="Objects">The tables that decide which standard objects exist, named the same way.</param>
public sealed record MapOptions(string DataSet, string DataVersion, string Values, string Objects)
{
    private const string InfoFile = "war3map.w3i";

    /// <summary>The map's option from its <c>war3map.w3i</c>; null when the map has none or it cannot be read.</summary>
    public static MapOptions? Read(IMapStore store) =>
        Info(store) is { } info ? Of(info.GameDataSet, info.GameDataVersion) : null;

    /// <summary>
    /// The map's name as its info file writes it (a <c>TRIGSTR_n</c> for most maps) and whether it is an expansion
    /// (Frozen Throne) map: an archive by its extension (<c>.w3x</c>, <c>.w3m</c>), a folder by its info file's format
    /// (Reign of Chaos maps have a format below 25). Null when the info file is missing or cannot be read.
    /// </summary>
    public static (string Name, bool Expansion)? Header(IMapStore store)
    {
        if (Info(store) is not { } info)
        {
            return null;
        }

        var expansion = store.IsArchive
            ? !string.Equals(System.IO.Path.GetExtension(store.Path), ".w3m", StringComparison.OrdinalIgnoreCase)
            : (int)info.FormatVersion >= 25;
        return (info.MapName ?? "", expansion);
    }

    private static MapInfo? Info(IMapStore store)
    {
        var bytes = store.Read(InfoFile);
        if (bytes == null)
        {
            return null;
        }

        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            return reader.ReadMapInfo();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException
                                              or ArgumentException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>
    /// The option for a data set and version, with the tables the game reads under it as Moonwell's reader gives them
    /// (<see cref="DataOptions.TablesOf"/>).
    /// </summary>
    public static MapOptions Of(GameDataSet set, GameDataVersion version)
    {
        var (setOption, setName) = set switch
        {
            GameDataSet.Custom => (DataSetOption.Custom, "Custom"),
            GameDataSet.Melee => (DataSetOption.Melee, "Melee"),
            _ => (DataSetOption.Default, "Default"),
        };
        var (versionOption, versionName) = version switch
        {
            GameDataVersion.RoC => (DataVersionOption.ReignOfChaos, "Reign of Chaos"),
            GameDataVersion.TFT => (DataVersionOption.FrozenThrone, "Frozen Throne"),
            _ => (DataVersionOption.ForsakenKingdom, "Forsaken Kingdom"),
        };

        var tables = DataOptions.TablesOf(setOption, versionOption);
        return new MapOptions(setName, versionName, tables.Values, tables.Objects);
    }
}
