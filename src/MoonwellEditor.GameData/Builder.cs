using Moonwell.GameData;
using MoonwellEditor.Files;
using War3Net.IO.Slk;
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

    public static GameData Build(string folder)
    {
        var tables = ExportTables.Read(folder);
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
                choice.Members.Select(member => new ChoiceEntry(member.Text, LabelOf(member), member.Alternates)).ToList());
        }

        lists["tilesetList"] = OwnList(tables, true, Tilesets);
        lists["destructableCategory"] = OwnList(tables, false, DestructableCategories);
        lists["doodadCategory"] = OwnList(tables, false, DoodadCategories);

        var kinds = new Dictionary<ObjectKind, KindData>();
        foreach (var kind in Enum.GetValues<Kind>())
        {
            var fields = Fields.WithStandardNegatives(fieldsOf[kind], objectsOf[kind])
                .Select(field => field.Choice == null ? field : field with { Choice = choices[field.Choice.Type] })
                .ToList();
            kinds[KindOf(kind)] = KindDataOf(
                tables,
                kind,
                fields,
                objectsOf[kind],
                kind == Kind.Unit ? UnitUiOf(folder) : [],
                kind == Kind.Upgrade ? EffectsOf(tables, folder, lists) : null);
        }

        return new GameData(folder, kinds, lists);
    }

    /// <summary>The kind as the Object Editor names it.</summary>
    public static ObjectKind KindOf(Kind kind) => kind switch
    {
        Kind.Unit => ObjectKind.Unit,
        Kind.Item => ObjectKind.Item,
        Kind.Destructable => ObjectKind.Destructable,
        Kind.Doodad => ObjectKind.Doodad,
        Kind.Ability => ObjectKind.Ability,
        Kind.Buff => ObjectKind.Buff,
        Kind.Upgrade => ObjectKind.Upgrade,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static KindData KindDataOf(
        ExportTables tables,
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
        var objectKind = KindOf(kind);
        var readers = fields.Select(field => new FieldReader(field, meta[field.Id])).ToList();
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
            var standard = Object(tables, objectKind, levelField, obj, rows, infos, readers, unitUi.GetValueOrDefault(obj.Id));
            result.Add(standard with { Row = places.TryGetValue(obj.Id, out var place) ? place : baseTable.Count + index });
        }

        return new KindData(objectKind, levelField, infos, result, effects);
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
            Codes(Cell(meta, "notSpecific")),
            Cell(meta, "useUnit") == "1",
            Cell(meta, "useHero") == "1",
            Cell(meta, "useBuilding") == "1",
            field.Rules.Min,
            field.Rules.Max,
            field.Rules.MaxLength,
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
        ExportTables tables,
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
        ExportTables tables,
        ObjectKind kind,
        string? levelField,
        MoonwellObject obj,
        ObjectRows rows,
        List<FieldInfo> infos,
        List<FieldReader> readers,
        Dictionary<string, string>? ui)
    {
        var row = new ModeRow(rows);
        var building = kind == ObjectKind.Unit && FieldReader.Clean(row.Get("isbldg", "de")?.Value) == "1";
        // World Editor files a unit by unitui.slk's own campaign and special, which the skin text may contradict (it
        // gives the High Elf Archmage, Haah, campaign 0 against the table's 1), and lists a folder's units by its
        // unitClass (HUnit01, HUnit02...), the classic order (Sven, 2026-10-27).
        var campaign = ui?.GetValueOrDefault("campaign") is { } inTable ? inTable == "1" : obj.Campaign;
        var special = (ui?.GetValueOrDefault("special") ?? FieldReader.Clean(row.Get("special", "de")?.Value)) == "1";
        var suffix = Resolved(tables, FieldReader.Clean(row.Get("EditorSuffix", "de")?.Value) ?? "");
        // An upgrade keeps a suffix per level; World Editor names it by its first (Glyph of Fortification (Upgrade 1)).
        // A doodad has no suffix field, and World Editor shows none (the data's "(Default)" is not shown: Wooden Coffin).
        if (kind == ObjectKind.Upgrade)
        {
            suffix = suffix.Split(',')[0].Trim();
        }
        else if (kind == ObjectKind.Doodad)
        {
            suffix = "";
        }
        var categoryColumn = kind switch
        {
            ObjectKind.Item => "class",
            ObjectKind.Destructable or ObjectKind.Doodad => "category",
            ObjectKind.Ability => "sort",
            ObjectKind.Buff => "isEffect",
            _ => null,
        };
        var category = kind == ObjectKind.Unit ? ui?.GetValueOrDefault("unitClass") ?? ""
            : categoryColumn == null ? ""
            : FieldReader.Clean(row.Get(categoryColumn, "de")?.Value) ?? "";
        // World Editor leaves out the abilities whose useInEditor is 0 and the units whose hiddenInEditor is 1 (the
        // Shoveler and Sammy's cube), files a unit of no playable race by its hostilePal (Neutral Hostile or Passive),
        // and lists a buff folder by sort (hero, then unit) and a destructable or doodad folder by doodClass before
        // the names (World Editor's screens, 2026-10-27).
        var hidden = kind switch
        {
            ObjectKind.Ability => FieldReader.Clean(row.Get("useInEditor", "de")?.Value) == "0",
            ObjectKind.Unit => ui?.GetValueOrDefault("hiddenInEditor") == "1",
            _ => false,
        };
        var hostile = kind == ObjectKind.Unit && ui?.GetValueOrDefault("hostilePal") == "1";
        var sortColumn = kind switch
        {
            ObjectKind.Buff => "sort",
            ObjectKind.Destructable or ObjectKind.Doodad => "doodClass",
            _ => null,
        };
        // Kept as written: a doodClass of _ (most of them) comes after A and B.
        var sort = sortColumn == null ? "" : row.Get(sortColumn, "de")?.Value.Trim() ?? "";
        // World Editor shows a campaign hero by its proper name (Haah, the High Elf Archmage, as Kelen the Seeker).
        var properName = FieldReader.Clean(row.Get("Propernames", "de")?.Value)?.Split(',')[0].Trim();
        var name = campaign && obj.IsHero && !string.IsNullOrEmpty(properName) ? Resolved(tables, properName) : obj.Name;
        var standard = new StandardObject(
            obj.Id,
            name,
            suffix,
            obj.Race,
            campaign,
            obj.IsHero,
            building,
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
            Version: FieldReader.Clean(row.Get("version", "de")?.Value) ?? "");
        return standard with { Modes = Modes(row, standard, kind, levelField, infos, readers) };
    }

    /// <summary>The values of the fields that apply to the object that differ between the graphics modes.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Modes(
        ModeRow row,
        StandardObject obj,
        ObjectKind kind,
        string? levelField,
        List<FieldInfo> infos,
        List<FieldReader> readers)
    {
        var modes = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        int? levels = levelField == null ? null : KindData.Levels(levelField, obj);
        for (var i = 0; i < infos.Count; i++)
        {
            // Only the skin files key values by mode, and they hold no column per level.
            if (!row.HasModes(readers[i].Column) || !KindData.Applies(kind, infos[i], obj))
            {
                continue;
            }

            var byMode = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var mode in ModeRow.Modes)
            {
                byMode[mode] = new Dictionary<string, string>(StringComparer.Ordinal);
                readers[i].Read(row, mode, levels, byMode[mode]);
            }

            foreach (var key in byMode.Values.SelectMany(values => values.Keys).Distinct().ToList())
            {
                var values = ModeRow.Modes.ToDictionary(
                    mode => mode,
                    mode => byMode[mode].GetValueOrDefault(key, ""),
                    StringComparer.Ordinal);
                if (values.Values.Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    modes[key] = values;
                }
            }
        }

        return modes;
    }

    /// <summary>
    /// The <see cref="UnitUiColumns"/> of each unit in the base <c>unitui.slk</c>, as the table writes them (a cell it
    /// leaves empty is absent); empty when the folder has no such table.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> UnitUiOf(string folder)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var path = Path.Combine(folder, UnitUi);
        if (!File.Exists(path))
        {
            return result;
        }

        SylkTable table;
        using (var stream = File.OpenRead(path))
        {
            table = new SylkParser(keepText: true).Parse(stream);
        }

        // The first column with a name holds the ids, as Moonwell reads its tables.
        int? idColumn = null;
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var x = 0; x <= table.Columns; x++)
        {
            if (table[x, 0] is not string name)
            {
                continue;
            }

            idColumn ??= x;
            if (Array.IndexOf(UnitUiColumns, name) >= 0)
            {
                columns[name] = x;
            }
        }

        for (var y = 1; idColumn != null && y <= table.Rows; y++)
        {
            if (table[idColumn.Value, y] is not string { Length: > 0 } id)
            {
                continue;
            }

            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, x) in columns)
            {
                // The class is kept as written: a class of one space (Garek's) comes before HHero01, as World Editor
                // lists them.
                var cell = table[x, y]?.ToString();
                if ((name == "unitClass" ? cell : FieldReader.Clean(cell)) is { Length: > 0 } value)
                {
                    cells[name] = value;
                }
            }

            result[id] = cells;
        }

        return result;
    }

    /// <summary>A list the editor makes itself, its labels from the export's World Editor strings.</summary>
    private static ChoiceList OwnList(ExportTables tables, bool flags, (string Value, string Key)[] entries) =>
        new(flags, "string", entries.Select(entry => new ChoiceEntry(entry.Value, tables.Label(entry.Key), [])).ToList());

    /// <summary>
    /// The fields each upgrade effect uses, from <see cref="UpgradeEffects"/>: their data type, World Editor's label,
    /// and how they are typed (a whole number, a real, a choice of <paramref name="lists"/>, an ability or a unit).
    /// Empty when the folder has no such table.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<UpgradeEffectField>> EffectsOf(
        ExportTables tables,
        string folder,
        IReadOnlyDictionary<string, ChoiceList> lists)
    {
        var found = new Dictionary<string, List<UpgradeEffectField>>(StringComparer.Ordinal);
        foreach (var row in Rows(Path.Combine(folder, UpgradeEffects)))
        {
            if (row.GetValueOrDefault("effectID") is not { Length: > 0 } effect
                || row.GetValueOrDefault("dataType") is not { Length: > 0 } dataType)
            {
                continue;
            }

            var metaType = row.GetValueOrDefault("type", "");
            var (type, choice, listOf) = metaType switch
            {
                "int" => ("int", (string?)null, (string?)null),
                "real" or "unreal" => ("float", null, null),
                "abilCode" => ("objectRef", null, "Ability"),
                "unitCode" => ("objectRef", null, "Unit"),
                _ when lists.ContainsKey(metaType) => ("choice", metaType, null),
                _ => ("int", null, null),
            };
            var label = tables.Label(row.GetValueOrDefault("displayName", ""));
            if (!found.TryGetValue(effect, out var fields))
            {
                found[effect] = fields = [];
            }

            fields.Add(new UpgradeEffectField(dataType, label, type, choice, listOf));
        }

        return found.ToDictionary(
            pair => pair.Key, IReadOnlyList<UpgradeEffectField> (pair) => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>The rows of a table, each by its column names, the cells as written; empty when there is no file.</summary>
    private static List<Dictionary<string, string>> Rows(string path)
    {
        var result = new List<Dictionary<string, string>>();
        if (!File.Exists(path))
        {
            return result;
        }

        SylkTable table;
        using (var stream = File.OpenRead(path))
        {
            table = new SylkParser(keepText: true).Parse(stream);
        }

        var columns = new Dictionary<int, string>();
        for (var x = 0; x <= table.Columns; x++)
        {
            if (table[x, 0] is string name)
            {
                columns[x] = name;
            }
        }

        for (var y = 1; y <= table.Rows; y++)
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (x, name) in columns)
            {
                if (table[x, y]?.ToString()?.Trim() is { Length: > 0 } cell)
                {
                    cells[name] = cell;
                }
            }

            if (cells.Count > 0)
            {
                result.Add(cells);
            }
        }

        return result;
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
    /// The label of a <c>WESTRING_</c> key, or the empty string when the export has none; any other text as it is.
    /// </summary>
    private static string Resolved(ExportTables tables, string text)
    {
        var label = tables.Label(text);
        return label == text && text.StartsWith(KeyPrefix, StringComparison.Ordinal) ? "" : label;
    }

    /// <summary>A choice value's label, or its text when the export has no label for it.</summary>
    private static string LabelOf(ChoiceMember member) => member.Label.Length > 0 ? member.Label : member.Text;

    private static string Cell(MetaRow meta, string column) =>
        meta.Columns.TryGetValue(column, out var value) ? value.Trim() : "";

    private static List<string> Codes(string cell) =>
        cell.Split(',', '.').Select(part => part.Trim()).Where(code => code.Length > 0).ToList();
}
