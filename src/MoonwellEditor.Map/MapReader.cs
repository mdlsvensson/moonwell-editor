using MoonwellEditor.Files;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Widget;

namespace MoonwellEditor.Map;

/// <summary>
/// The map's files the map view draws from, as War3Net reads them. A file the map does not have is null and means
/// none of that kind; a file War3Net cannot read is null too, and its reason is in <paramref name="Problems"/>.
/// </summary>
/// <param name="Terrain">The terrain, <c>war3map.w3e</c>.</param>
/// <param name="Doodads">The doodads and destructables, <c>war3map.doo</c>.</param>
/// <param name="Units">The units, items and start locations, <c>war3mapUnits.doo</c>.</param>
/// <param name="Regions">The regions, <c>war3map.w3r</c>.</param>
/// <param name="Cameras">The cameras, <c>war3map.w3c</c>.</param>
/// <param name="Info">The map's info, <c>war3map.w3i</c>: its bounds, players and water tint.</param>
/// <param name="Shadows">
/// The shadows World Editor bakes for Classic, <c>war3map.shd</c>, as they are: a byte a quarter tile, 255 in shadow.
/// </param>
/// <param name="Problems">The files that could not be read, each with its reason.</param>
public sealed record MapFiles(
    MapEnvironment? Terrain,
    MapDoodads? Doodads,
    MapUnits? Units,
    MapRegions? Regions,
    MapCameras? Cameras,
    MapInfo? Info,
    byte[]? Shadows,
    IReadOnlyList<string> Problems);

/// <summary>Reads the map's files for the map view. It only reads: nothing here writes to the map.</summary>
public static class MapReader
{
    /// <summary>Reads the terrain, doodad, unit, region, camera, info and shadow files of <paramref name="store"/>.</summary>
    public static MapFiles Read(IMapStore store)
    {
        var problems = new List<string>();
        return new MapFiles(
            Read(store, MapEnvironment.FileName, problems, reader => reader.ReadMapEnvironment()),
            Read(store, MapDoodads.FileName, problems, reader => reader.ReadMapDoodads()),
            Read(store, MapUnits.FileName, problems, reader => reader.ReadMapUnits()),
            Read(store, MapRegions.FileName, problems, reader => reader.ReadMapRegions()),
            Read(store, MapCameras.FileName, problems, reader => reader.ReadMapCameras()),
            Read(store, MapInfo.FileName, problems, reader => reader.ReadMapInfo()),
            Read(store, "war3map.shd", problems, reader => reader.ReadBytes((int)reader.BaseStream.Length)),
            problems);
    }

    // One file: null when the map has none, or when it cannot be read, out of the archive (a protected map's bogus
    // entry) or by War3Net (its reason added to problems).
    private static T? Read<T>(IMapStore store, string name, List<string> problems, Func<BinaryReader, T> read)
        where T : class
    {
        try
        {
            var bytes = store.Read(name);
            if (bytes == null)
            {
                return null;
            }

            using var reader = new BinaryReader(new MemoryStream(bytes));
            return read(reader);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            problems.Add(Texts.Get("app.mapView.unreadable", ("name", name), ("reason", exception.Message)));
            return null;
        }
    }
}
