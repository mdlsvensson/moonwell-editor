using System.Text.RegularExpressions;
using War3Net.Build.Environment;

namespace MoonwellEditor.Map;

/// <summary>A cliff or ramp model on a tile.</summary>
/// <param name="Column">The tile's column (its bottom-left corner's).</param>
/// <param name="Row">The tile's row.</param>
/// <param name="Model">The model's game path, <c>Doodads\Terrain\Cliffs\CliffsBAAA0.mdx</c>.</param>
/// <param name="CliffType">The cliff type's number in the map's list, which picks its texture.</param>
/// <param name="BaseZ">The height of the tile's lowest cliff level: <c>(level − 2) × 128</c>.</param>
public sealed record CliffPiece(int Column, int Row, string Model, int CliffType, float BaseZ);

/// <summary>Which tiles a cliff or ramp model covers, and which corners take a cliff type's ground texture.</summary>
public sealed class CliffTiles
{
    private readonly int _columns;
    private readonly int _rows;
    private readonly bool[] _cliff;
    private readonly bool[] _ramp;
    private readonly bool[] _modelled;
    private readonly bool[] _covered;

    internal CliffTiles(int columns, int rows, bool[] cliff, bool[] ramp, bool[] modelled, bool[] covered)
    {
        _columns = columns;
        _rows = rows;
        _cliff = cliff;
        _ramp = ramp;
        _modelled = modelled;
        _covered = covered;
    }

    /// <summary>
    /// Whether the corner takes its cliff type's ground texture: one of the four tiles around it holds a ramp's side
    /// model, or a cliff and the corner is not ramp-flagged (*claim*, from HiveWE).
    /// </summary>
    public bool Touches(int column, int row)
    {
        var modelled = false;
        var cliff = false;
        for (var y = row - 1; y <= row; y++)
        {
            for (var x = column - 1; x <= column; x++)
            {
                if (x < 0 || y < 0 || x >= _columns - 1 || y >= _rows - 1)
                {
                    continue;
                }

                modelled |= _modelled[(y * _columns) + x];
                cliff |= _cliff[(y * _columns) + x];
            }
        }

        return modelled || (cliff && !_ramp[(row * _columns) + column]);
    }

    /// <summary>Whether a cliff or ramp model takes the ground's place on the tile, so the ground is not drawn there.</summary>
    public bool Covered(int column, int row) => _covered[(row * _columns) + column];
}

/// <summary>
/// The cliff and ramp models of a terrain (*claim*, read from HiveWE and checked against the install's files): a tile
/// whose corners' cliff levels differ gets <c>Cliffs</c> + a letter a corner (<c>A</c> + its level above the lowest; top
/// left, top right, bottom right, bottom left) + its variation; a ramp's side, two tiles long, gets <c>CliffTrans</c> +
/// a letter a corner (a ramp-flagged corner <c>L</c> at the ramp's foot, <c>H</c> one up; any other <c>A</c> + its
/// level above the foot) + <c>0</c>. The folders and textures are the cliff type's, from <c>TerrainArt\CliffTypes.slk</c>.
/// </summary>
public static class Cliffs
{
    private const string Table = "TerrainArt/CliffTypes.slk";

    /// <summary>The cliff and ramp models of <paramref name="terrain"/>, and the tiles they cover.</summary>
    public static (IReadOnlyList<CliffPiece> Pieces, CliffTiles Tiles) Of(
        MapEnvironment terrain, IMapGameFiles files, string mode)
    {
        var columns = (int)terrain.Width + 1;
        var rows = (int)terrain.Height + 1;
        var corners = terrain.TerrainTiles;
        var types = TypesOf(terrain, files);
        var variations = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        int Level(int x, int y) => corners[(y * columns) + x].CliffLevel;
        bool Ramp(int x, int y) => corners[(y * columns) + x].IsRamp;

        var cliff = new bool[columns * rows];
        var ramp = new bool[columns * rows];
        for (var i = 0; i < corners.Count; i++)
        {
            ramp[i] = corners[i].IsRamp;
        }

        for (var y = 0; y < rows - 1; y++)
        {
            for (var x = 0; x < columns - 1; x++)
            {
                var level = Level(x, y);
                cliff[(y * columns) + x] = level != Level(x + 1, y) || level != Level(x, y + 1)
                    || level != Level(x + 1, y + 1);
            }
        }

        bool Entrance(int x, int y) => Ramp(x, y) && Ramp(x + 1, y) && Ramp(x, y + 1) && Ramp(x + 1, y + 1)
            && !(Level(x, y) == Level(x + 1, y + 1) && Level(x, y + 1) == Level(x + 1, y));

        // A ramp corner's letter counts down from L by 4 a level; any other corner's counts up from A.
        char Letter(int x, int y, int foot) => Ramp(x, y)
            ? (char)('L' - (4 * (Level(x, y) - foot)))
            : (char)('A' + (Level(x, y) - foot));

        var modelled = new bool[columns * rows];
        var pieces = new List<CliffPiece>();
        for (var x = 0; x < columns - 1; x++)
        {
            for (var y = 0; y < rows - 1; y++)
            {
                var type = TypeAt(corners[(y * columns) + x], types.Count);
                var (cliffFolder, rampFolder) = types.Count > 0 ? types[type] : ("Cliffs", "CliffTrans");

                // A ramp's side along y: the corner columns x and x + 1 over rows y to y + 2, one ramp-flagged.
                if (y < rows - 2)
                {
                    var left = Math.Min(Level(x, y), Level(x, y + 2));
                    var right = Math.Min(Level(x + 1, y), Level(x + 1, y + 2));
                    if (Level(x, y + 1) == left && Level(x + 1, y + 1) == right
                        && Ramp(x, y) == Ramp(x, y + 1) && Ramp(x, y) == Ramp(x, y + 2)
                        && Ramp(x + 1, y) == Ramp(x + 1, y + 1) && Ramp(x + 1, y) == Ramp(x + 1, y + 2)
                        && Ramp(x, y) != Ramp(x + 1, y))
                    {
                        var foot = Math.Min(left, right);
                        var name = string.Concat(
                            Letter(x, y + 2, foot), Letter(x + 1, y + 2, foot), Letter(x + 1, y, foot), Letter(x, y, foot));
                        if (Variations(files, mode, rampFolder, variations).ContainsKey(name))
                        {
                            pieces.Add(new CliffPiece(x, y, Path(rampFolder, name, 0), type, (foot - 2) * 128f));
                            modelled[(y * columns) + x] = true;
                            modelled[((y + 1) * columns) + x] = true;
                            continue;
                        }
                    }
                }

                // A ramp's side along x: the corner rows y and y + 1 over columns x to x + 2, one ramp-flagged.
                if (x < columns - 2)
                {
                    var bottom = Math.Min(Level(x, y), Level(x + 2, y));
                    var top = Math.Min(Level(x, y + 1), Level(x + 2, y + 1));
                    if (Level(x + 1, y) == bottom && Level(x + 1, y + 1) == top
                        && Ramp(x, y) == Ramp(x + 1, y) && Ramp(x, y) == Ramp(x + 2, y)
                        && Ramp(x, y + 1) == Ramp(x + 1, y + 1) && Ramp(x, y + 1) == Ramp(x + 2, y + 1)
                        && Ramp(x, y) != Ramp(x, y + 1))
                    {
                        var foot = Math.Min(bottom, top);
                        var name = string.Concat(
                            Letter(x, y + 1, foot), Letter(x + 2, y + 1, foot), Letter(x + 2, y, foot), Letter(x, y, foot));
                        if (Variations(files, mode, rampFolder, variations).ContainsKey(name))
                        {
                            pieces.Add(new CliffPiece(x, y, Path(rampFolder, name, 0), type, (foot - 2) * 128f));
                            modelled[(y * columns) + x] = true;
                            modelled[(y * columns) + x + 1] = true;
                            continue;
                        }
                    }
                }

                if (!cliff[(y * columns) + x] || modelled[(y * columns) + x] || Entrance(x, y))
                {
                    continue;
                }

                var lowest = Math.Min(Math.Min(Level(x, y), Level(x + 1, y)), Math.Min(Level(x, y + 1), Level(x + 1, y + 1)));
                var letters = string.Concat(
                    (char)('A' + Level(x, y + 1) - lowest),
                    (char)('A' + Level(x + 1, y + 1) - lowest),
                    (char)('A' + Level(x + 1, y) - lowest),
                    (char)('A' + Level(x, y) - lowest));
                var highest = Variations(files, mode, cliffFolder, variations).GetValueOrDefault(letters);
                var variation = Math.Clamp(CliffVariation(corners[(y * columns) + x]), 0, highest);
                pieces.Add(new CliffPiece(x, y, Path(cliffFolder, letters, variation), type, (lowest - 2) * 128f));
            }
        }

        var covered = new bool[columns * rows];
        for (var y = 0; y < rows - 1; y++)
        {
            for (var x = 0; x < columns - 1; x++)
            {
                var index = (y * columns) + x;
                covered[index] = (cliff[index] || modelled[index]) && !Entrance(x, y);
            }
        }

        return (pieces, new CliffTiles(columns, rows, cliff, ramp, modelled, covered));
    }

    /// <summary>Each cliff type's texture, in the map's order: its <c>texDir</c> and <c>texFile</c> (*claim*).</summary>
    public static IReadOnlyList<string> Textures(MapEnvironment terrain, IMapGameFiles files, string mode)
    {
        var rows = (files.Table(Table) ?? []).ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        return terrain.CliffTypes.Select(type => rows.TryGetValue(FourCC.Text((int)type), out var row)
                ? row.Columns.GetValueOrDefault("texDir", "") + "\\" + row.Columns.GetValueOrDefault("texFile", "") + ".blp"
                : "")
            .ToList();
    }

    /// <summary>
    /// The corner's cliff variation: the top three bits of its variation byte (*claim*, HiveWE's split; War3Net splits
    /// the byte four and four, and gives the whole byte back as <c>(CliffVariation &lt;&lt; 4) | Variation</c>).
    /// </summary>
    internal static int CliffVariation(TerrainTile corner) => Raw(corner) >> 5;

    /// <summary>The corner's whole variation byte.</summary>
    internal static int Raw(TerrainTile corner) => (corner.CliffVariation << 4) | corner.Variation;

    // The corner's cliff type: its number in the map's list; 15 counts as 1 (*claim*, HiveWE).
    internal static int TypeAt(TerrainTile corner, int count)
    {
        var type = corner.CliffTexture == 15 ? 1 : corner.CliffTexture;
        return Math.Clamp(type, 0, Math.Max(count - 1, 0));
    }

    // Each of the map's cliff types' model folders, Cliffs and CliffTrans or CityCliffs and CityCliffTrans.
    private static List<(string Cliff, string Ramp)> TypesOf(MapEnvironment terrain, IMapGameFiles files)
    {
        var rows = (files.Table(Table) ?? []).ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        return terrain.CliffTypes.Select(type => rows.TryGetValue(FourCC.Text((int)type), out var row)
                ? (row.Columns.GetValueOrDefault("cliffModelDir", "Cliffs"), row.Columns.GetValueOrDefault("rampModelDir", "CliffTrans"))
                : ("Cliffs", "CliffTrans"))
            .ToList();
    }

    private static string Path(string folder, string letters, int variation) =>
        $@"Doodads\Terrain\{folder}\{folder}{letters}{variation}.mdx";

    // The models a folder has, by their letters, each with its highest variation number.
    private static Dictionary<string, int> Variations(
        IMapGameFiles files, string mode, string folder, Dictionary<string, Dictionary<string, int>> known)
    {
        if (known.TryGetValue(folder, out var found))
        {
            return found;
        }

        var name = new Regex("^" + Regex.Escape(folder) + "([A-Z]{4})([0-9]+)$", RegexOptions.IgnoreCase);
        found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in files.List($"Doodads/Terrain/{folder}/", mode, ".mdx"))
        {
            var match = name.Match(System.IO.Path.GetFileNameWithoutExtension(path));
            if (match.Success)
            {
                var letters = match.Groups[1].Value.ToUpperInvariant();
                found[letters] = Math.Max(found.GetValueOrDefault(letters), int.Parse(match.Groups[2].Value));
            }
        }

        return known[folder] = found;
    }
}
