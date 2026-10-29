using System.Globalization;
using System.Text.Json.Serialization;
using MoonwellEditor.Files;
using MoonwellEditor.Model;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Widget;
using War3Net.Common.Extensions;

namespace MoonwellEditor.Map;

/// <summary>What kind of thing is placed; it goes to the page by its name (<c>StartLocation</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThingKind>))]
public enum ThingKind
{
    /// <summary>A unit, from <c>war3mapUnits.doo</c>.</summary>
    Unit,

    /// <summary>An item, from <c>war3mapUnits.doo</c>.</summary>
    Item,

    /// <summary>A player's start location (<c>sloc</c>), from <c>war3mapUnits.doo</c>.</summary>
    StartLocation,

    /// <summary>A destructable, from <c>war3map.doo</c>.</summary>
    Destructable,

    /// <summary>A doodad, from <c>war3map.doo</c>.</summary>
    Doodad,
}

/// <summary>A thing placed on the map, as the view draws and picks it.</summary>
/// <param name="Number">Its number, from 1, in the order of the list: the doodad file's, then the unit file's.</param>
/// <param name="Kind">What it is.</param>
/// <param name="TypeId">Its type's id, <c>hfoo</c>.</param>
/// <param name="Name">Its type's name; its id when the map and game have no such type.</param>
/// <param name="Ref">The name triggers use for it, <c>gg_unit_hfoo_0001</c>; null for a doodad and a start location.</param>
/// <param name="X">Where it stands.</param>
/// <param name="Y">Where it stands.</param>
/// <param name="Z">Where it stands: a unit on the ground plus its fly height, a doodad or destructable at its file's z.</param>
/// <param name="Facing">Its facing in radians, as the files hold it, counter-clockwise from east.</param>
/// <param name="ScaleX">Its scale along x.</param>
/// <param name="ScaleY">Its scale along y.</param>
/// <param name="ScaleZ">Its scale along z.</param>
/// <param name="Model">Its model's game path, as object data writes it; null for a marker (no such type, no model).</param>
/// <param name="Texture">The texture its model's replaceable texture takes (a tree's), or null.</param>
/// <param name="Player">The player whose colour its team colour takes, 0 for player 1; -1 for none.</param>
/// <param name="Tint">Its tint, 0xRRGGBB.</param>
/// <param name="Variation">Its variation, from its file: which of its model's shapes it is.</param>
/// <param name="Shadow">A unit's shadow image in Classic; null for none (a building's shadow is baked into the map).</param>
public sealed record PlacedThing(
    int Number,
    ThingKind Kind,
    string TypeId,
    string Name,
    string? Ref,
    float X,
    float Y,
    float Z,
    float Facing,
    float ScaleX,
    float ScaleY,
    float ScaleZ,
    string? Model,
    string? Texture,
    int Player,
    int Tint,
    int Variation = 0,
    UnitShadow? Shadow = null);

/// <summary>
/// A unit's shadow image, the picture Classic lays flat on the ground under it (<c>ushu</c>, <c>ushx</c>, <c>ushy</c>,
/// <c>ushw</c>, <c>ushh</c>): the unit stands <paramref name="X"/>, <paramref name="Y"/> from the picture's bottom left
/// (*claim*), which is <paramref name="Width"/> by <paramref name="Height"/>.
/// </summary>
/// <param name="Texture">The picture's path, <c>ReplaceableTextures\Shadows\Shadow.blp</c>.</param>
/// <param name="X">How far right of the picture's left edge the unit stands.</param>
/// <param name="Y">How far up from the picture's bottom edge the unit stands.</param>
/// <param name="Width">The picture's width.</param>
/// <param name="Height">The picture's height.</param>
public sealed record UnitShadow(string Texture, float X, float Y, float Width, float Height);

/// <summary>A region, as the view draws and picks it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Ref">The name triggers use for it, <c>gg_rct_Spawn</c>.</param>
/// <param name="Left">Its left edge.</param>
/// <param name="Bottom">Its bottom edge.</param>
/// <param name="Right">Its right edge.</param>
/// <param name="Top">Its top edge.</param>
/// <param name="Color">Its colour, 0xRRGGBB.</param>
public sealed record RegionMark(string Name, string Ref, float Left, float Bottom, float Right, float Top, int Color);

/// <summary>A camera, as the view draws and picks it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Ref">The name triggers use for it, <c>gg_cam_Intro</c>.</param>
/// <param name="X">The x it looks at.</param>
/// <param name="Y">The y it looks at.</param>
/// <param name="Z">Its height offset.</param>
/// <param name="Rotation">Its rotation in degrees.</param>
/// <param name="AngleOfAttack">Its angle of attack in degrees.</param>
/// <param name="Distance">Its distance from what it looks at.</param>
/// <param name="FieldOfView">Its field of view in degrees.</param>
public sealed record CameraMark(
    string Name,
    string Ref,
    float X,
    float Y,
    float Z,
    float Rotation,
    float AngleOfAttack,
    float Distance,
    float FieldOfView);

/// <summary>
/// The things placed on the map, its regions and its cameras, with what the view needs to draw them: each type's model,
/// scale and tint from the session's object data (the map's own objects and unsaved changes included), and the name
/// triggers use.
/// </summary>
public static class Placed
{
    private const string StartLocation = "sloc";

    /// <summary>The doodads, destructables, units, items and start locations of the map, in a graphics mode.</summary>
    public static IReadOnlyList<PlacedThing> Things(MapFiles files, TerrainGrid? grid, Session session, string mode)
    {
        var things = new List<PlacedThing>();
        var types = new Dictionary<(ObjectKind, string), ObjectView?>();
        ObjectView? View(ObjectKind kind, string id)
        {
            if (!types.TryGetValue((kind, id), out var view))
            {
                view = types[(kind, id)] = session.Object(kind, id);
            }

            return view;
        }

        foreach (var doodad in files.Doodads?.Doodads ?? [])
        {
            var id = doodad.TypeId.ToRawcode();
            var skin = doodad.SkinId == 0 ? id : doodad.SkinId.ToRawcode();
            var destructable = View(ObjectKind.Destructable, id);
            var kind = destructable != null || View(ObjectKind.Doodad, id) == null
                ? ThingKind.Destructable
                : ThingKind.Doodad;
            var objectKind = kind == ThingKind.Destructable ? ObjectKind.Destructable : ObjectKind.Doodad;
            var view = View(objectKind, id);
            var look = View(objectKind, skin) ?? view;
            var (x, y, facing) = (doodad.Position.X, doodad.Position.Y, doodad.Rotation);
            var prefix = kind == ThingKind.Destructable ? "b" : "d";
            var fixedRotation = look == null ? -1f : Number(look, prefix + "fxr", mode, 0, -1f);
            if (fixedRotation >= 0)
            {
                // The game ignores a fixed-rotation type's facing; a bridge stands where CreateDestructable moves
                // one (docs/game-facts.md, 10-27; seen on YT16, other types *claim*).
                facing = fixedRotation * MathF.PI / 180f;
                if (kind == ThingKind.Destructable)
                {
                    x = (64f * MathF.Truncate(x / 64f)) + 32f;
                    y = 64f * MathF.Truncate(y / 64f);
                }
            }

            var tint = kind == ThingKind.Destructable
                ? Tint(look, "bvcr", "bvcg", "bvcb", mode, 0)
                : Tint(look, "dvr1", "dvg1", "dvb1", mode, doodad.Variation + 1);
            var texture = kind == ThingKind.Destructable && look != null ? Text(look, "btxf", mode, 0) : null;
            things.Add(new PlacedThing(
                things.Count + 1,
                kind,
                id,
                view?.Name ?? id,
                kind == ThingKind.Destructable ? TriggerNames.Destructable(id, doodad.CreationNumber) : null,
                x,
                y,
                doodad.Position.Z,
                facing,
                doodad.Scale.X,
                doodad.Scale.Y,
                doodad.Scale.Z,
                look == null ? null : Model(look, prefix + "fil", prefix + "var", doodad.Variation, mode),
                string.IsNullOrEmpty(texture) ? null : texture,
                -1,
                tint,
                doodad.Variation));
        }

        foreach (var unit in files.Units?.Units ?? [])
        {
            var id = unit.TypeId.ToRawcode();
            var skin = unit.SkinId == 0 ? id : unit.SkinId.ToRawcode();
            var player = unit.CustomPlayerColorId >= 0 ? unit.CustomPlayerColorId : unit.OwnerId;
            var ground = grid?.GroundAt(unit.Position.X, unit.Position.Y) ?? unit.Position.Z;
            if (id == StartLocation)
            {
                things.Add(new PlacedThing(
                    things.Count + 1, ThingKind.StartLocation, id,
                    Texts.Get("app.mapView.startLocation", ("player", unit.OwnerId + 1)), null,
                    unit.Position.X, unit.Position.Y, ground, unit.Rotation, 1, 1, 1, null, null, unit.OwnerId, 0xFFFFFF,
                    unit.Variation));
                continue;
            }

            var item = unit.IsItem();
            var objectKind = item ? ObjectKind.Item : ObjectKind.Unit;
            var view = View(objectKind, id);
            var look = View(objectKind, skin) ?? view;
            var prefix = item ? "i" : "u";
            var scale = look == null ? 1f : Number(look, item ? "isca" : "usca", mode, 0, 1f);
            var height = look == null || item ? 0f : Number(look, "umvh", mode, 0, 0f);
            things.Add(new PlacedThing(
                things.Count + 1,
                item ? ThingKind.Item : ThingKind.Unit,
                id,
                view?.Name ?? id,
                item ? TriggerNames.Item(id, unit.CreationNumber) : TriggerNames.Unit(id, unit.CreationNumber),
                unit.Position.X,
                unit.Position.Y,
                ground + height,
                unit.Rotation,
                unit.Scale.X * scale,
                unit.Scale.Y * scale,
                unit.Scale.Z * scale,
                look == null ? null : Model(look, item ? "ifil" : "umdl", null, 0, mode),
                null,
                item ? -1 : player,
                Tint(look, prefix + "clr", prefix + "clg", prefix + "clb", mode, 0),
                unit.Variation,
                item || look == null ? null : ShadowOf(look, mode)));
        }

        return things;
    }

    // A unit type's shadow image; null for none ("_" or nothing, as a building has) or one of no size.
    private static UnitShadow? ShadowOf(ObjectView look, string mode)
    {
        var image = Text(look, "ushu", mode, 0);
        if (string.IsNullOrWhiteSpace(image) || image == "_")
        {
            return null;
        }

        var shadow = new UnitShadow(
            @"ReplaceableTextures\Shadows\" + image + ".blp",
            Number(look, "ushx", mode, 0, 0f),
            Number(look, "ushy", mode, 0, 0f),
            Number(look, "ushw", mode, 0, 0f),
            Number(look, "ushh", mode, 0, 0f));
        return shadow.Width > 0 && shadow.Height > 0 ? shadow : null;
    }

    /// <summary>The map's regions.</summary>
    public static IReadOnlyList<RegionMark> Regions(MapRegions? regions) =>
        (regions?.Regions ?? []).Select(region => new RegionMark(
            region.Name,
            TriggerNames.Region(region.Name),
            region.Left,
            region.Bottom,
            region.Right,
            region.Top,
            region.Color.ToArgb() & 0xFFFFFF)).ToList();

    /// <summary>The map's cameras.</summary>
    public static IReadOnlyList<CameraMark> Cameras(MapCameras? cameras) =>
        (cameras?.Cameras ?? []).Select(camera => new CameraMark(
            camera.Name,
            TriggerNames.Camera(camera.Name),
            camera.TargetPosition.X,
            camera.TargetPosition.Y,
            camera.ZOffset,
            camera.Rotation,
            camera.AngleOfAttack,
            camera.TargetDistance,
            camera.FieldOfView)).ToList();

    // The model a type draws in the mode: with more than one variation, the variation's number before the extension
    // (*claim*, checked against the install's files on five maps). Null when the type has no model.
    private static string? Model(ObjectView view, string field, string? variations, int variation, string mode)
    {
        var path = Text(view, field, mode, 0);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (extension is not (".mdl" or ".mdx" or ".MDL" or ".MDX"))
        {
            extension = ".mdl";
        }
        else
        {
            path = path[..^extension.Length];
        }

        var count = variations == null ? 1 : (int)Number(view, variations, mode, 0, 1f);
        return count > 1 ? path + variation.ToString(CultureInfo.InvariantCulture) + extension : path + extension;
    }

    // A field's value in the mode: the map's own when it changes the field, else the mode's standard value.
    private static string? Text(ObjectView view, string field, string mode, int level)
    {
        var found = view.Fields.FirstOrDefault(each => each.Id == field && (each.Level == level || level == 0))
            ?? view.Fields.FirstOrDefault(each => each.Id == field);
        if (found == null)
        {
            return null;
        }

        return !found.Changed && found.Modes != null && found.Modes.TryGetValue(mode, out var standard)
            ? standard
            : found.Value;
    }

    private static float Number(ObjectView view, string field, string mode, int level, float fallback) =>
        float.TryParse(Text(view, field, mode, level), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    // Three colour fields as 0xRRGGBB; white for a missing type or field.
    private static int Tint(ObjectView? view, string red, string green, string blue, string mode, int level)
    {
        if (view == null)
        {
            return 0xFFFFFF;
        }

        int Part(string field) => Math.Clamp((int)Number(view, field, mode, level, 255f), 0, 255);
        return (Part(red) << 16) | (Part(green) << 8) | Part(blue);
    }
}
