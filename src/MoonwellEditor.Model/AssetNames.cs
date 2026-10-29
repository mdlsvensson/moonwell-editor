using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>
/// The names World Editor shows a game file by: an icon, a model or a texture field shows the names of every standard
/// object whose field of that sort holds the same file (the Paladin's icon as "Paladin, Admiral Proudmoore, ..."), each
/// tagged by the field it comes from ("Holy Light &lt;Research&gt;"), and the path only when no object holds it (World
/// Editor's screens, 2026-10-27).
/// </summary>
public static class AssetNames
{
    /// <summary>The sorts of file the names are kept for, by the metadata type of the field.</summary>
    public static string? GroupOf(string meta) => meta switch
    {
        "icon" => "icon",
        "model" or "modelList" => "model",
        "pathingTexture" => "pathing",
        "shadowTexture" => "shadow",
        "uberSplat" => "splat",
        "texture" => "texture",
        _ => null,
    };

    // The kinds whose objects name files, in the order World Editor lists them (its units before its destructables,
    // abilities before their buffs). Upgrades are left out: an upgrade's icon shows the items that share it, never the
    // upgrade (Iron Forged Swords' as Steel Sword).
    private static readonly ObjectKind[] Kinds =
    [
        ObjectKind.Unit, ObjectKind.Item, ObjectKind.Destructable, ObjectKind.Doodad, ObjectKind.Ability, ObjectKind.Buff,
    ];

    // The fields World Editor names objects by, with the tag it puts after the name ("" for none). Seen on its screens:
    // a unit's Icon - Score Screen, Caster Upgrade Art and Special; an ability's Icon - Normal and Research, Target and
    // Missile Art; a buff's Target; a destructable's and a doodad's Model File, Replaceable Texture File and Pathing
    // Texture (Base). The other tags are the agent's reading, by the same pattern. A unit's portrait and upgrade models,
    // a buff's icon and a destructable's dead pathing texture are not named (the Town Hall's portrait shows its path).
    private static readonly Dictionary<ObjectKind, (string Field, string Tag)[]> Tagged = new()
    {
        [ObjectKind.Unit] =
        [
            ("uico", ""), ("ussi", Texts.Get("app.assetTag.scoreScreen")), ("ucua", Texts.Get("app.assetTag.caster")), ("umdl", ""), ("ua1m", ""), ("ua2m", ""),
            ("uspa", Texts.Get("app.assetTag.special")), ("utaa", Texts.Get("app.assetTag.target")), ("upat", ""), ("ushb", ""), ("uubs", ""),
        ],
        [ObjectKind.Item] = [("iico", ""), ("ifil", "")],
        [ObjectKind.Destructable] = [("bfil", Texts.Get("app.assetTag.base")), ("btxf", Texts.Get("app.assetTag.base")), ("bptx", Texts.Get("app.assetTag.base")), ("bshd", "")],
        [ObjectKind.Doodad] = [("dfil", Texts.Get("app.assetTag.base")), ("dptx", Texts.Get("app.assetTag.base"))],
        [ObjectKind.Ability] =
        [
            ("aart", Texts.Get("app.assetTag.normal")), ("arar", Texts.Get("app.assetTag.research")), ("auar", Texts.Get("app.assetTag.turnOff")), ("acat", Texts.Get("app.assetTag.caster")), ("atat", Texts.Get("app.assetTag.target")),
            ("asat", Texts.Get("app.assetTag.special")), ("aeat", Texts.Get("app.assetTag.effect")), ("aaea", Texts.Get("app.assetTag.areaEffect")), ("amat", Texts.Get("app.assetTag.missile")),
        ],
        [ObjectKind.Buff] = [("ftat", Texts.Get("app.assetTag.target")), ("fsat", Texts.Get("app.assetTag.special")), ("feat", Texts.Get("app.assetTag.effect")), ("fmat", Texts.Get("app.assetTag.missile"))],
    };

    /// <summary>
    /// The names, by sort of file (<see cref="GroupOf"/>), then by the file's <see cref="Key"/>: each object's name and
    /// suffix with its field's tag, for every field that holds the file, the objects in the order of their kind's table
    /// (the Paladin before the campaign heroes). A unit's model is left out when the unit has other models by version
    /// (its Model File - Extra Versions is set: the Priest's and the Sorceress's models show their paths), and so are the
    /// abilities Reforged added (version 2): World Editor names Iron Forged Swords' icon by the Steel Sword alone, an
    /// item of version 2, not by the seventy such abilities that share it.
    /// </summary>
    public static Dictionary<string, Dictionary<string, List<string>>> Of(GameData.GameData data)
    {
        var result = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var kind in Kinds)
        {
            var kindData = data.Kind(kind);
            var fields = Tagged[kind]
                .Select(each => (Info: kindData.Field(each.Field), each.Tag))
                .Where(each => each.Info != null && GroupOf(each.Info.Meta) != null)
                .ToList();
            foreach (var standard in kindData.Objects.OrderBy(each => each.Row))
            {
                if (kind == ObjectKind.Ability && standard.Version == "2")
                {
                    continue;
                }

                // An object the tables have no name for goes by its id, as the tree shows it.
                var bare = standard.Name.Length > 0 ? standard.Name : standard.Id;
                var name = standard.Suffix.Length > 0 ? $"{bare} {standard.Suffix}" : bare;
                foreach (var (info, tag) in fields)
                {
                    var value = standard.Values.GetValueOrDefault(info!.PerLevel ? info.Id + ":1" : info.Id, "");
                    if (value.Trim().Length == 0
                        || (info.Id == "umdl" && standard.Values.GetValueOrDefault("uver", "0") is not ("0" or "")))
                    {
                        continue;
                    }

                    var group = GroupOf(info.Meta)!;
                    if (!result.TryGetValue(group, out var files))
                    {
                        result[group] = files = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    }

                    var key = Key(value);
                    if (!files.TryGetValue(key, out var names))
                    {
                        files[key] = names = [];
                    }

                    names.Add(tag.Length > 0 ? $"{name} <{tag}>" : name);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A file's key: its path in lower case with backslashes and without the extension of a model, image or texture
    /// (<c>units\human\peasant\peasant</c>), as World Editor matches <c>Peasant.mdl</c> with <c>Peasant</c>.
    /// </summary>
    public static string Key(string path)
    {
        var key = path.Trim().Replace('/', '\\').ToLowerInvariant();
        var dot = key.LastIndexOf('.');
        return dot > key.LastIndexOf('\\') && key[dot..] is ".mdl" or ".mdx" or ".blp" or ".dds" or ".tga"
            ? key[..dot]
            : key;
    }
}
