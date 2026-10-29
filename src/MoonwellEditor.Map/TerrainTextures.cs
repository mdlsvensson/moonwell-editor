using System.Text;
using War3Net.Build.Environment;

namespace MoonwellEditor.Map;

/// <summary>A ground texture: a sheet of 4 by 4 pieces, 8 by 4 when it has variations.</summary>
/// <param name="TileId">Its id in <c>TerrainArt\Terrain.slk</c>, <c>Ldrt</c>; <c>blight</c> for the blight.</param>
/// <param name="Path">Its game path, <c>TerrainArt\LordaeronSummer\Lords_Dirt.blp</c>.</param>
/// <param name="Extended">Whether the sheet is twice as wide as it is high: 8 by 4 pieces, the right half variations.</param>
public sealed record TerrainTexture(string TileId, string Path, bool Extended);

/// <summary>
/// A map's ground textures in its own order (a corner's texture number counts in it), then the tileset's blight
/// texture; and each cliff type's ground tile, the texture a corner by a cliff takes.
/// </summary>
/// <param name="all">The textures, the blight last.</param>
/// <param name="blight">The blight's number in <paramref name="all"/>.</param>
/// <param name="cliffGround">Each cliff type's ground texture's number in <paramref name="all"/>, in the map's order.</param>
public sealed class TerrainTextures(IReadOnlyList<TerrainTexture> all, int blight, IReadOnlyList<int> cliffGround)
{
    /// <summary>The textures, the blight last.</summary>
    public IReadOnlyList<TerrainTexture> All { get; } = all;

    /// <summary>The blight texture's number.</summary>
    public int Blight { get; } = blight;

    /// <summary>Each cliff type's ground texture's number, in the map's order of cliff types.</summary>
    public IReadOnlyList<int> CliffGround { get; } = cliffGround;

    /// <summary>
    /// The textures of <paramref name="terrain"/> in a graphics mode: each from <c>TerrainArt\Terrain.slk</c>
    /// (<c>dir</c> and <c>file</c>), the blight from <c>UI\WorldEditData.txt</c>'s <c>[TileSets]</c> (the tileset
    /// letter's second value, *claim*, HiveWE), a cliff type's ground tile from <c>TerrainArt\CliffTypes.slk</c>.
    /// </summary>
    public static TerrainTextures Read(MapEnvironment terrain, IMapGameFiles files, string mode)
    {
        var tiles = (files.Table("TerrainArt/Terrain.slk") ?? []).ToDictionary(row => row.Id, StringComparer.Ordinal);
        var all = new List<TerrainTexture>();
        foreach (var type in terrain.TerrainTypes)
        {
            var id = FourCC.Text((int)type);
            var path = tiles.TryGetValue(id, out var row)
                ? HdOrClassic(files, row.Columns.GetValueOrDefault("dir", "") + "\\" + row.Columns.GetValueOrDefault("file", ""), mode)
                : "";
            all.Add(new TerrainTexture(id, path, path != "" && IsExtended(files.File(path, mode))));
        }

        var blight = BlightOf(terrain, files, mode);
        blight = blight == "" ? "" : HdOrClassic(files, blight[..^".blp".Length], mode);
        all.Add(new TerrainTexture("blight", blight, blight != "" && IsExtended(files.File(blight, mode))));

        var cliffs = (files.Table("TerrainArt/CliffTypes.slk") ?? []).ToDictionary(row => row.Id, StringComparer.Ordinal);
        var ground = terrain.CliffTypes.Select(type =>
        {
            var tile = cliffs.TryGetValue(FourCC.Text((int)type), out var row)
                ? row.Columns.GetValueOrDefault("groundTile", "")
                : "";
            var at = all.FindIndex(texture => texture.TileId == tile);
            return at < 0 ? 0 : at;
        }).ToList();

        return new TerrainTextures(all, all.Count - 1, ground);
    }

    /// <summary>
    /// A ground texture's file: outside Classic its HD version, the <c>_diffuse</c> file (2048 by 1024 where Classic's
    /// is 512 by 256), when the install has one, as HiveWE takes it (*claim*); else the Classic one.
    /// </summary>
    private static string HdOrClassic(IMapGameFiles files, string stem, string mode) =>
        mode != "sd" && files.File(stem + "_diffuse.blp", mode) != null ? stem + "_diffuse.blp" : stem + ".blp";

    /// <summary>The number of the texture with this id; -1 when the map has none.</summary>
    public int IndexOf(string tileId) => All.ToList().FindIndex(texture => texture.TileId == tileId);

    // A texture twice as wide as high, from its DDS or BLP header; false when it cannot be read.
    private static bool IsExtended(byte[]? bytes)
    {
        if (bytes is not { Length: >= 20 })
        {
            return false;
        }

        var magic = Encoding.ASCII.GetString(bytes, 0, 4);
        var (width, height) = magic switch
        {
            "DDS " => (BitConverter.ToInt32(bytes, 16), BitConverter.ToInt32(bytes, 12)),
            "BLP1" => (BitConverter.ToInt32(bytes, 12), BitConverter.ToInt32(bytes, 16)),
            "BLP2" => (BitConverter.ToInt32(bytes, 8), BitConverter.ToInt32(bytes, 12)),
            _ => (0, 0),
        };
        return height > 0 && width == height * 2;
    }

    // The tileset's blight texture, from World Editor's list of tilesets.
    private static string BlightOf(MapEnvironment terrain, IMapGameFiles files, string mode)
    {
        var bytes = files.File("UI/WorldEditData.txt", mode);
        if (bytes == null)
        {
            return "";
        }

        var letter = ((char)terrain.Tileset).ToString();
        var inSection = false;
        foreach (var raw in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line == "[TileSets]";
                continue;
            }

            if (inSection && line.StartsWith(letter + "=", StringComparison.Ordinal))
            {
                var values = line[(letter.Length + 1)..].Split(',');
                return values.Length > 1 ? values[1].Trim() + ".blp" : "";
            }
        }

        return "";
    }
}
