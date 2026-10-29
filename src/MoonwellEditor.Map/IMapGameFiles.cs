using Moonwell.GameData;

namespace MoonwellEditor.Map;

/// <summary>
/// The game's files the map view reads: textures and models (the map's own imports first), the files in a folder,
/// and the game's tables (<c>TerrainArt\Terrain.slk</c> and the like, which a map cannot change).
/// </summary>
public interface IMapGameFiles
{
    /// <summary>A file's bytes in a graphics mode (<c>sd</c>, <c>hd</c>, <c>de</c>); null when there is none.</summary>
    byte[]? File(string path, string mode);

    /// <summary>The game's files in <paramref name="folder"/> with <paramref name="extension"/>, in a graphics mode.</summary>
    IReadOnlyList<string> List(string folder, string mode, string extension);

    /// <summary>The rows of a table by its path (<c>TerrainArt/CliffTypes.slk</c>); null when there is none.</summary>
    IReadOnlyList<MetaRow>? Table(string relative);
}

/// <summary>Four-letter ids as the game's files hold them: a 32-bit number, the first letter in its lowest byte.</summary>
public static class FourCC
{
    /// <summary>The id <paramref name="value"/> spells (<c>CLdi</c> for War3Net's <c>CliffType.L_Dirt</c>).</summary>
    public static string Text(int value) => string.Create(4, value, static (span, number) =>
    {
        for (var i = 0; i < 4; i++)
        {
            span[i] = (char)((number >> (8 * i)) & 0xFF);
        }
    });
}
