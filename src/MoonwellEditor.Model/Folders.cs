using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>
/// Where World Editor's Object Editor files a standard object in a kind's tree, and the order its folders come in
/// (Sven, 2026-10-26).
/// </summary>
internal static class Folders
{
    // The four playable races, as World Editor names them and in its order.
    private static readonly Dictionary<string, string> Races = new(StringComparer.Ordinal)
    {
        ["human"] = "Human",
        ["orc"] = "Orc",
        ["undead"] = "Undead",
        ["nightelf"] = "Night Elf",
    };

    private static readonly string[] RaceOrder = ["Human", "Orc", "Undead", "Night Elf"];

    // The abilities World Editor does not show get a folder of their own, after its folders (Sven, 2026-10-27).
    private const string HiddenFolder = "Hidden in World Editor";

    private static readonly Dictionary<string, string> ItemClasses = new(StringComparer.Ordinal)
    {
        ["Permanent"] = "Permanent",
        ["Charged"] = "Charged",
        ["PowerUp"] = "Power Up",
        ["Artifact"] = "Artifact",
        ["Purchasable"] = "Purchasable",
        ["Campaign"] = "Campaign",
        ["Miscellaneous"] = "Miscellaneous",
        ["Equipment"] = "Equipment",
    };

    private static readonly Dictionary<string, string> DestructableCategories = new(StringComparer.Ordinal)
    {
        ["D"] = "Trees/Destructables",
        ["P"] = "Pathing Blockers",
        ["B"] = "Bridges/Ramps",
    };

    private static readonly Dictionary<string, string> DoodadCategories = new(StringComparer.Ordinal)
    {
        ["O"] = "Props",
        ["S"] = "Structures",
        ["W"] = "Water",
        ["C"] = "Cliff/Terrain",
        ["E"] = "Environment",
        ["Z"] = "Cinematic",
    };

    /// <summary>
    /// The folders of each level, in order, per kind; a folder not listed comes after the listed ones, in the data's
    /// order.
    /// </summary>
    private static readonly Dictionary<ObjectKind, string[][]> Order = new()
    {
        [ObjectKind.Unit] = [RaceOrder, ["Melee", "Campaign"], ["Units", "Buildings", "Heroes", "Special"]],
        [ObjectKind.Item] = [[.. ItemClasses.Values]],
        [ObjectKind.Destructable] = [[.. DestructableCategories.Values]],
        [ObjectKind.Doodad] = [[.. DoodadCategories.Values]],
        [ObjectKind.Ability] =
        [
            [.. RaceOrder, "Neutral Hostile", "Neutral Passive", "Special", HiddenFolder],
            ["Units", "Heroes", "Items"],
        ],
        [ObjectKind.Buff] = [[.. RaceOrder, "Special"], ["Buffs", "Effects"]],
        [ObjectKind.Upgrade] = [[.. RaceOrder, "Special"]],
    };

    /// <summary>The folders a standard object sits in, broadest first; see <see cref="TreeEntry.Path"/>.</summary>
    public static IReadOnlyList<string> Of(ObjectKind kind, StandardObject standard) => kind switch
    {
        ObjectKind.Unit => [UnitRace(standard.Race), standard.Campaign ? "Campaign" : "Melee", UnitType(standard)],
        ObjectKind.Item => [ItemClasses.GetValueOrDefault(standard.Category, Other(standard.Category))],
        ObjectKind.Destructable =>
            [DestructableCategories.GetValueOrDefault(standard.Category, Other(standard.Category))],
        ObjectKind.Doodad => [DoodadCategories.GetValueOrDefault(standard.Category, Other(standard.Category))],
        ObjectKind.Ability when standard.Hidden => [HiddenFolder],
        ObjectKind.Ability => [AbilityRace(standard.Race), AbilityUse(standard.Category)],
        ObjectKind.Buff => [RaceOrSpecial(standard.Race), standard.Category == "1" ? "Effects" : "Buffs"],
        ObjectKind.Upgrade => [RaceOrSpecial(standard.Race)],
        _ => [Other(standard.Race)],
    };

    /// <summary>
    /// The standard objects in tree order: by their folders' places in <see cref="Order"/>, level by level, and in
    /// the data's order within a folder and among folders it does not list.
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

            return a.Index.CompareTo(b.Index);
        });
        return ranked.Select(each => each.Standard).ToList();
    }

    private static string Key(IReadOnlyList<string> path, int depth) => string.Join("/", path.Take(depth + 1));

    private static string UnitRace(string race) => Races.GetValueOrDefault(race, Other(race));

    private static string UnitType(StandardObject standard) =>
        standard.Special ? "Special"
        : standard.Hero ? "Heroes"
        : standard.Building ? "Buildings"
        : "Units";

    // As World Editor files them (Sven, 2026-10-27): the creeps' abilities are Neutral Hostile, the naga's Neutral
    // Passive, and the rest (race "other", which World Editor names Special) Special.
    private static string AbilityRace(string race) =>
        Races.TryGetValue(race, out var name) ? name
        : race switch
        {
            "creeps" => "Neutral Hostile",
            "naga" => "Neutral Passive",
            _ => "Special",
        };

    private static string AbilityUse(string sort) => sort switch
    {
        "hero" => "Heroes",
        "item" => "Items",
        _ => "Units",
    };

    private static string RaceOrSpecial(string race) => Races.GetValueOrDefault(race, "Special");

    // A value the lists do not know, shown with a capital letter; none as Other.
    private static string Other(string value) =>
        value.Length == 0 ? "Other" : char.ToUpperInvariant(value[0]) + value[1..];
}
