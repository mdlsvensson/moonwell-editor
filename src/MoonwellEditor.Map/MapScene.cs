using System.Globalization;
using MoonwellEditor.Files;
using MoonwellEditor.Model;
using War3Net.Build.Info;

namespace MoonwellEditor.Map;

/// <summary>How the tileset's water looks, from <c>TerrainArt\Water.slk</c>, tinted by the map when it says so.</summary>
/// <param name="Texture">The water texture's path without its frame number, <c>ReplaceableTextures\Water\Water</c>.</param>
/// <param name="Frames">How many frames the texture has (<c>Water00</c> to <c>Water44</c>).</param>
/// <param name="ShallowMin">The shallowest water's colour, 0xAARRGGBB.</param>
/// <param name="ShallowMax">The colour where shallow water ends, 0xAARRGGBB.</param>
/// <param name="DeepMin">The colour where deep water begins, 0xAARRGGBB.</param>
/// <param name="DeepMax">The deepest water's colour, 0xAARRGGBB.</param>
public sealed record WaterLook(string Texture, int Frames, int ShallowMin, int ShallowMax, int DeepMin, int DeepMax);

/// <summary>Everything the map view draws but the terrain's corners, which go as bytes (<see cref="TerrainBytes"/>).</summary>
/// <param name="Columns">Corners a row; 0 when the map has no terrain.</param>
/// <param name="Rows">Rows of corners.</param>
/// <param name="Left">The x of the first column.</param>
/// <param name="Bottom">The y of the first row.</param>
/// <param name="Tileset">The tileset's letter, <c>L</c>.</param>
/// <param name="Textures">The ground textures, in the order the tiles' layers count them.</param>
/// <param name="CliffTextures">Each cliff type's texture, in the order the cliff pieces count them.</param>
/// <param name="Cliffs">The cliff and ramp models.</param>
/// <param name="Water">How the water looks.</param>
/// <param name="Things">The placed things.</param>
/// <param name="Regions">The regions.</param>
/// <param name="Cameras">The cameras.</param>
/// <param name="Problems">What could not be read.</param>
public sealed record MapSceneView(
    int Columns,
    int Rows,
    float Left,
    float Bottom,
    string Tileset,
    IReadOnlyList<TerrainTexture> Textures,
    IReadOnlyList<string> CliffTextures,
    IReadOnlyList<CliffPiece> Cliffs,
    WaterLook Water,
    IReadOnlyList<PlacedThing> Things,
    IReadOnlyList<RegionMark> Regions,
    IReadOnlyList<CameraMark> Cameras,
    IReadOnlyList<string> Problems);

/// <summary>The map view's scene, built from the map's files and the session's object data when the view asks.</summary>
public static class MapScene
{
    /// <summary>The scene of the map in <paramref name="store"/>, in a graphics mode, and its terrain's bytes.</summary>
    public static (MapSceneView View, byte[] Terrain) Build(
        IMapStore store, Session session, IMapGameFiles files, string mode)
    {
        var read = MapReader.Read(store);
        var problems = read.Problems.ToList();
        var regions = Placed.Regions(read.Regions);
        var cameras = Placed.Cameras(read.Cameras);
        var terrain = read.Terrain;
        if (terrain == null)
        {
            if (!problems.Any(problem => problem.Contains("war3map.w3e", StringComparison.Ordinal)))
            {
                problems.Add(Texts.Get("app.mapView.noTerrain"));
            }

            var things = Placed.Things(read, null, session, mode);
            return (new MapSceneView(0, 0, 0, 0, "", [], [], [], new WaterLook("", 0, 0, 0, 0, 0), things, regions,
                cameras, problems), []);
        }

        var tileset = ((char)terrain.Tileset).ToString();
        var water = (files.Table("TerrainArt/Water.slk") ?? [])
            .FirstOrDefault(row => row.Id == tileset + "Sha")?.Columns ?? new Dictionary<string, string>();
        var level = Number(water, "height", -0.7f) * 128f;
        var grid = TerrainGrid.From(terrain, level);
        var (pieces, tiles) = Cliffs.Of(terrain, files, mode);
        var textures = TerrainTextures.Read(terrain, files, mode);
        var layers = TileLayers.From(terrain, textures, tiles);
        var view = new MapSceneView(
            grid.Columns,
            grid.Rows,
            grid.Left,
            grid.Bottom,
            tileset,
            textures.All,
            Cliffs.Textures(terrain, files, mode),
            pieces,
            Look(water, read.Info),
            Placed.Things(read, grid, session, mode),
            regions,
            cameras,
            problems);
        return (view, TerrainBytes.Write(grid, layers, tiles));
    }

    // The water's texture, frames and four colours; tinted by the map's water colour when its flag says so.
    private static WaterLook Look(IReadOnlyDictionary<string, string> water, MapInfo? info)
    {
        var tint = info != null && info.MapFlags.HasFlag(MapFlags.HasWaterTintingColor) ? info.WaterTintingColor : default;
        int Colour(string prefix)
        {
            int Part(string channel, int tinted) => Math.Clamp(
                (int)(Number(water, prefix + channel, 255f) * tinted / 255f), 0, 255);
            var tinting = info != null && info.MapFlags.HasFlag(MapFlags.HasWaterTintingColor);
            return (Part("_A", tinting ? tint.A : 255) << 24) | (Part("_R", tinting ? tint.R : 255) << 16)
                | (Part("_G", tinting ? tint.G : 255) << 8) | Part("_B", tinting ? tint.B : 255);
        }

        return new WaterLook(
            water.GetValueOrDefault("texFile", ""),
            (int)Number(water, "numTex", 1f),
            Colour("Smin"),
            Colour("Smax"),
            Colour("Dmin"),
            Colour("Dmax"));
    }

    private static float Number(IReadOnlyDictionary<string, string> row, string column, float fallback) =>
        float.TryParse(row.GetValueOrDefault(column), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
