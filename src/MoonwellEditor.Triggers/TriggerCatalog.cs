using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Extensions;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers;

/// <summary>
/// One piece of a function's sentence: text, or the place of an argument, whose name is then <paramref name="Text"/>.
/// </summary>
/// <param name="Text">The text, or the argument's name (<c>Time</c>).</param>
/// <param name="Argument">The argument's number from 0, or -1 for text.</param>
public sealed record SentencePart(string Text, int Argument = -1);

/// <summary>A category of functions: <c>TC_UNIT</c> is "Unit".</summary>
/// <param name="Key">The category's key in the game's file.</param>
/// <param name="Title">Its name.</param>
/// <param name="Icon">Its icon's game path.</param>
/// <param name="Shown">False when World Editor leaves the name out of a function's title.</param>
public sealed record CatalogCategory(string Key, string Title, string Icon, bool Shown);

/// <summary>A type of value: the types of variables and of a function's arguments.</summary>
/// <param name="Key">The type's key (<c>unitcode</c>).</param>
/// <param name="Title">Its name.</param>
/// <param name="Global">True when a variable may have this type.</param>
/// <param name="Comparable">True when a comparison may use it.</param>
/// <param name="Base">The script type it is made of, when another (<c>integer</c>); null for none.</param>
/// <param name="Default">A new variable's value for this type: script text, null for none.</param>
/// <param name="DefaultText">That value as World Editor shows it, null for none.</param>
public sealed record CatalogType(
    string Key, string Title, bool Global, bool Comparable, string? Base, string? Default, string? DefaultText);

/// <summary>A value a type offers by name: <c>Player00</c> is "Player 1 (Red)".</summary>
/// <param name="Key">The name the map's file holds.</param>
/// <param name="Type">The type it is a value of.</param>
/// <param name="Text">Its text.</param>
/// <param name="Code">Its script text.</param>
/// <param name="Version">The first game version that has it.</param>
public sealed record CatalogPreset(string Key, string Type, string Text, string Code, int Version);

/// <summary>A function a trigger can use.</summary>
/// <param name="Name">Its name in the map's file.</param>
/// <param name="Kind"><c>event</c>, <c>condition</c>, <c>action</c> or <c>call</c>.</param>
/// <param name="Category">Its category's key.</param>
/// <param name="Title">Its name in the editor's lists.</param>
/// <param name="Sentence">Its sentence.</param>
/// <param name="Arguments">Its arguments' types.</param>
/// <param name="Defaults">A new function's argument values, as the game's file has them; empty text for none.</param>
/// <param name="Returns">The type a call gives; null for the other kinds.</param>
/// <param name="InEvents">True when a call may be used in an event's arguments.</param>
/// <param name="Version">The first game version that has it.</param>
/// <param name="Hint">World Editor's hint; empty for none.</param>
public sealed record CatalogFunction(
    string Name,
    string Kind,
    string Category,
    string Title,
    IReadOnlyList<SentencePart> Sentence,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> Defaults,
    string? Returns,
    bool InEvents,
    int Version,
    string Hint);

/// <summary>
/// The game's list of trigger functions, read from its <c>UI\TriggerData.txt</c>, with their text from World Editor's
/// strings. The map's triggers can only be read with it: the file gives how many arguments each function has.
/// </summary>
public sealed partial class TriggerCatalog
{
    private const string None = "_";

    // The keys after a function's name that War3Net reads, by section.
    private static readonly Dictionary<string, HashSet<string>> FunctionKeys = new()
    {
        ["TriggerEvents"] = ["DisplayName", "Parameters", "Defaults", "Limits", "Category"],
        ["TriggerConditions"] = ["DisplayName", "Parameters", "Defaults", "Category", "UseWithAI", "AIDefaults"],
        ["TriggerActions"] = ["DisplayName", "Parameters", "Defaults", "Limits", "Category", "ScriptName"],
        ["TriggerCalls"] = ["DisplayName", "Parameters", "Defaults", "Limits", "Limites", "Category", "CATEGORY", "UseWithAI"],
    };

    private readonly Dictionary<string, CatalogFunction> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogFunction> _conditions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogFunction> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogFunction> _calls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogPreset> _presets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogType> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _strings;

    private TriggerCatalog(TriggerData data, Dictionary<string, string> strings)
    {
        Data = data;
        _strings = strings;
    }

    /// <summary>War3Net's copy of the file, which reads the map's triggers.</summary>
    public TriggerData Data { get; }

    /// <summary>The script's name of a call that has another name in the map's file, by that name.</summary>
    public IReadOnlyDictionary<string, string> CallScriptNames { get; private init; } = new Dictionary<string, string>();

    /// <summary>The categories, in the game's order.</summary>
    public IReadOnlyList<CatalogCategory> Categories { get; private set; } = [];

    /// <summary>The types, in the game's order, which is the order of the Variables dialog.</summary>
    public IReadOnlyList<CatalogType> Types { get; private set; } = [];

    /// <summary>The named values, in the game's order.</summary>
    public IReadOnlyList<CatalogPreset> Presets { get; private set; } = [];

    /// <summary>Every function, in the game's order: events, conditions, actions, then calls.</summary>
    public IReadOnlyList<CatalogFunction> Functions { get; private set; } = [];

    /// <summary>
    /// Reads the catalog through <paramref name="read"/>, which gives a game file by its path. Throws
    /// <see cref="InvalidDataException"/> when the game has no <c>UI\TriggerData.txt</c>.
    /// </summary>
    public static TriggerCatalog Read(Func<string, byte[]?> read)
    {
        var bytes = read(@"UI\TriggerData.txt")
            ?? throw new InvalidDataException(@"The game has no UI\TriggerData.txt.");
        var text = Encoding.UTF8.GetString(bytes);
        var orders = Orders(text);
        var scriptNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = new StringReader(Cleaned(text, scriptNames)).ReadTriggerData();
        var strings = Strings(read(@"UI\WorldEditStrings.txt"));
        foreach (var (key, value) in Strings(read(@"UI\WorldEditGameStrings.txt")))
        {
            strings.TryAdd(key, value);
        }

        var catalog = new TriggerCatalog(data, strings) { CallScriptNames = scriptNames };
        catalog.Fill(orders, Strings(read(@"UI\TriggerStrings.txt")));
        return catalog;
    }

    /// <summary>The event of this name; null when the game has none.</summary>
    public CatalogFunction? Event(string name) => _events.GetValueOrDefault(name);

    /// <summary>The condition of this name; null when the game has none.</summary>
    public CatalogFunction? Condition(string name) => _conditions.GetValueOrDefault(name);

    /// <summary>The action of this name; null when the game has none.</summary>
    public CatalogFunction? Action(string name) => _actions.GetValueOrDefault(name);

    /// <summary>The call of this name; null when the game has none.</summary>
    public CatalogFunction? Call(string name) => _calls.GetValueOrDefault(name);

    /// <summary>The function of this name and type; null when the game has none.</summary>
    public CatalogFunction? Function(TriggerFunctionType type, string name) => type switch
    {
        TriggerFunctionType.Event => Event(name),
        TriggerFunctionType.Condition => Condition(name),
        TriggerFunctionType.Action => Action(name),
        TriggerFunctionType.Call => Call(name),
        _ => null,
    };

    /// <summary>The named value of this key; null when the game has none.</summary>
    public CatalogPreset? Preset(string key) => _presets.GetValueOrDefault(key);

    /// <summary>The type of this key; null when the game has none.</summary>
    public CatalogType? Type(string key) => _types.GetValueOrDefault(key);

    /// <summary>A text as World Editor shows it: a <c>WESTRING_</c> key as its string, any other as it is.</summary>
    public string Label(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return _strings.TryGetValue(text, out var found) ? found : Unquoted(text);
    }

    // The keys of each section in the order of the file; War3Net's dictionaries have no order.
    private static Dictionary<string, List<string>> Orders(string text)
    {
        var orders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? section = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                section = [];
                orders[end > 0 ? line[1..end] : line] = section;
                continue;
            }

            var equals = line.IndexOf('=');
            if (section != null && equals > 0 && !line.StartsWith('_') && !line.StartsWith("//", StringComparison.Ordinal))
            {
                section.Add(line[..equals]);
            }
        }

        return orders;
    }

    // War3Net's reader stops at lines the game's own file has: a value listed twice (UnitOrderChallengingCall), a line
    // with no value (_BlzSetHDWaterMinOpacityBJ_Defaults0), a key of another function's name, and a call's ScriptName,
    // which it does not know. Those lines are left out; the calls' script names are kept in scriptNames.
    private static string Cleaned(string text, Dictionary<string, string> scriptNames)
    {
        var output = new StringBuilder(text.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var section = "";
        var function = "";
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('['))
            {
                seen.Clear();
                section = line.TrimEnd('\r').Trim('[', ']');
                function = "";
                output.Append(line).Append('\n');
                continue;
            }

            if (Keep(line))
            {
                output.Append(Corrected(line)).Append('\n');
            }
        }

        return output.ToString();

        // Three calls in the game's file say 2 where War3Net reads 0 or 1 (UnitItemInEquipmentSlotBJ=0,2,item,...), and
        // two actions end in a comma, which War3Net takes for one more argument (AllowHeroGlowOnAllUnitsBJ=1,).
        string Corrected(string line)
        {
            if (!FunctionKeys.ContainsKey(section) || line.StartsWith('_') || line.StartsWith("//", StringComparison.Ordinal))
            {
                return line;
            }

            line = line.TrimEnd('\r').TrimEnd(',');
            if (section != "TriggerCalls")
            {
                return line;
            }

            var fields = line.Split(',');
            if (fields.Length > 1 && fields[1] is not ("0" or "1"))
            {
                fields[1] = "1";
            }

            return string.Join(',', fields);
        }

        bool Keep(string line)
        {
            if (line.StartsWith("//", StringComparison.Ordinal) || line.Trim().Length == 0)
            {
                return true;
            }

            if (section == "TriggerParams")
            {
                return seen.Add(line[..Math.Max(line.IndexOf('='), 0)]);
            }

            if (!FunctionKeys.TryGetValue(section, out var keys))
            {
                return true;
            }

            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                equals = section == "TriggerCalls" ? line.IndexOf('-') : -1;
            }

            if (equals <= 0)
            {
                return false;
            }

            var key = line[..equals];
            if (!key.StartsWith('_'))
            {
                function = key;
                return true;
            }

            if (key == "_" + function && section == "TriggerCalls")
            {
                return true;
            }

            var prefix = $"_{function}_";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = key[prefix.Length..];
            if (suffix == "ScriptName" && section == "TriggerCalls")
            {
                scriptNames[function] = line[(equals + 1)..].Trim();
                return false;
            }

            return keys.Contains(suffix);
        }
    }

    // Each KEY=text line of a strings file; the first of a key counts, whatever its section.
    private static Dictionary<string, string> Strings(byte[]? bytes)
    {
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bytes == null)
        {
            return strings;
        }

        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            var equals = line.IndexOf('=');
            if (equals > 0 && !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith('['))
            {
                strings.TryAdd(line[..equals].Trim(), Unquoted(line[(equals + 1)..].Trim()));
            }
        }

        return strings;
    }

    // A text without its quotes; the game's file leaves some closing quotes out.
    private static string Unquoted(string text) => text.StartsWith('"') ? text[1..].TrimEnd('"') : text;

    private void Fill(Dictionary<string, List<string>> orders, Dictionary<string, string> hints)
    {
        List<string> Order(string section) => orders.GetValueOrDefault(section) ?? [];

        Categories = Order("TriggerCategories")
            .Where(Data.TriggerCategories.ContainsKey)
            .Select(key => Data.TriggerCategories[key])
            .Select(category => new CatalogCategory(
                category.CategoryIdentifier,
                Label(category.DisplayText),
                Path.HasExtension(category.IconImageFile) ? category.IconImageFile : category.IconImageFile + ".blp",
                !category.HideCategoryName))
            .ToList();

        Types = Order("TriggerTypes")
            .Where(Data.TriggerTypes.ContainsKey)
            .Select(key => Data.TriggerTypes[key])
            .Select(type =>
            {
                var fallback = Data.TriggerTypeDefaults.GetValueOrDefault(type.TypeName);
                return new CatalogType(
                    type.TypeName,
                    Label(type.DisplayText),
                    type.UsableAsGlobalVariable,
                    type.UsableWithComparisonOperators,
                    type.BaseType,
                    fallback?.ScriptText,
                    fallback == null ? null : Label(fallback.DisplayText ?? fallback.ScriptText));
            })
            .ToList();
        foreach (var type in Types)
        {
            _types[type.Key] = type;
        }

        Presets = Order("TriggerParams")
            .Distinct(StringComparer.Ordinal)
            .Where(Data.TriggerParams.ContainsKey)
            .Select(key => Data.TriggerParams[key])
            .Select(preset => new CatalogPreset(
                preset.ParameterName, preset.VariableType, Label(preset.DisplayText), preset.ScriptText, preset.GameVersion))
            .ToList();
        foreach (var preset in Presets)
        {
            _presets[preset.Key] = preset;
        }

        var functions = new List<CatalogFunction>();
        foreach (var key in Order("TriggerEvents"))
        {
            var each = Data.TriggerEvents[key];
            Add(_events, new CatalogFunction(
                key, "event", each.Category ?? "", Title(each.DisplayName), Sentence(each.Parameters, each.DisplayName),
                each.ArgumentTypes, Defaults(each.Defaults), null, false, each.GameVersion, Hint(key)));
        }

        foreach (var key in Order("TriggerConditions"))
        {
            var each = Data.TriggerConditions[key];
            Add(_conditions, new CatalogFunction(
                key, "condition", each.Category ?? "", Title(each.DisplayName), Sentence(each.Parameters, each.DisplayName),
                each.ArgumentTypes, Defaults(each.Defaults), null, false, each.GameVersion, Hint(key)));
        }

        foreach (var key in Order("TriggerActions"))
        {
            var each = Data.TriggerActions[key];
            Add(_actions, new CatalogFunction(
                key, "action", each.Category ?? "", Title(each.DisplayName), Sentence(each.Parameters, each.DisplayName),
                each.ArgumentTypes, Defaults(each.Defaults), null, false, each.GameVersion, Hint(key)));
        }

        foreach (var key in Order("TriggerCalls"))
        {
            var each = Data.TriggerCalls[key];
            Add(_calls, new CatalogFunction(
                key, "call", each.Category ?? "", Title(each.DisplayName), Sentence(each.Parameters, each.DisplayName),
                each.ArgumentTypes, Defaults(each.Defaults), each.ReturnType, each.UsableInEvents, each.GameVersion, Hint(key)));
        }

        Functions = functions;
        return;

        void Add(Dictionary<string, CatalogFunction> into, CatalogFunction function)
        {
            into[function.Name] = function;
            functions.Add(function);
        }

        string Hint(string key) => hints.GetValueOrDefault(key + "Hint", "");
    }

    private string Title(string? title) => Label(title);

    private static IReadOnlyList<string> Defaults(ImmutableArray<string> defaults) =>
        defaults.IsDefault ? [] : defaults.Select(value => value == None ? "" : value).ToList();

    private static IReadOnlyList<string> Defaults(ImmutableArray<string>? defaults) =>
        defaults == null ? [] : Defaults(defaults.Value);

    // A sentence: arguments start with ~, and what lies between them is text in quotes. The game's file drops quotes and
    // commas here and there, so an argument is found first and the text is what is left. A few sentences still start
    // with the function's name, as the old file's did: that piece is not shown.
    private List<SentencePart> Sentence(string? text, string? title)
    {
        var parts = new List<SentencePart>();
        text ??= "";
        var position = 0;
        var argument = 0;
        var first = true;
        foreach (Match match in ArgumentPattern().Matches(text))
        {
            AddText(text[position..match.Index]);
            parts.Add(new SentencePart(match.Value[1..], argument++));
            position = match.Index + match.Length;
        }

        AddText(text[position..]);
        return parts;

        void AddText(string gap)
        {
            var pieces = gap.Trim(',', '`').Split("\",\"");
            for (var i = 0; i < pieces.Length; i++)
            {
                var piece = Unquoted(pieces[i].TrimStart('"')).TrimEnd('"');
                var titleFirst = first && i == 0 && pieces.Length + argument > 1 && piece == Title(title);
                if (piece.Length > 0 && !titleFirst)
                {
                    parts.Add(new SentencePart(piece));
                }
            }

            first = false;
        }
    }

    [GeneratedRegex("""~[^,"`]+""")]
    private static partial Regex ArgumentPattern();
}
