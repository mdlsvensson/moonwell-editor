using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>
/// Where World Editor's Object Editor files a standard object in a kind's tree, and the order its folders come in
/// (Sven, 2026-10-26).
/// </summary>
internal static class Folders
{
    /// <summary>The folder the map's custom objects are in, before their own folders.</summary>
    public static string Custom => F("custom");

    // A folder's name, from the strings file (Sven, 2026-10-29).
    private static string F(string key) => Texts.Get("app.folder." + key);

    // The four playable races, as World Editor names them and in its order.
    private static readonly Dictionary<string, string> Races = new(StringComparer.Ordinal)
    {
        ["human"] = F("human"),
        ["orc"] = F("orc"),
        ["undead"] = F("undead"),
        ["nightelf"] = F("nightElf"),
    };

    private static readonly string[] RaceOrder = [F("human"), F("orc"), F("undead"), F("nightElf")];

    // A unit of no playable race: the naga's folder, then by hostilePal (World Editor's screens, 2026-10-27).
    private static readonly string NagaFolder = F("naga");
    private static readonly string HostileFolder = F("hostile");
    private static readonly string PassiveFolder = F("passive");

    // The abilities and units World Editor does not show get a folder of their own, after its folders (Sven,
    // 2026-10-27).
    private static readonly string HiddenFolder = F("hidden");

    private static readonly Dictionary<string, string> ItemClasses = new(StringComparer.Ordinal)
    {
        ["Permanent"] = F("permanent"),
        ["Charged"] = F("charged"),
        ["PowerUp"] = F("powerUp"),
        ["Artifact"] = F("artifact"),
        ["Purchasable"] = F("purchasable"),
        ["Campaign"] = F("campaign"),
        ["Miscellaneous"] = F("miscellaneous"),
        ["Equipment"] = F("equipment"),
    };

    private static readonly Dictionary<string, string> DestructableCategories = new(StringComparer.Ordinal)
    {
        ["D"] = F("trees"),
        ["P"] = F("pathingBlockers"),
        ["B"] = F("bridges"),
    };

    private static readonly Dictionary<string, string> DoodadCategories = new(StringComparer.Ordinal)
    {
        ["O"] = F("props"),
        ["S"] = F("structures"),
        ["W"] = F("water"),
        ["C"] = F("cliffs"),
        ["E"] = F("environment"),
        ["Z"] = F("cinematic"),
    };

    /// <summary>
    /// The folders of each level, in order, per kind; a folder not listed comes after the listed ones, in the data's
    /// order.
    /// </summary>
    private static readonly Dictionary<ObjectKind, string[][]> Order = new()
    {
        [ObjectKind.Unit] =
        [
            [.. RaceOrder, NagaFolder, HostileFolder, PassiveFolder, HiddenFolder],
            [F("melee"), F("campaign")],
            [F("units"), F("buildings"), F("heroes"), F("special")],
        ],
        [ObjectKind.Item] = [[.. ItemClasses.Values]],
        [ObjectKind.Destructable] = [[.. DestructableCategories.Values]],
        [ObjectKind.Doodad] = [[.. DoodadCategories.Values]],
        [ObjectKind.Ability] =
        [
            [.. RaceOrder, F("hostile"), F("passive"), F("special"), HiddenFolder],
            [F("units"), F("heroes"), F("items")],
        ],
        [ObjectKind.Buff] = [[.. RaceOrder, F("special")], [F("buff"), F("effect")]],
        [ObjectKind.Upgrade] = [[.. RaceOrder, F("special")]],
    };

    /// <summary>The folders a standard object sits in, broadest first; see <see cref="TreeEntry.Path"/>.</summary>
    public static IReadOnlyList<string> Of(ObjectKind kind, StandardObject standard) => kind switch
    {
        ObjectKind.Unit when standard.Hidden => [HiddenFolder],
        ObjectKind.Unit => [UnitRace(standard), standard.Campaign ? F("campaign") : F("melee"), UnitType(standard)],
        ObjectKind.Item => [ItemClasses.GetValueOrDefault(standard.Category, Other(standard.Category))],
        ObjectKind.Destructable =>
            [DestructableCategories.GetValueOrDefault(standard.Category, Other(standard.Category))],
        ObjectKind.Doodad => [DoodadCategories.GetValueOrDefault(standard.Category, Other(standard.Category))],
        ObjectKind.Ability when standard.Hidden => [HiddenFolder],
        ObjectKind.Ability => [AbilityRace(standard.Race), AbilityUse(standard.Category)],
        ObjectKind.Buff => [RaceOrSpecial(standard.Race), standard.Category == "1" ? F("effect") : F("buff")],
        ObjectKind.Upgrade => [RaceOrSpecial(standard.Race)],
        _ => [Other(standard.Race)],
    };

    /// <summary>
    /// A custom object as World Editor files it under Custom, in the same folders as the standard objects (Sven,
    /// 2026-10-28): its base with the custom object's id, name and suffix, and its own values for what picks a folder
    /// where the map sets them (a unit's race, campaign, special, building and Display as Neutral Hostile, and a hero by
    /// its id's capital letter; an item's class; a destructable's or a doodad's category; an ability's race, and Hero
    /// Ability or Item Ability; a buff's race and Is an Effect; an upgrade's race). It is never hidden. A custom object
    /// whose base the game data lacks is filed as an object of no race. <paramref name="changed"/> gives the map's value
    /// of a field by its id, null when the map does not set it. *(Agent's calls: which fields count, not checked in
    /// World Editor field by field.)*
    /// </summary>
    public static StandardObject AsFiled(
        ObjectKind kind,
        string id,
        string name,
        string suffix,
        StandardObject? standard,
        Func<string, string?> changed)
    {
        var filed = (standard ?? new StandardObject(
            id, name, suffix, "", false, false, false, false, "", false, null,
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>())) with
        {
            Id = id,
            Name = name,
            Suffix = suffix,
            Hidden = false,
        };

        bool Flag(string field, bool otherwise) => changed(field) is { } value ? value.Trim() == "1" : otherwise;
        string Text(string field, string otherwise) => changed(field) is { Length: > 0 } value ? value : otherwise;

        return kind switch
        {
            ObjectKind.Unit => filed with
            {
                Race = Text("urac", filed.Race),
                Campaign = Flag("ucam", filed.Campaign),
                Special = Flag("uspe", filed.Special),
                Building = Flag("ubdg", filed.Building),
                Hostile = Flag("uhos", filed.Hostile),
                Hero = id.Length > 0 && char.IsUpper(id[0]),
            },
            ObjectKind.Item => filed with { Category = Text("icla", filed.Category) },
            ObjectKind.Destructable => filed with { Category = Text("bcat", filed.Category) },
            ObjectKind.Doodad => filed with { Category = Text("dcat", filed.Category) },
            ObjectKind.Ability when changed("aher") == null && changed("aite") == null =>
                filed with { Race = Text("arac", filed.Race) },
            ObjectKind.Ability => filed with
            {
                Race = Text("arac", filed.Race),
                Category = Flag("aher", filed.Category == "hero") ? "hero"
                    : Flag("aite", filed.Category == "item") ? "item"
                    : "unit",
            },
            ObjectKind.Buff => filed with
            {
                Race = Text("frac", filed.Race),
                Category = Flag("feff", filed.Category == "1") ? "1" : "0",
            },
            ObjectKind.Upgrade => filed with { Race = Text("grac", filed.Race) },
            _ => filed,
        };
    }

    /// <summary>
    /// The standard objects in tree order: by their folders' places in <see cref="Order"/>, level by level, and in
    /// the data's order among folders it does not list. Within a folder, units go by their <c>unitClass</c>
    /// (<see cref="StandardObject.Category"/>: <c>HUnit01</c>, <c>HUnit02</c>...), as World Editor lists them, those
    /// without one after, and units of one class (or none) by name and suffix (Sven, 2026-10-27); any other object by
    /// its <see cref="StandardObject.Sort"/>, then by name and suffix, compared as written (World Editor lists Call To
    /// Arms (Town Hall) before Call to Arms (Peasant)).
    /// </summary>
    public static List<StandardObject> Sorted(ObjectKind kind, IEnumerable<StandardObject> standards)
    {
        var levels = Order[kind];
        var ranked = standards
            .Select((standard, index) => (Standard: standard, Index: index, Path: Of(kind, standard)))
            .ToList();

        // A folder the order does not list keeps the place of the first object in it.
        var firstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var each in ranked)
        {
            for (var depth = 0; depth < each.Path.Count; depth++)
            {
                firstSeen.TryAdd(Key(each.Path, depth), each.Index);
            }
        }

        int Rank(IReadOnlyList<string> path, int depth)
        {
            var order = depth < levels.Length ? levels[depth] : [];
            var listed = Array.IndexOf(order, path[depth]);
            return listed >= 0 ? listed : order.Length + firstSeen[Key(path, depth)];
        }

        ranked.Sort((a, b) =>
        {
            for (var depth = 0; depth < Math.Min(a.Path.Count, b.Path.Count); depth++)
            {
                var order = Rank(a.Path, depth).CompareTo(Rank(b.Path, depth));
                if (order != 0)
                {
                    return order;
                }
            }

            if (kind != ObjectKind.Unit && a.Path.SequenceEqual(b.Path))
            {
                var byName = string.CompareOrdinal(a.Standard.Sort, b.Standard.Sort);
                if (byName == 0)
                {
                    byName = string.CompareOrdinal(a.Standard.Name, b.Standard.Name);
                }

                if (byName == 0)
                {
                    byName = string.CompareOrdinal(a.Standard.Suffix, b.Standard.Suffix);
                }

                if (byName != 0)
                {
                    return byName;
                }
            }

            if (kind == ObjectKind.Unit && a.Path.SequenceEqual(b.Path))
            {
                var byClass = (a.Standard.Category.Length == 0).CompareTo(b.Standard.Category.Length == 0);
                if (byClass == 0)
                {
                    byClass = string.CompareOrdinal(a.Standard.Category, b.Standard.Category);
                }

                if (byClass == 0)
                {
                    byClass = StringComparer.OrdinalIgnoreCase.Compare(a.Standard.Name, b.Standard.Name);
                }

                if (byClass == 0)
                {
                    byClass = StringComparer.OrdinalIgnoreCase.Compare(a.Standard.Suffix, b.Standard.Suffix);
                }

                if (byClass != 0)
                {
                    return byClass;
                }
            }

            return a.Index.CompareTo(b.Index);
        });
        return ranked.Select(each => each.Standard).ToList();
    }

    private static string Key(IReadOnlyList<string> path, int depth) => string.Join("/", path.Take(depth + 1));

    private static string UnitRace(StandardObject standard) =>
        Races.TryGetValue(standard.Race, out var name) ? name
        : standard.Race == "naga" ? NagaFolder
        : standard.Hostile ? HostileFolder
        : PassiveFolder;

    private static string UnitType(StandardObject standard) =>
        standard.Special ? F("special")
        : standard.Hero ? F("heroes")
        : standard.Building ? F("buildings")
        : F("units");

    // As World Editor files them (Sven, 2026-10-27): the creeps' abilities are Neutral Hostile, the naga's Neutral
    // Passive, and the rest (race "other", which World Editor names Special) Special.
    private static string AbilityRace(string race) =>
        Races.TryGetValue(race, out var name) ? name
        : race switch
        {
            "creeps" => F("hostile"),
            "naga" => F("passive"),
            _ => F("special"),
        };

    private static string AbilityUse(string sort) => sort switch
    {
        "hero" => F("heroes"),
        "item" => F("items"),
        _ => F("units"),
    };

    private static string RaceOrSpecial(string race) => Races.GetValueOrDefault(race, F("special"));

    // A value the lists do not know, shown with a capital letter; none as Other.
    private static string Other(string value) =>
        value.Length == 0 ? F("unlisted") : char.ToUpperInvariant(value[0]) + value[1..];
}
