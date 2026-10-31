using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using MoonwellEditor.Files;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers;

/// <summary>An edit that cannot be made; the message says why, as the user sees it.</summary>
/// <param name="message">Why.</param>
public sealed class TriggerEditException(string message) : Exception(message);

/// <summary>A parameter's new value.</summary>
/// <param name="Kind"><c>preset</c>, <c>variable</c>, <c>function</c>, <c>value</c>, or <c>none</c> for no value.</param>
/// <param name="Value">The preset's key, the variable's name, the function's name, or the value as text.</param>
public sealed record ParamInput(string Kind, string Value);

/// <summary>A variable's fields.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its type's key.</param>
/// <param name="IsArray">True for an array.</param>
/// <param name="Size">An array's size; 1 otherwise.</param>
/// <param name="Initial">Its first value as the map's file holds it; empty for none.</param>
public sealed record VariableInput(string Name, string Type, bool IsArray, int Size, string Initial);

/// <summary>
/// The document's state at one moment. An edit makes a new one and a snapshot shares the items that did not change,
/// so an undo step costs a list of references.
/// </summary>
public sealed class TriggerSnapshot
{
    internal TriggerItem[] Items { get; init; } = [];

    internal VariableDefinition[] Variables { get; init; } = [];

    internal KeyValuePair<TriggerItemType, int>[] Counts { get; init; } = [];

    internal ImmutableDictionary<int, CustomTextTrigger> Code { get; init; } = ImmutableDictionary<int, CustomTextTrigger>.Empty;

    internal string HeaderComment { get; init; } = "";

    internal CustomTextTrigger? HeaderCode { get; init; }

    internal bool Touched { get; init; }
}

/// <summary>The edits.</summary>
public sealed partial class TriggerDocument
{
    // The first free number of a new item's name starts here, as World Editor's "Untitled Trigger 001" does.
    private const int FirstNumber = 1;

    // True once an edit was made, so a file the map did not have is written.
    private bool _touched;

    // What CopyFunction kept.
    private TriggerFunction? _clipboard;

    /// <summary>The state now, for undo.</summary>
    public TriggerSnapshot Snapshot() => new()
    {
        Items = [.. _triggers.TriggerItems],
        Variables = [.. _triggers.Variables],
        Counts = [.. _triggers.TriggerItemCounts],
        Code = _code,
        HeaderComment = _customText.GlobalCustomScriptComment,
        HeaderCode = _customText.GlobalCustomScriptCode,
        Touched = _touched,
    };

    /// <summary>Goes back or forward to a state.</summary>
    public void Restore(TriggerSnapshot snapshot)
    {
        _triggers.TriggerItems.Clear();
        _triggers.TriggerItems.AddRange(snapshot.Items);
        _triggers.Variables.Clear();
        _triggers.Variables.AddRange(snapshot.Variables);
        _triggers.TriggerItemCounts.Clear();
        foreach (var (type, count) in snapshot.Counts)
        {
            _triggers.TriggerItemCounts[type] = count;
        }

        _code = snapshot.Code;
        _customText.GlobalCustomScriptComment = snapshot.HeaderComment;
        _customText.GlobalCustomScriptCode = snapshot.HeaderCode!;
        _touched = snapshot.Touched;
    }

    /// <summary>
    /// Adds a category, trigger, script or comment (<paramref name="kind"/> as in <see cref="TriggerTreeItem.Kind"/>) to
    /// the category <paramref name="parent"/>, after its other items, with the first free name of its kind. Returns its id.
    /// </summary>
    public int Add(string kind, int parent)
    {
        Editable();
        NeedRoot();
        var (item, name) = kind switch
        {
            "category" => (
                new TriggerCategoryDefinition { Id = NextId(TriggerItemType.Category), IsExpanded = true },
                Texts.Get("app.triggers.new.category")),
            "trigger" => ((TriggerItem)NewDefinition(TriggerItemType.Gui), Texts.Get("app.triggers.new.trigger")),
            "script" => (NewDefinition(TriggerItemType.Script), Texts.Get("app.triggers.new.script")),
            "comment" => (NewDefinition(TriggerItemType.Comment), Texts.Get("app.triggers.new.comment")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind."),
        };
        parent = kind == "category" ? Category(parent) : InCategory(parent);
        item.Name = FreeName(name);
        item.ParentId = parent;
        Insert(item, parent, int.MaxValue);
        Recount();
        return item.Id;
    }

    /// <summary>Adds a variable to the category <paramref name="parent"/>. Returns its id.</summary>
    public int AddVariable(VariableInput input, int parent)
    {
        Editable();
        NeedRoot();
        CheckVariable(input, null);
        parent = InCategory(parent);
        var id = NextId(TriggerItemType.Variable);
        var item = new TriggerVariableDefinition { Id = id, ParentId = parent, Name = input.Name };
        _triggers.Variables.Add(Fill(new VariableDefinition { Unk = 1, Id = id, ParentId = item.ParentId }, input));
        Insert(item, parent, int.MaxValue);
        Recount();
        return id;
    }

    /// <summary>Changes a variable; a new name changes the parameters that use it too.</summary>
    public void SetVariable(int id, VariableInput input)
    {
        Editable();
        var old = _triggers.Variables.FirstOrDefault(variable => variable.Id == id)
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No such variable.");
        CheckVariable(input, old.Name);
        if (old.Type != input.Type)
        {
            ClearUses(old.Name);
        }
        else if (old.IsArray != input.IsArray)
        {
            ChangeUses(old.Name, parameter => parameter.ArrayIndexer = input.IsArray
                ? new TriggerFunctionParameter { Type = TriggerFunctionParameterType.String, Value = "0" }
                : null);
        }

        var copy = Fill(CloneVariable(old), input);
        _triggers.Variables[_triggers.Variables.IndexOf(old)] = copy;
        var index = _triggers.TriggerItems.FindIndex(item => item is TriggerVariableDefinition && item.Id == id);
        if (index >= 0)
        {
            var item = (TriggerVariableDefinition)_triggers.TriggerItems[index];
            _triggers.TriggerItems[index] = new TriggerVariableDefinition
            {
                Id = item.Id, ParentId = item.ParentId, Name = input.Name,
            };
        }

        if (old.Name != input.Name)
        {
            RenameUses(old.Name, input.Name);
        }

        _touched = true;
    }

    /// <summary>
    /// Renames a category, trigger, script or comment. A trigger's parameters that name it (<c>gg_trg_</c>) follow.
    /// </summary>
    public void Rename(int id, string name)
    {
        Editable();
        var old = Item(id);
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new TriggerEditException(Texts.Get("app.triggers.emptyName"));
        }

        // Two triggers may not end up with one name in the script: "A B" and "A_B" are the same there.
        if (old is TriggerDefinition { Type: not TriggerItemType.Comment } or TriggerCategoryDefinition)
        {
            if (Live().Any(item => item.Id != id && item.GetType() == old.GetType() && item.Type != TriggerItemType.Comment
                && string.Equals(TriggerJass.Identifier(item.Name), TriggerJass.Identifier(name), StringComparison.OrdinalIgnoreCase)))
            {
                throw new TriggerEditException(Texts.Get("app.triggers.nameTaken", ("name", name)));
            }
        }

        var copy = Copied(id);
        var before = copy.Name;
        copy.Name = name;
        if (copy is TriggerDefinition { Type: TriggerItemType.Gui } trigger)
        {
            RenameUses(ScriptName(before), ScriptName(name));
            if (trigger.IsCustomTextTrigger && CodeOf(id) is { } code)
            {
                _code = _code.SetItem(id, Stored(RenamedInCode(code, before, name)));
            }
        }

        _touched = true;
    }

    /// <summary>Sets a trigger's fields; a null one stays as it is.</summary>
    public void SetOptions(int id, bool? enabled, bool? initiallyOn, bool? runOnInit, string? description)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        copy.IsEnabled = enabled ?? copy.IsEnabled;
        copy.IsInitiallyOn = initiallyOn ?? copy.IsInitiallyOn;
        copy.RunOnMapInit = runOnInit ?? copy.RunOnMapInit;
        copy.Description = description ?? copy.Description;
        _touched = true;
    }

    /// <summary>Sets a custom text trigger's text.</summary>
    public void SetCode(int id, string code)
    {
        Editable();
        _ = Item(id);
        _code = _code.SetItem(id, Stored(code));
        _touched = true;
    }

    /// <summary>Sets the map's own script text and its comment.</summary>
    public void SetHeader(string comment, string code)
    {
        Editable();
        _customText.GlobalCustomScriptComment = comment;
        _customText.GlobalCustomScriptCode = Stored(code);
        _touched = true;
    }

    /// <summary>
    /// Deletes an item and, for a category, what is in it. Parameters that name a deleted variable or trigger lose
    /// their value.
    /// </summary>
    public void Delete(int id)
    {
        Editable();
        var item = Item(id);
        if (item.Type == TriggerItemType.RootCategory)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "The map cannot be deleted.");
        }

        var gone = Subtree(id).ToList();
        var ids = gone.Select(each => each.Id).ToHashSet();
        _triggers.TriggerItems.RemoveAll(each => each is not DeletedTriggerItem && ids.Contains(each.Id));
        foreach (var each in gone)
        {
            _code = _code.Remove(each.Id);
            switch (each)
            {
                case TriggerVariableDefinition variable:
                    _triggers.Variables.RemoveAll(definition => definition.Id == variable.Id);
                    ClearUses(variable.Name);
                    break;
                case TriggerDefinition { Type: TriggerItemType.Gui }:
                    ClearUses(ScriptName(each.Name));
                    break;
            }
        }

        Recount();
    }

    /// <summary>
    /// Moves an item, with what is in it, to place <paramref name="index"/> among the items of the category
    /// <paramref name="parent"/>. Refused when that category is in the item.
    /// </summary>
    public void Move(int id, int parent, int index)
    {
        Editable();
        var moved = Subtree(id).ToList();
        if (moved.Any(item => item.Id == parent))
        {
            throw new TriggerEditException(Texts.Get("app.triggers.moveIntoItself"));
        }

        var head = Copied(id);
        moved[0] = head;
        foreach (var each in moved)
        {
            _triggers.TriggerItems.Remove(each);
        }

        head.ParentId = Category(parent);
        InsertAll(moved, parent, index);
        if (head is TriggerVariableDefinition)
        {
            var at = _triggers.Variables.FindIndex(variable => variable.Id == id);
            var copy = CloneVariable(_triggers.Variables[at]);
            copy.ParentId = head.ParentId;
            _triggers.Variables[at] = copy;
        }

        _touched = true;
    }

    /// <summary>
    /// Adds a function with the game's default values to a trigger, or to a block of the function at
    /// <paramref name="parentPath"/>, at place <paramref name="index"/> among those of its kind or block.
    /// Returns its path.
    /// </summary>
    public string AddFunction(int id, string? parentPath, int branch, int index, string kind, string name)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        var type = TypeOf(kind);
        var function = New(type, name, parentPath == null ? null : branch);
        return Place(copy, parentPath, branch, index, function);
    }

    /// <summary>
    /// Keeps a copy of the function at <paramref name="path"/> for <see cref="PasteFunction"/>; what happens to the
    /// trigger afterwards does not change it. It is no edit.
    /// </summary>
    public void CopyFunction(int id, string path) =>
        _clipboard = Clone(Walk((TriggerDefinition)Item(id), path).Function);

    /// <summary>Adds what <see cref="CopyFunction"/> kept at a place as in <see cref="AddFunction"/>. Returns its path.</summary>
    public string PasteFunction(int id, string? parentPath, int branch, int index)
    {
        Editable();
        if (_clipboard == null)
        {
            throw new InvalidOperationException("Nothing was copied.");
        }

        var function = Clone(_clipboard);
        function.Branch = parentPath == null ? null : branch;
        return Place((TriggerDefinition)Copied(id), parentPath, branch, index, function);
    }

    /// <summary>
    /// Puts another function in the place of the one at <paramref name="path"/>, with the game's default values; what
    /// was in it goes, as its blocks may not exist in the new one.
    /// </summary>
    public void ChangeFunction(int id, string path, string name)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        var (list, index, function, _) = Walk(copy, path);
        list[index] = New(function.Type, name, function.Branch);
    }

    /// <summary>Deletes the function at <paramref name="path"/>.</summary>
    public void DeleteFunction(int id, string path)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        var (list, index, _, _) = Walk(copy, path);
        list.RemoveAt(index);
    }

    /// <summary>Turns the function at <paramref name="path"/> on or off.</summary>
    public void EnableFunction(int id, string path, bool enabled)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        Walk(copy, path).Function.IsEnabled = enabled;
    }

    /// <summary>
    /// Moves the function at <paramref name="path"/> to place <paramref name="index"/> among those of its kind or
    /// block. Returns its new path.
    /// </summary>
    public string MoveFunction(int id, string path, int index)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        var (list, at, function, _) = Walk(copy, path);
        var group = list.Where(each => each.Type == function.Type && each.Branch == function.Branch).ToList();
        index = Math.Clamp(index, 0, group.Count - 1);
        list.RemoveAt(at);
        group.Remove(function);
        var target = index < group.Count ? list.IndexOf(group[index]) : list.IndexOf(group[^1]) + 1;
        list.Insert(target, function);
        return path[..(path.LastIndexOf('/') + 1)] + (path.Contains('/') ? "c" : "") + target;
    }

    /// <summary>Sets the parameter at <paramref name="path"/>.</summary>
    public void SetParameter(int id, string path, ParamInput input)
    {
        Editable();
        var copy = (TriggerDefinition)Copied(id);
        var (_, _, function, parameter) = Walk(copy, path);
        if (parameter == null)
        {
            throw new ArgumentOutOfRangeException(nameof(path), path, "Not a parameter.");
        }

        var type = path.EndsWith("/i", StringComparison.Ordinal) ? "integer" : TypeOfParameter(function, parameter);
        var made = Made(input, type);
        var isVariableOfSet = function.Name == "SetVariable" && function.Parameters[0] == parameter;
        parameter.Type = made.Type;
        parameter.Value = made.Value;
        parameter.Function = made.Function;
        parameter.ArrayIndexer = made.ArrayIndexer;

        // Set Variable's value has the variable's type, so another type of variable leaves it without one.
        if (isVariableOfSet && TypeOfVariable(made.Value) != type)
        {
            function.Parameters[1] = new TriggerFunctionParameter { Type = TriggerFunctionParameterType.Undefined, Value = "" };
        }
    }

    /// <summary>The placed units, items and destructables the enabled triggers name.</summary>
    internal IEnumerable<string> PlacedNames() => _triggers.TriggerItems
        .OfType<TriggerDefinition>()
        .Where(definition => definition.IsEnabled)
        .SelectMany(definition => definition.Functions)
        .SelectMany(Named);

    /// <summary>The name a trigger has in the script: <c>gg_trg_</c> and its name with spaces as underscores.</summary>
    internal static string ScriptName(string name) => "gg_trg_" + TriggerJass.Identifier(name);

    /// <summary>
    /// Whether a type of a new variable's name is fine: a letter, then letters, digits and underscores, not ending in
    /// an underscore.
    /// </summary>
    [GeneratedRegex("^[A-Za-z](?:[A-Za-z0-9_]*[A-Za-z0-9])?$")]
    private static partial Regex VariableName();

    private static TriggerFunctionType TypeOf(string kind) => kind switch
    {
        "event" => TriggerFunctionType.Event,
        "condition" => TriggerFunctionType.Condition,
        "action" => TriggerFunctionType.Action,
        "call" => TriggerFunctionType.Call,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind."),
    };

    // The type of a function that fills an argument: callbacks are actions and conditions, the rest are calls.
    private static TriggerFunctionType TypeForArgument(string type) => type switch
    {
        "code" => TriggerFunctionType.Action,
        "boolexpr" or "boolcall" => TriggerFunctionType.Condition,
        "eventcall" => TriggerFunctionType.Event,
        _ => TriggerFunctionType.Call,
    };

    private static IEnumerable<string> Named(TriggerFunction function)
    {
        if (!function.IsEnabled)
        {
            yield break;
        }

        foreach (var name in function.Parameters.SelectMany(Named).Concat(function.ChildFunctions.SelectMany(Named)))
        {
            yield return name;
        }
    }

    private static IEnumerable<string> Named(TriggerFunctionParameter parameter)
    {
        if (parameter.Type == TriggerFunctionParameterType.Variable
            && (parameter.Value.StartsWith("gg_unit_", StringComparison.Ordinal)
                || parameter.Value.StartsWith("gg_item_", StringComparison.Ordinal)
                || parameter.Value.StartsWith("gg_dest_", StringComparison.Ordinal)))
        {
            yield return parameter.Value;
        }

        foreach (var name in (parameter.Function == null ? [] : Named(parameter.Function))
            .Concat(parameter.ArrayIndexer == null ? [] : Named(parameter.ArrayIndexer)))
        {
            yield return name;
        }
    }

    private static CustomTextTrigger Stored(string code)
    {
        var text = code.Replace("\0", "", StringComparison.Ordinal);
        return new CustomTextTrigger { Code = text.Length == 0 ? "" : text + Terminator };
    }

    // A trigger converted to text holds its own name: the script needs its InitTrig under the new name.
    private static string RenamedInCode(string code, string from, string to)
    {
        var (oldName, newName) = (TriggerJass.Identifier(from), TriggerJass.Identifier(to));
        foreach (var prefix in new[] { "InitTrig_", "gg_trg_", "Trig_" })
        {
            code = Regex.Replace(code, $@"\b{prefix}{Regex.Escape(oldName)}(?=\b|_)", prefix + newName.Replace("$", "$$", StringComparison.Ordinal));
        }

        return code;
    }

    private static TriggerFunction Clone(TriggerFunction function)
    {
        var copy = new TriggerFunction
        {
            Type = function.Type,
            Branch = function.Branch,
            Name = function.Name,
            IsEnabled = function.IsEnabled,
        };
        copy.Parameters.AddRange(function.Parameters.Select(Clone));
        copy.ChildFunctions.AddRange(function.ChildFunctions.Select(Clone));
        return copy;
    }

    private static TriggerFunctionParameter Clone(TriggerFunctionParameter parameter) => new()
    {
        Type = parameter.Type,
        Value = parameter.Value,
        Function = parameter.Function == null ? null : Clone(parameter.Function),
        ArrayIndexer = parameter.ArrayIndexer == null ? null : Clone(parameter.ArrayIndexer),
    };

    private static VariableDefinition CloneVariable(VariableDefinition variable) => new()
    {
        Name = variable.Name,
        Type = variable.Type,
        Unk = variable.Unk,
        IsArray = variable.IsArray,
        ArraySize = variable.ArraySize,
        IsInitialized = variable.IsInitialized,
        InitialValue = variable.InitialValue,
        Id = variable.Id,
        ParentId = variable.ParentId,
    };

    private static VariableDefinition Fill(VariableDefinition variable, VariableInput input)
    {
        variable.Name = input.Name;
        variable.Type = input.Type;
        variable.IsArray = input.IsArray;
        variable.ArraySize = input.IsArray ? Math.Max(input.Size, 1) : 1;
        variable.IsInitialized = input.Initial.Length > 0;
        variable.InitialValue = input.Initial;
        return variable;
    }

    private static TriggerItem Copy(TriggerItem item) => item switch
    {
        TriggerCategoryDefinition category => new TriggerCategoryDefinition(category.Type)
        {
            Id = category.Id,
            ParentId = category.ParentId,
            Name = category.Name,
            IsComment = category.IsComment,
            IsExpanded = category.IsExpanded,
        },
        TriggerVariableDefinition variable => new TriggerVariableDefinition(variable.Type)
        {
            Id = variable.Id, ParentId = variable.ParentId, Name = variable.Name,
        },
        TriggerDefinition definition => Copy(definition),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, "A deleted item cannot be copied."),
    };

    private static TriggerDefinition Copy(TriggerDefinition definition)
    {
        var copy = new TriggerDefinition(definition.Type)
        {
            Id = definition.Id,
            ParentId = definition.ParentId,
            Name = definition.Name,
            Description = definition.Description,
            IsComment = definition.IsComment,
            IsEnabled = definition.IsEnabled,
            IsCustomTextTrigger = definition.IsCustomTextTrigger,
            IsInitiallyOn = definition.IsInitiallyOn,
            RunOnMapInit = definition.RunOnMapInit,
        };
        copy.Functions.AddRange(definition.Functions.Select(Clone));
        return copy;
    }

    private void Editable()
    {
        if (ReadOnly)
        {
            throw new TriggerEditException(Problem ?? Texts.Get("app.triggers.readOnly"));
        }
    }

    // A map with no trigger file gets the root item World Editor's files start with.
    private void NeedRoot()
    {
        if (_triggers.TriggerItems.Any(item => item.Type == TriggerItemType.RootCategory))
        {
            return;
        }

        _triggers.TriggerItems.Insert(0, new TriggerCategoryDefinition(TriggerItemType.RootCategory)
        {
            Id = 0, ParentId = -1, Name = "", IsExpanded = true,
        });
    }

    private TriggerItem Item(int id) => Live().FirstOrDefault(item => item.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No such item.");

    // Puts a copy of the item in its place and returns it: the old one stays as a snapshot has it.
    private TriggerItem Copied(int id)
    {
        var old = Item(id);
        var copy = Copy(old);
        _triggers.TriggerItems[_triggers.TriggerItems.IndexOf(old)] = copy;
        _touched = true;
        return copy;
    }

    private int Category(int parent)
    {
        if (parent is -1 or NoRoot)
        {
            return 0;
        }

        return Item(parent) is TriggerCategoryDefinition
            ? parent
            : throw new ArgumentOutOfRangeException(nameof(parent), parent, "Not a category.");
    }

    // In World Editor's files only a category is directly under the root (every map of the install). For anything else,
    // the root means the first category, made when the map has none.
    private int InCategory(int parent)
    {
        var category = Category(parent);
        if (category != 0)
        {
            return category;
        }

        var first = Live().OfType<TriggerCategoryDefinition>()
            .FirstOrDefault(each => each.Type == TriggerItemType.Category);
        return first?.Id ?? Add("category", 0);
    }

    private TriggerDefinition NewDefinition(TriggerItemType type) => new(type)
    {
        Id = NextId(type),
        Description = "",
        IsComment = type == TriggerItemType.Comment,
        IsEnabled = true,
        IsCustomTextTrigger = type == TriggerItemType.Script,
        IsInitiallyOn = true,
    };

    // The item's type in the high byte, then the first number no item of the type has, deleted ones included.
    private int NextId(TriggerItemType type)
    {
        var typeId = (int)Enum.Parse<TriggerItemTypeId>(type.ToString());
        var used = _triggers.TriggerItems
            .Where(item => item.Type == type)
            .Select(item => item.Id & 0xFFFFFF)
            .ToHashSet();
        var number = 0;
        while (!used.Add(number))
        {
            number++;
        }

        return (typeId << 24) | number;
    }

    private string FreeName(string name)
    {
        var taken = Live().Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = FirstNumber;
        while (!taken.Add($"{name} {number:D3}"))
        {
            number++;
        }

        return $"{name} {number:D3}";
    }

    // The item, then what is in it, in the file's order.
    private IEnumerable<TriggerItem> Subtree(int id)
    {
        var all = Live().ToList();
        var ids = new HashSet<int> { id };
        foreach (var item in all)
        {
            if (item.Id == id)
            {
                yield return item;
                break;
            }
        }

        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var item in all)
            {
                if (ids.Contains(item.ParentId) && ids.Add(item.Id))
                {
                    grew = true;
                    yield return item;
                }
            }
        }
    }

    private void Insert(TriggerItem item, int parent, int index)
    {
        InsertAll([item], parent, index);
        _touched = true;
    }

    // Puts items where place index among the children of parent is: before that child, or after all of them and what
    // is in them, so the file stays in tree order.
    private void InsertAll(List<TriggerItem> items, int parent, int index)
    {
        var parentId = parent == -1 ? 0 : parent;
        var children = Live().Where(item => item.ParentId == parentId && item.Id != parentId).ToList();
        var list = _triggers.TriggerItems;
        int position;
        if (index < children.Count)
        {
            position = list.IndexOf(children[Math.Max(index, 0)]);
        }
        else if (children.Count > 0)
        {
            position = list.IndexOf(Subtree(children[^1].Id).Last()) + 1;
        }
        else
        {
            position = list.FindIndex(item => item.Id == parentId && item is not DeletedTriggerItem) + 1;
        }

        list.InsertRange(position, items);
    }

    // A type's count in the file is its live items and its deleted ids.
    private void Recount()
    {
        foreach (var type in Enum.GetValues<TriggerItemType>())
        {
            _triggers.TriggerItemCounts[type] = _triggers.TriggerItems.Count(item => item.Type == type);
        }

        _touched = true;
    }

    private void CheckVariable(VariableInput input, string? oldName)
    {
        if (!VariableName().IsMatch(input.Name))
        {
            throw new TriggerEditException(Texts.Get("app.triggers.badVariableName", ("name", input.Name)));
        }

        if (input.Name != oldName && _triggers.Variables.Any(variable =>
            string.Equals(variable.Name, input.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new TriggerEditException(Texts.Get("app.triggers.nameTaken", ("name", input.Name)));
        }

        var type = _catalog.Type(input.Type)
            ?? throw new ArgumentOutOfRangeException(nameof(input), input.Type, "No such type.");
        var script = type.Base ?? type.Key;
        if (input.Initial.Length == 0 || script == "string" || _catalog.Preset(input.Initial)?.Type == input.Type)
        {
            return;
        }

        CheckNumber(input.Initial, input.Type);
        var fine = input.Type switch
        {
            "integer" or "real" => true,
            "boolean" => input.Initial is "true" or "false",
            _ => script == "integer" && input.Initial.Length == 4,
        };
        if (!fine)
        {
            throw new TriggerEditException(Texts.Get("app.triggers.badInitial", ("value", input.Initial), ("type", type.Title)));
        }
    }

    private void RenameUses(string from, string to) => ChangeUses(from, parameter => parameter.Value = to);

    private void ClearUses(string name) => ChangeUses(name, parameter =>
    {
        parameter.Type = TriggerFunctionParameterType.Undefined;
        parameter.Value = "";
        parameter.ArrayIndexer = null;
    });

    // Changes each parameter that names a variable, in copies of the triggers that have one.
    private void ChangeUses(string name, Action<TriggerFunctionParameter> change)
    {
        var list = _triggers.TriggerItems;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not TriggerDefinition definition || !definition.Functions.Any(function => Uses(function, name)))
            {
                continue;
            }

            var copy = Copy(definition);
            foreach (var function in copy.Functions)
            {
                Apply(function, name, change);
            }

            list[i] = copy;
        }
    }

    private static bool Uses(TriggerFunction function, string name) =>
        function.Parameters.Any(parameter => Uses(parameter, name))
        || function.ChildFunctions.Any(child => Uses(child, name));

    private static bool Uses(TriggerFunctionParameter parameter, string name) =>
        (parameter.Type == TriggerFunctionParameterType.Variable && parameter.Value == name)
        || (parameter.Function != null && Uses(parameter.Function, name))
        || (parameter.ArrayIndexer != null && Uses(parameter.ArrayIndexer, name));

    private static void Apply(TriggerFunction function, string name, Action<TriggerFunctionParameter> change)
    {
        foreach (var parameter in function.Parameters)
        {
            Apply(parameter, name, change);
        }

        foreach (var child in function.ChildFunctions)
        {
            Apply(child, name, change);
        }
    }

    private static void Apply(TriggerFunctionParameter parameter, string name, Action<TriggerFunctionParameter> change)
    {
        if (parameter.Function != null)
        {
            Apply(parameter.Function, name, change);
        }

        if (parameter.ArrayIndexer != null)
        {
            Apply(parameter.ArrayIndexer, name, change);
        }

        if (parameter.Type == TriggerFunctionParameterType.Variable && parameter.Value == name)
        {
            change(parameter);
        }
    }

    // A new function: its arguments with the game's defaults.
    private TriggerFunction New(TriggerFunctionType type, string name, int? branch)
    {
        var entry = _catalog.Function(type, name)
            ?? throw new ArgumentOutOfRangeException(nameof(name), name, "No such function.");
        var function = new TriggerFunction { Type = type, Name = name, IsEnabled = true, Branch = branch };
        for (var i = 0; i < entry.Arguments.Count; i++)
        {
            function.Parameters.Add(Default(entry.Arguments[i], i < entry.Defaults.Count ? entry.Defaults[i] : ""));
        }

        return function;
    }

    private TriggerFunctionParameter Default(string type, string value)
    {
        value = value.Trim();
        var kind = TypeForArgument(type);
        if (value.Length == 0)
        {
            return new TriggerFunctionParameter { Type = TriggerFunctionParameterType.Undefined, Value = "" };
        }

        if (_catalog.Function(kind, value) != null)
        {
            return new TriggerFunctionParameter
            {
                Type = TriggerFunctionParameterType.Function, Value = value, Function = New(kind, value, null),
            };
        }

        if (_catalog.Preset(value) != null)
        {
            return new TriggerFunctionParameter { Type = TriggerFunctionParameterType.Preset, Value = value };
        }

        return new TriggerFunctionParameter { Type = TriggerFunctionParameterType.String, Value = value.Trim('"') };
    }

    private TriggerFunctionParameter Made(ParamInput input, string type)
    {
        switch (input.Kind)
        {
            case "none":
                return new TriggerFunctionParameter { Type = TriggerFunctionParameterType.Undefined, Value = "" };
            case "preset":
                return _catalog.Preset(input.Value) != null
                    ? new TriggerFunctionParameter { Type = TriggerFunctionParameterType.Preset, Value = input.Value }
                    : throw new ArgumentOutOfRangeException(nameof(input), input.Value, "No such preset.");
            case "value":
                CheckNumber(input.Value, type);
                return new TriggerFunctionParameter { Type = TriggerFunctionParameterType.String, Value = input.Value };
            case "function":
                var kind = TypeForArgument(type);
                return new TriggerFunctionParameter
                {
                    Type = TriggerFunctionParameterType.Function, Value = input.Value, Function = New(kind, input.Value, null),
                };
            case "variable":
                var variable = _triggers.Variables.FirstOrDefault(each => each.Name == input.Value);
                return new TriggerFunctionParameter
                {
                    Type = TriggerFunctionParameterType.Variable,
                    Value = input.Value,
                    ArrayIndexer = variable is { IsArray: true }
                        ? new TriggerFunctionParameter { Type = TriggerFunctionParameterType.String, Value = "0" }
                        : null,
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Kind, "No such kind of value.");
        }
    }

    private static void CheckNumber(string value, string type)
    {
        var fine = type switch
        {
            "integer" => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "real" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            _ => true,
        };
        if (!fine)
        {
            throw new TriggerEditException(Texts.Get("app.triggers.notNumber", ("value", value)));
        }
    }

    // Where a function goes: after the last of its group, or before the one at index in it. A group keeps events
    // before conditions before actions at the top, and a block's functions together.
    private string Place(TriggerDefinition definition, string? parentPath, int branch, int index, TriggerFunction function)
    {
        var list = definition.Functions;
        var prefix = "";
        if (parentPath != null)
        {
            list = Walk(definition, parentPath).Function.ChildFunctions;
            prefix = parentPath + "/c";
        }

        var group = list.Where(each => each.Type == function.Type && (parentPath == null || each.Branch == branch)).ToList();
        int target;
        if (index >= 0 && index < group.Count)
        {
            target = list.IndexOf(group[index]);
        }
        else if (group.Count > 0)
        {
            target = list.IndexOf(group[^1]) + 1;
        }
        else
        {
            target = list.FindIndex(each => parentPath == null ? each.Type > function.Type : each.Branch > branch);
            target = target < 0 ? list.Count : target;
        }

        list.Insert(target, function);
        return prefix + target;
    }

    // What a path names. A path is steps split by /: a number is a function of the trigger, cN a function of the block
    // lists of the function before it, pN a parameter of that function or of the function in the parameter before it,
    // and i that parameter's array index.
    private (List<TriggerFunction> List, int Index, TriggerFunction Function, TriggerFunctionParameter? Parameter) Walk(
        TriggerDefinition definition, string path)
    {
        var list = definition.Functions;
        var index = -1;
        TriggerFunction? function = null;
        TriggerFunctionParameter? parameter = null;
        foreach (var step in path.Split('/'))
        {
            switch (step[0])
            {
                case 'c':
                    list = function!.ChildFunctions;
                    index = int.Parse(step[1..]);
                    function = list[index];
                    parameter = null;
                    break;
                case 'p':
                    function = parameter?.Function ?? function;
                    parameter = function!.Parameters[int.Parse(step[1..])];
                    break;
                case 'i':
                    parameter = parameter!.ArrayIndexer;
                    break;
                default:
                    index = int.Parse(step);
                    function = list[index];
                    break;
            }
        }

        return (list, index, function!, parameter);
    }

    private string TypeOfParameter(TriggerFunction function, TriggerFunctionParameter parameter)
    {
        var entry = _catalog.Function(function.Type, function.Name);
        var at = function.Parameters.IndexOf(parameter);
        var type = entry != null && at < entry.Arguments.Count ? entry.Arguments[at] : "";
        return Effective(function, type);
    }

    // SetVariable's value has the type of its variable.
    private string Effective(TriggerFunction function, string type)
    {
        if (type is "Null" or "AnyGlobal")
        {
            var variable = function.Parameters[0];
            return TypeOfVariable(variable.Value) ?? type;
        }

        return type;
    }

    /// <summary>The type of a variable of the map or a generated one; null for neither.</summary>
    internal string? VariableType(string name) => TypeOfVariable(name);

    private string? TypeOfVariable(string name) =>
        _triggers.Variables.FirstOrDefault(variable => variable.Name == name)?.Type ?? GeneratedType(name);

    private static string? GeneratedType(string name) => name.Split('_') switch
    {
        ["gg", "unit", ..] => "unit",
        ["gg", "item", ..] => "item",
        ["gg", "dest", ..] => "destructable",
        ["gg", "rct", ..] => "rect",
        ["gg", "cam", ..] => "camerasetup",
        ["gg", "trg", ..] => "trigger",
        ["gg", "snd", ..] => "sound",
        _ => null,
    };
}
