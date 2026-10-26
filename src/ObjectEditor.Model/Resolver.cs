using System.Globalization;
using ObjectEditor.Files;
using ObjectEditor.GameData;

namespace ObjectEditor.Model;

/// <summary>
/// Works out what one kind's objects show: each field's value is the map's modification when it has one, else the
/// standard value. A custom object takes its standard values from its standard base, never from the map's changes to
/// that base, as the game does.
/// </summary>
internal sealed class Resolver(ObjectKind kind, KindData data, WtsFile? strings)
{
    private const string KeyPrefix = "WESTRING_";

    /// <summary>
    /// The fields that hold an object's name, in the order they are looked at: a buff's name in the game is its
    /// tooltip, as Moonwell's generator reads it.
    /// </summary>
    private static IReadOnlyList<string> NameFields(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit or ObjectKind.Item => ["unam"],
        ObjectKind.Destructable => ["bnam"],
        ObjectKind.Doodad => ["dnam"],
        ObjectKind.Ability => ["anam"],
        ObjectKind.Buff => ["ftip", "fnam"],
        ObjectKind.Upgrade => ["gnam"],
        _ => [],
    };

    /// <summary>
    /// The field that holds World Editor's suffix (<c>(Evil)</c>); null for doodads, which have none.
    /// </summary>
    private static string? SuffixField(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit or ObjectKind.Item => "unsf",
        ObjectKind.Destructable => "bsuf",
        ObjectKind.Ability => "ansf",
        ObjectKind.Buff => "fnsf",
        ObjectKind.Upgrade => "gnsf",
        _ => null,
    };

    /// <summary>
    /// The field that holds the number of levels: the kind's level count, or a doodad's number of variations
    /// (<c>dvar</c>, as Moonwell's build counts them).
    /// </summary>
    private string? LevelField => data.LevelField ?? (kind == ObjectKind.Doodad ? "dvar" : null);

    /// <summary>The kind's name in an object's chain.</summary>
    private static string KindName(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit => "Unit",
        ObjectKind.Item => "Item",
        ObjectKind.Destructable => "Destructable",
        ObjectKind.Doodad => "Doodad",
        ObjectKind.Ability => "Ability",
        ObjectKind.Buff => "Buff",
        ObjectKind.Upgrade => "Upgrade",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The object's view. <paramref name="standard"/> is the standard object (a custom object's base), null only for a
    /// custom object based on an id the game does not have, which then shows no fields.
    /// </summary>
    public ObjectView View(string id, StandardObject? standard, MapObject? map, string? readOnly)
    {
        var custom = map is { Custom: true };
        var name = Name(standard, map, id);
        var levels = Levels(standard, map);
        var fields = new List<FieldView>();
        if (standard != null)
        {
            foreach (var field in data.Fields)
            {
                if (!data.Applies(field, standard))
                {
                    continue;
                }

                if (!field.PerLevel)
                {
                    fields.Add(Field(field, 0, standard, map));
                    continue;
                }

                for (var level = 1; level <= levels; level++)
                {
                    fields.Add(Field(field, level, standard, map));
                }
            }
        }

        var chain = new List<ChainLink> { new(KindName(kind), "") };
        if (standard is { Hero: true } && kind == ObjectKind.Unit)
        {
            chain.Add(new ChainLink("Hero", ""));
        }
        else if (standard is { Building: true } && kind == ObjectKind.Unit)
        {
            chain.Add(new ChainLink("Building", ""));
        }

        if (custom)
        {
            var baseName = standard == null || standard.Name.Length == 0 ? map!.BaseId : standard.Name;
            chain.Add(new ChainLink(baseName, map!.BaseId));
        }

        chain.Add(new ChainLink(name, id));
        return new ObjectView(
            ObjectKinds.Key(kind),
            id,
            custom ? map!.BaseId : null,
            name,
            chain,
            levels,
            readOnly,
            fields);
    }

    /// <summary>
    /// The object's name: the map's (from its name fields) when it sets one, else the standard object's, else
    /// <paramref name="fallback"/>.
    /// </summary>
    public string Name(StandardObject? standard, MapObject? map, string fallback)
    {
        foreach (var id in NameFields(kind))
        {
            var field = data.Field(id);
            var value = field == null ? null : MapValue(field, field.PerLevel ? 1 : 0, map);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return standard is { Name.Length: > 0 } ? standard.Name : fallback;
    }

    /// <summary>The object's suffix: the map's when it sets one, else the standard object's.</summary>
    public string Suffix(StandardObject? standard, MapObject? map)
    {
        var id = SuffixField(kind);
        var field = id == null ? null : data.Field(id);
        var value = field == null ? null : MapValue(field, 0, map);
        return !string.IsNullOrEmpty(value) ? value : standard?.Suffix ?? "";
    }

    /// <summary>
    /// The number of levels: the map's level count when it sets one, else the standard object's; else 0.
    /// </summary>
    internal int Levels(StandardObject? standard, MapObject? map)
    {
        var id = LevelField;
        var field = id == null ? null : data.Field(id);
        if (field == null)
        {
            return 0;
        }

        var text = MapValue(field, 0, map) ?? (standard == null ? "" : StandardValue(field, 0, standard));
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var levels) && levels > 0
            ? levels
            : 0;
    }

    /// <summary>One field of an object at a level, as <see cref="View"/> shows it.</summary>
    internal FieldView Field(FieldInfo field, int level, StandardObject standard, MapObject? map)
    {
        var standardValue = StandardValue(field, level, standard);
        var mapValue = MapValue(field, level, map);
        var modes = standard.Modes.GetValueOrDefault(Key(field, level));
        return new FieldView(
            field.Id,
            field.Label,
            field.Category,
            field.Sort,
            field.Type,
            field.Choice,
            field.ListOf,
            level,
            mapValue ?? standardValue,
            standardValue,
            mapValue != null,
            field.Skin,
            mapValue == null ? null : Warning(field, mapValue),
            modes);
    }

    /// <summary>
    /// What is wrong with a value the map sets, by the field's rules (World Editor's limits); null when nothing is. A
    /// value past a limit is kept: World Editor allows it too (Shift+Enter). A number below 0 in a field that never
    /// allows one is below a limit of 0.
    /// </summary>
    internal static string? Warning(FieldInfo field, string value)
    {
        var warnings = new List<string>();
        if (field.Type is "int" or "float"
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var min = field.Min ?? (field.NoNegative ? 0 : null);
            if (field.Max is { } max && number > max)
            {
                warnings.Add($"Above World Editor's limit of {Number(max)}.");
            }

            if (min is { } low && number < low)
            {
                warnings.Add($"Below World Editor's limit of {Number(low)}.");
            }
        }

        if (field.MaxLength is { } length && value.Length > length)
        {
            warnings.Add($"Longer than World Editor's limit of {Number(length)} characters.");
        }

        // A list's entries are separated by commas.
        if (field.MaxEntries is { } entries && value.Length > 0 && value.Split(',').Length > entries)
        {
            warnings.Add($"More than World Editor's limit of {Number(entries)} entries.");
        }

        if (field.Required && value.Length == 0)
        {
            warnings.Add("May not be empty.");
        }

        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    private static string Number(double number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The map's value of a field at a level, shown as text; null when the map has no modification of it. The file the
    /// field belongs to (skin or main) is looked at first. A modification matches by field and level, whatever its
    /// data pointer.
    /// </summary>
    private string? MapValue(FieldInfo field, int level, MapObject? map)
    {
        if (map == null)
        {
            return null;
        }

        var first = field.Skin ? map.Skin : map.Main;
        var second = field.Skin ? map.Main : map.Skin;
        var mod = first?.Find(field.Id, level) ?? second?.Find(field.Id, level);
        return mod == null ? null : Show(mod.Value);
    }

    /// <summary>
    /// The standard value of a field at a level, shown as text; empty when the object has none. A real number is
    /// written as a map's is, and a name the data leaves as a <c>WESTRING_</c> key shows the object's name.
    /// </summary>
    internal string StandardValue(FieldInfo field, int level, StandardObject standard)
    {
        var text = standard.Values.GetValueOrDefault(Key(field, level), "");
        if (field.Store is "real" or "unreal"
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return Real(number);
        }

        if (text.StartsWith(KeyPrefix, StringComparison.Ordinal) && NameFields(kind).Contains(field.Id))
        {
            return standard.Name;
        }

        return text;
    }

    /// <summary>
    /// A modification's value as text; a <c>TRIGSTR_n</c> shows the strings file's text when it has it.
    /// </summary>
    internal string Show(FieldValue value) => value switch
    {
        FieldValue.Int number => number.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Real real => Real(real.Value),
        FieldValue.Unreal unreal => Real(unreal.Value),
        FieldValue.Text text => Strings(text.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private string Strings(string text)
    {
        var number = WtsFile.TriggerString(text);
        return number == null ? text : strings?.Get(number.Value) ?? text;
    }

    private static string Real(float number) => number.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The key of a field's standard value: its id, or <c>id:level</c> for a level.</summary>
    internal static string Key(FieldInfo field, int level) =>
        level == 0 ? field.Id : field.Id + ":" + level.ToString(CultureInfo.InvariantCulture);
}
