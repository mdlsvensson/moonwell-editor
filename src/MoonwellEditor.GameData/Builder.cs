using Moonwell.GameData;
using MoonwellEditor.Files;
using MoonwellObject = Moonwell.GameData.StandardObject;

namespace MoonwellEditor.GameData;

/// <summary>
/// Makes the game data with Moonwell's game-data reader (the <c>Moonwell.GameData</c> package), calling it in the order
/// Moonwell's generator does (its <c>GeneratedObjects.Make</c>), and adds what the editor needs that the reader's
/// records lack.
/// </summary>
internal static class Builder
{
    // The name Moonwell gives the field that holds an object's number of levels (alev, glvl).
    private const string LevelCountName = "LevelCount";
    private const string KeyPrefix = "WESTRING_";
    private const string BaseTables = "base";

    // The table World Editor files and sorts the units by (Sven, 2026-10-27).
    private const string UnitUi = "units/unitui.slk";
    private static readonly string[] UnitUiColumns = ["campaign", "special", "unitClass", "hostilePal", "hiddenInEditor"];

    // The fields each upgrade effect uses, which World Editor labels and shows by the effect an upgrade names.
    private const string UpgradeEffects = "units/upgradeeffectmetadata.slk";

    // Lists World Editor names that uniteditordata.txt does not hold: the tilesets by their letters, in World Editor's
    // order (its names for P, e, R and u read off its screen, 2026-10-27), and the destructable and doodad categories.
    private static readonly (string Value, string Key)[] Tilesets =
    [
        ("*", "WESTRING_LOCALE_ALL"), ("L", "WESTRING_LOCALE_LORDAERON_SUMMER"),
        ("F", "WESTRING_LOCALE_LORDAERON_FALL"), ("W", "WESTRING_LOCALE_LORDAERON_WINTER"),
        ("B", "WESTRING_LOCALE_BARRENS"), ("A", "WESTRING_LOCALE_ASHENVALE"), ("C", "WESTRING_LOCALE_FELWOOD"),
        ("N", "WESTRING_LOCALE_NORTHREND"), ("Y", "WESTRING_LOCALE_CITYSCAPE"), ("V", "WESTRING_LOCALE_VILLAGE"),
        ("Q", "WESTRING_LOCALE_VILLAGEFALL"), ("D", "WESTRING_LOCALE_DUNGEON"), ("G", "WESTRING_LOCALE_DUNGEON2"),
        ("X", "WESTRING_LOCALE_DALARAN"), ("Z", "WESTRING_LOCALE_RUINS"), ("I", "WESTRING_LOCALE_ICECROWN"),
        ("O", "WESTRING_LOCALE_OUTLAND"), ("K", "WESTRING_LOCALE_BLACKCITADEL"), ("J", "WESTRING_LOCALE_DALARANRUINS"),
        ("P", "WESTRING_LOCALE_CITYSCAPE_RUINS"), ("e", "WESTRING_LOCALE_LORDAERON_CAPITAL"),
        ("R", "WESTRING_LOCALE_LORDAERON_CAPITAL_RUINS"), ("u", "WESTRING_LOCALE_UNDERCITY"),
    ];

    private static readonly (string Value, string Key)[] DestructableCategories =
    [
        ("D", "WESTRING_DTYPE_DESTRUCTABLE"), ("P", "WESTRING_DTYPE_PATHING"), ("B", "WESTRING_DTYPE_BRIDGE"),
    ];

    private static readonly (string Value, string Key)[] DoodadCategories =
    [
        ("O", "WESTRING_DTYPE_PROPS"), ("S", "WESTRING_DTYPE_STRUCTURES"), ("W", "WESTRING_DTYPE_WATER"),
        ("C", "WESTRING_DTYPE_CLIFF"), ("E", "WESTRING_DTYPE_ENVIRONMENT"), ("Z", "WESTRING_DTYPE_CINEMATIC"),
    ];

    // The metadata types the editor gives a list of its own (above), as a choice.
    private static readonly string[] OwnLists = ["tilesetList", "destructableCategory", "doodadCategory"];

    /// <summary>
    /// Makes the game data from the tables, with the rows of the two tables Moonwell's records do not use
    /// (<see cref="UnitUi"/>, <see cref="UpgradeEffects"/>).
    /// </summary>
    /// <param name="tables">The install's tables.</param>
    /// <param name="source">The install's folder.</param>
    public static GameData Build(GameTables tables, string source)
    {
        var listed = Choices.All(tables, null);
        var fieldsOf = new Dictionary<Kind, IReadOnlyList<Field>>();
        var objectsOf = new Dictionary<Kind, IReadOnlyList<MoonwellObject>>();
        foreach (var kind in Enum.GetValues<Kind>())
        {
            fieldsOf[kind] = Fields.Of(tables, kind, null, listed);
            objectsOf[kind] = StandardObjects.Of(tables, kind, fieldsOf[kind]);
        }

        // A bit the game's own data uses is one of its type's values, and a negative it holds is allowed (as Moonwell).
        var kindsOf = fieldsOf.Keys.Select(kind => (fieldsOf[kind], objectsOf[kind])).ToList();
        var choices = Choices.WithStandardBits(listed, kindsOf);
        var lists = new SortedDictionary<string, ChoiceList>(StringComparer.Ordinal);
        foreach (var (type, choice) in choices)
        {
            lists[type] = new ChoiceList(
                choice.Flags,
                choice.Store,
                choice.Members
                    .Select(member => new ChoiceEntry(
                        member.Text,
                        LabelOf(member),
                        member.Alternates,
                        Hidden: member.Name.StartsWith("Unlisted", StringComparison.Ordinal)))
                    .ToList());
        }

        lists["tilesetList"] = OwnList(tables, true, Tilesets);
        lists["destructableCategory"] = OwnList(tables, false, DestructableCategories);
        lists["doodadCategory"] = OwnList(tables, false, DoodadCategories);

        var kinds = new Dictionary<Kind, KindData>();
        foreach (var kind in Enum.GetValues<Kind>())
        {
            var fields = Fields.WithStandardNegatives(fieldsOf[kind], objectsOf[kind])
                .Select(field => field.Choice == null ? field : field with { Choice = choices[field.Choice.Type] })
                .ToList();
            kinds[kind] = KindDataOf(
                tables,
                kind,
                fields,
                objectsOf[kind],
                kind == Kind.Unit ? UnitUiOf(tables.Table(UnitUi)) : [],
                kind == Kind.Upgrade ? EffectsOf(tables, tables.Table(UpgradeEffects), lists) : null);
        }

        return new GameData(source, tables.Build?.Version, kinds, lists);
    }

    private static KindData KindDataOf(
        GameTables tables,
        Kind kind,
        IReadOnlyList<Field> fields,
        IReadOnlyList<MoonwellObject> objects,
        Dictionary<string, Dictionary<string, string>> unitUi,
        IReadOnlyDictionary<string, IReadOnlyList<UpgradeEffectField>>? effects)
    {
        var meta = new Dictionary<string, MetaRow>(StringComparer.Ordinal);
        foreach (var row in tables.Meta(kind))
        {
            meta[row.Id] = row;
        }

        var infos = fields.Select(field => Info(field, meta[field.Id])).ToList();
        var levelField = fields.FirstOrDefault(field => field.Name == LevelCountName)?.Id;
        var infoOf = infos.ToDictionary(info => info.Id, StringComparer.Ordinal);
        var baseTable = tables.Objects(kind, null);
        var baseRows = Index(baseTable);
        var places = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var place = 0; place < baseTable.Count; place++)
        {
            places.TryAdd(baseTable[place].Id, place);
        }

        var setRows = new Dictionary<string, Dictionary<string, ObjectRows>>(StringComparer.Ordinal);
        var result = new List<StandardObject>(objects.Count);
        for (var index = 0; index < objects.Count; index++)
        {
            var obj = objects[index];
            var rows = PrimaryRows(tables, kind, obj, baseRows, setRows);
            var standard = Object(tables, kind, obj, rows, infoOf, unitUi.GetValueOrDefault(obj.Id));
            result.Add(standard with { Row = places.TryGetValue(obj.Id, out var place) ? place : baseTable.Count + index });
        }

        return new KindData(kind, levelField, infos, result, effects);
    }

    private static FieldInfo Info(Field field, MetaRow meta)
    {
        // An ability's Effects (aeff) hold effect ids, which are buffs of the buff table (Xclf), as its Buffs (abuf)
        // hold buff ids; Moonwell's reader types only buffList as a list, so the editor types effectList the same.
        // A unit's Normal Skin and Hero Skin (abilitySkinList) hold ability ids too, which World Editor shows by name.
        var metaType = Cell(meta, "type");
        var effects = metaType == "effectList";
        var skins = metaType == "abilitySkinList";
        var ownList = field.Choice == null && OwnLists.Contains(metaType);
        var type = effects || skins ? "ObjectList" : ownList ? "Choice" : field.Type.ToString();
        return new FieldInfo(
            field.Id,
            field.Rules.Label ?? field.Name,
            Cell(meta, "category"),
            Cell(meta, "sort"),
            char.ToLowerInvariant(type[0]) + type.Substring(1),
            field.Store,
            effects ? "Buff" : skins ? "Ability" : field.ListOf,
            field.PerLevel,
            field.DataPointer,
            field.Skin,
            field.Specific,
            field.NotSpecific,
            field.ForUnit,
            field.ForHero,
            field.ForBuilding,
            field.Rules.Min,
            field.Rules.Max,
            field.Rules.MaxLength ?? TextSize(Cell(meta, "maxVal"), field.Rules.Label ?? field.Name),
            field.Rules.MaxEntries,
            field.Rules.Required,
            field.Rules.NoNegative,
            ownList ? metaType : field.Choice?.Type,
            metaType switch
            {
                "icon" => "icon",
                "model" or "modelList" => "model",
                "texture" => "texture",
                _ => null,
            },
            metaType);
    }

    /// <summary>
    /// The rows Moonwell read the object's values from: the base tables', or for an object the base tables lack, the
    /// first balance set's it exists in.
    /// </summary>
    private static ObjectRows PrimaryRows(
        GameTables tables,
        Kind kind,
        MoonwellObject obj,
        Dictionary<string, ObjectRows> baseRows,
        Dictionary<string, Dictionary<string, ObjectRows>> setRows)
    {
        if (!obj.MissingFrom.Contains(BaseTables))
        {
            return baseRows[obj.Id];
        }

        var set = tables.Sets.First(name => !obj.MissingFrom.Contains(name));
        if (!setRows.TryGetValue(set, out var rows))
        {
            setRows[set] = rows = Index(tables.Objects(kind, set));
        }

        return rows[obj.Id];
    }

    private static StandardObject Object(
        GameTables tables,
        Kind kind,
        MoonwellObject obj,
        ObjectRows rows,
        Dictionary<string, FieldInfo> infos,
        Dictionary<string, string>? ui)
    {
        // World Editor files a unit by unitui.slk's own campaign and special, which the skin text may contradict (it
        // gives the High Elf Archmage, Haah, campaign 0 against the table's 1), and lists a folder's units by its
        // unitClass (HUnit01, HUnit02...), the classic order (Sven, 2026-10-27).
        var campaign = ui?.GetValueOrDefault("campaign") is { } inTable ? inTable == "1" : obj.Campaign;
        var special = (ui?.GetValueOrDefault("special") ?? Clean(Read(rows, "special"))) == "1";
        var categoryColumn = kind switch
        {
            Kind.Item => "class",
            Kind.Destructable or Kind.Doodad => "category",
            Kind.Ability => "sort",
            Kind.Buff => "isEffect",
            _ => null,
        };
        var category = kind == Kind.Unit ? ui?.GetValueOrDefault("unitClass") ?? ""
            : categoryColumn == null ? ""
            : Clean(Read(rows, categoryColumn)) ?? "";
        // World Editor leaves out the abilities whose useInEditor is 0 and the units whose hiddenInEditor is 1 (the
        // Shoveler and Sammy's cube), files a unit of no playable race by its hostilePal (Neutral Hostile or Passive),
        // and lists a buff folder by sort (hero, then unit) and a destructable or doodad folder by doodClass before
        // the names (World Editor's screens, 2026-10-27).
        var hidden = kind switch
        {
            Kind.Ability => Clean(Read(rows, "useInEditor")) == "0",
            Kind.Unit => ui?.GetValueOrDefault("hiddenInEditor") == "1",
            _ => false,
        };
        var hostile = kind == Kind.Unit && ui?.GetValueOrDefault("hostilePal") == "1";
        var sortColumn = kind switch
        {
            Kind.Buff => "sort",
            Kind.Destructable or Kind.Doodad => "doodClass",
            _ => null,
        };
        // Kept as written: a doodClass of _ (most of them) comes after A and B.
        var sort = sortColumn == null ? "" : Read(rows, sortColumn)?.Trim() ?? "";
        // World Editor shows a campaign hero by its proper name (Haah, the High Elf Archmage, as Kelen the Seeker).
        var properName = Clean(Read(rows, "Propernames"))?.Split(',')[0].Trim();
        var name = campaign && obj.IsHero && !string.IsNullOrEmpty(properName) ? Resolved(tables, properName) : obj.Name;
        // Moonwell's suffix is the one World Editor shows: the comma list's first entry, its bracket closed (nvr0's
        // "(ruined, two story)" is "(ruined)"; Sven, 2026-10-28), an upgrade's first level's, none for a doodad.
        var suffix = obj.EditorSuffix;

        var standard = new StandardObject(
            obj.Id,
            name,
            suffix,
            obj.Race,
            campaign,
            obj.IsHero,
            obj.Building,
            special,
            category,
            hidden,
            obj.Code,
            obj.Values,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            obj.Sets,
            obj.MissingFrom,
            hostile,
            sort,
            Version: Clean(Read(rows, "version")) ?? "",
            Own: obj.Own);
        return standard with { Modes = Modes(obj, standard, kind, infos) };
    }

    /// <summary>
    /// Moonwell's values per graphics mode (<see cref="MoonwellObject.Modes"/>), of the fields that apply to the object
    /// by <see cref="KindData.Applies(Kind, FieldInfo, StandardObject)"/>.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Modes(
        MoonwellObject obj,
        StandardObject standard,
        Kind kind,
        Dictionary<string, FieldInfo> infos)
    {
        var modes = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (key, values) in obj.Modes)
        {
            // A key is the field's id, or id:level for a field of each level (aart:1).
            var colon = key.IndexOf(':');
            if (infos.TryGetValue(colon < 0 ? key : key.Substring(0, colon), out var info)
                && KindData.Applies(kind, info, standard))
            {
                modes[key] = values;
            }
        }

        return modes;
    }

    /// <summary>
    /// The <see cref="UnitUiColumns"/> of each unit in the base <c>unitui.slk</c>, as the table writes them (a cell it
    /// leaves empty is absent); empty when the tables have no such table.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> UnitUiOf(IReadOnlyList<MetaRow>? table)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var row in table ?? [])
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in UnitUiColumns)
            {
                // The class is kept as written: a class of one space (Garek's) comes before HHero01, as World Editor
                // lists them.
                var cell = row.Columns.GetValueOrDefault(name);
                if ((name == "unitClass" ? cell : Clean(cell)) is { Length: > 0 } value)
                {
                    cells[name] = value;
                }
            }

            result[row.Id] = cells;
        }

        return result;
    }

    /// <summary>A list the editor makes itself, its labels from the World Editor strings.</summary>
    private static ChoiceList OwnList(GameTables tables, bool flags, (string Value, string Key)[] entries) =>
        new(flags, "string", entries.Select(entry => new ChoiceEntry(entry.Value, tables.Label(entry.Key), [])).ToList());

    /// <summary>
    /// The fields each upgrade effect uses, from <see cref="UpgradeEffects"/>: their data type, World Editor's label,
    /// and how they are typed (a whole number, a real, a choice of <paramref name="lists"/>, an ability or a unit).
    /// Empty when the tables have no such table.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<UpgradeEffectField>> EffectsOf(
        GameTables tables,
        IReadOnlyList<MetaRow>? effects,
        IReadOnlyDictionary<string, ChoiceList> lists)
    {
        var found = new Dictionary<string, List<UpgradeEffectField>>(StringComparer.Ordinal);
        foreach (var row in effects ?? [])
        {
            if (Cell(row, "effectID") is not { Length: > 0 } effect || Cell(row, "dataType") is not { Length: > 0 } dataType)
            {
                continue;
            }

            var metaType = Cell(row, "type");
            var (type, choice, listOf) = metaType switch
            {
                "int" => ("int", (string?)null, (string?)null),
                "real" or "unreal" => ("float", null, null),
                "abilCode" => ("objectRef", null, "Ability"),
                "unitCode" => ("objectRef", null, "Unit"),
                _ when lists.ContainsKey(metaType) => ("choice", metaType, null),
                _ => ("int", null, null),
            };
            var label = tables.Label(Cell(row, "displayName"));
            if (!found.TryGetValue(effect, out var fields))
            {
                found[effect] = fields = [];
            }

            fields.Add(new UpgradeEffectField(dataType, label, type, choice, listOf));
        }

        return found.ToDictionary(
            pair => pair.Key, IReadOnlyList<UpgradeEffectField> (pair) => pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, ObjectRows> Index(IReadOnlyList<ObjectRows> rows)
    {
        var index = new Dictionary<string, ObjectRows>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            index[row.Id] = row;
        }

        return index;
    }

    /// <summary>
    /// The label of a <c>WESTRING_</c> key, or the empty string when the tables have none; any other text as it is.
    /// </summary>
    private static string Resolved(GameTables tables, string text)
    {
        var label = tables.Label(text);
        return label == text && text.StartsWith(KeyPrefix, StringComparison.Ordinal) ? "" : label;
    }

    /// <summary>A choice value's label, or its text when the tables have no label for it.</summary>
    private static string LabelOf(ChoiceMember member) => member.Label.Length > 0 ? member.Label : member.Text;

    /// <summary>
    /// The most characters World Editor's box takes for a tooltip, whose metadata names a text size: 2048 for a tooltip
    /// (<c>TTDesc</c>: Basic, Revive, Awaken, Normal, Turn Off, Learn), 8191 for an extended one (<c>TTUber</c>)
    /// (docs/game-facts.md, Sven, 2026-10-29). Null for any other field: other texts of <c>TTDesc</c> (a model file, a
    /// description) and a name's <c>TTName</c> were not measured.
    /// </summary>
    private static int? TextSize(string maxVal, string label) =>
        !label.StartsWith("Tooltip", StringComparison.Ordinal) ? null : maxVal switch
        {
            "TTDesc" => 2048,
            "TTUber" => 8191,
            _ => null,
        };

    private static string Cell(MetaRow meta, string column) =>
        meta.Columns.TryGetValue(column, out var value) ? value.Trim() : "";

    /// <summary>
    /// The value of a key in the object's rows as Moonwell reads its values (Definitive Edition's reading): the key's
    /// <c>:de</c> value, else its <c>:hd</c> value, else its own; a key is looked up as spelled and, failing that,
    /// without regard to case when one key matches. Null for none.
    /// </summary>
    private static string? Read(ObjectRows rows, string key)
    {
        foreach (var spelling in (string[])[key + ":de", key + ":hd", key])
        {
            if (rows.Values.TryGetValue(spelling, out var value))
            {
                return value;
            }

            var matches = rows.Values.Where(pair => string.Equals(pair.Key, spelling, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matches.Count == 1)
            {
                return matches[0].Value;
            }
        }

        return null;
    }

    /// <summary>A cell's value, trimmed: null when empty or a table's placeholder <c>-</c> or <c>_</c>.</summary>
    private static string? Clean(string? cell)
    {
        var value = cell?.Trim();
        return string.IsNullOrEmpty(value) || value == "-" || value == "_" ? null : value;
    }
}
