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
    private static readonly string[] UnitUiColumns = ["campaign", "special", "unitClass"];

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
        var kinds = new Dictionary<ObjectKind, KindData>();
        foreach (var kind in Enum.GetValues<Kind>())
        {
            var fields = Fields.WithStandardNegatives(fieldsOf[kind], objectsOf[kind])
                .Select(field => field.Choice == null ? field : field with { Choice = choices[field.Choice.Type] })
                .ToList();
            kinds[KindOf(kind)] = KindDataOf(tables, kind, fields, objectsOf[kind], kind == Kind.Unit ? UnitUiOf(folder) : []);
        }

        var lists = new SortedDictionary<string, ChoiceList>(StringComparer.Ordinal);
        foreach (var (type, choice) in choices)
        {
            lists[type] = new ChoiceList(
                choice.Flags,
                choice.Store,
                choice.Members.Select(member => new ChoiceEntry(member.Text, LabelOf(member), member.Alternates)).ToList());
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
        Dictionary<string, Dictionary<string, string>> unitUi)
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
        var baseRows = Index(tables.Objects(kind, null));
        var setRows = new Dictionary<string, Dictionary<string, ObjectRows>>(StringComparer.Ordinal);
        var result = new List<StandardObject>(objects.Count);
        foreach (var obj in objects)
        {
            var rows = PrimaryRows(tables, kind, obj, baseRows, setRows);
            result.Add(Object(tables, objectKind, levelField, obj, rows, infos, readers, unitUi.GetValueOrDefault(obj.Id)));
        }

        return new KindData(objectKind, levelField, infos, result);
    }

    private static FieldInfo Info(Field field, MetaRow meta)
    {
        // An ability's Effects (aeff) hold effect ids, which are buffs of the buff table (Xclf), as its Buffs (abuf)
        // hold buff ids; Moonwell's reader types only buffList as a list, so the editor types effectList the same.
        var effects = Cell(meta, "type") == "effectList";
        var type = effects ? "ObjectList" : field.Type.ToString();
        return new FieldInfo(
            field.Id,
            field.Rules.Label ?? field.Name,
            Cell(meta, "category"),
            Cell(meta, "sort"),
            char.ToLowerInvariant(type[0]) + type.Substring(1),
            field.Store,
            effects ? "Buff" : field.ListOf,
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
            field.Choice?.Type);
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
        var hidden = kind == ObjectKind.Ability && FieldReader.Clean(row.Get("useInEditor", "de")?.Value) == "0";
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
            new Dictionary<string, IReadOnlyDictionary<string, string>>());
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
