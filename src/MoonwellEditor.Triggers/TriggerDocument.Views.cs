using System.Globalization;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers;

/// <summary>What a trigger's sentences need from the map: the names of its objects and its strings.</summary>
public interface ITriggerNames
{
    /// <summary>The name of the object <paramref name="id"/> for a type such as <c>unitcode</c>; null when it has none.</summary>
    string? ObjectName(string type, string id);

    /// <summary>A text as the map shows it: a <c>TRIGSTR_n</c> as its strings file has it, any other as it is.</summary>
    string Text(string value);
}

/// <summary>A piece of a sentence: text, and when it is a parameter the parameter.</summary>
/// <param name="Text">The text shown.</param>
/// <param name="Parameter">The parameter it shows; null for plain text.</param>
public sealed record TextPart(string Text, ParameterView? Parameter = null);

/// <summary>A parameter of a function.</summary>
/// <param name="Path">Where it is in the trigger.</param>
/// <param name="Type">Its type's key.</param>
/// <param name="Name">The argument's name in the sentence (<c>Time</c>).</param>
/// <param name="Kind"><c>preset</c>, <c>variable</c>, <c>function</c>, <c>value</c>, or <c>none</c> when it has no value.</param>
/// <param name="Value">The value the map's file holds.</param>
/// <param name="Text">The value as shown.</param>
/// <param name="Typed">A typed value as its box starts: not rounded, a <c>TRIGSTR_n</c> as its text; empty for the other kinds.</param>
/// <param name="Function">A function's own sentence and parameters; null for the other kinds.</param>
/// <param name="Index">An array variable's index; null for the other kinds.</param>
public sealed record ParameterView(
    string Path,
    string Type,
    string Name,
    string Kind,
    string Value,
    string Text,
    string Typed,
    FunctionView? Function,
    ParameterView? Index);

/// <summary>A block of a function that holds others: the Then actions of an If.</summary>
/// <param name="Branch">The block's number in the file.</param>
/// <param name="Title">Its title (<c>Then - Actions</c>).</param>
/// <param name="Kind"><c>condition</c> or <c>action</c>.</param>
/// <param name="Functions">What it holds.</param>
public sealed record BlockView(int Branch, string Title, string Kind, IReadOnlyList<FunctionView> Functions);

/// <summary>A function in a trigger.</summary>
/// <param name="Path">Where it is in the trigger.</param>
/// <param name="Name">Its name.</param>
/// <param name="Title">Its name in the editor's lists, for its card's title bar.</param>
/// <param name="Kind"><c>event</c>, <c>condition</c>, <c>action</c> or <c>call</c>.</param>
/// <param name="Category">Its category's key.</param>
/// <param name="Enabled">False when it is turned off.</param>
/// <param name="Branch">The block it is in; null for one in a trigger's own list or a parameter.</param>
/// <param name="Prefix">The category's name when World Editor shows it before the sentence; empty for none.</param>
/// <param name="Sentence">Its sentence.</param>
/// <param name="Blocks">Its blocks, for a function that holds others.</param>
public sealed record FunctionView(
    string Path,
    string Name,
    string Title,
    string Kind,
    string Category,
    bool Enabled,
    int? Branch,
    string Prefix,
    IReadOnlyList<TextPart> Sentence,
    IReadOnlyList<BlockView> Blocks);

/// <summary>One trigger, script or comment.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Kind">As in <see cref="TriggerTreeItem.Kind"/>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">Its comment.</param>
/// <param name="Enabled">True when it is enabled.</param>
/// <param name="InitiallyOn">True when it starts on.</param>
/// <param name="RunOnInit">True when it runs at map initialization.</param>
/// <param name="Events">Its events.</param>
/// <param name="Conditions">Its conditions.</param>
/// <param name="Actions">Its actions.</param>
/// <param name="Code">A script's text; null for the other kinds.</param>
public sealed record TriggerView(
    int Id,
    string Kind,
    string Name,
    string Description,
    bool Enabled,
    bool InitiallyOn,
    bool RunOnInit,
    IReadOnlyList<FunctionView> Events,
    IReadOnlyList<FunctionView> Conditions,
    IReadOnlyList<FunctionView> Actions,
    string? Code);

/// <summary>A variable.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its type's key.</param>
/// <param name="IsArray">True for an array.</param>
/// <param name="Size">An array's size.</param>
/// <param name="Initial">Its first value as the map's file holds it; empty for none.</param>
public sealed record VariableView(int Id, string Name, string Type, bool IsArray, int Size, string Initial);

/// <summary>The views the page draws.</summary>
public sealed partial class TriggerDocument
{
    /// <summary>The variables, in their file's order.</summary>
    public IReadOnlyList<VariableView> Variables() => _triggers.Variables
        .Select(each => new VariableView(each.Id, each.Name, each.Type, each.IsArray, each.ArraySize, each.InitialValue))
        .ToList();

    /// <summary>The trigger, script or comment with this id.</summary>
    public TriggerView Trigger(int id, ITriggerNames names)
    {
        var definition = (TriggerDefinition)Item(id);
        var functions = definition.Functions
            .Select((function, index) => (Function: function, View: Function(function, $"{index}", names)))
            .ToList();
        return new TriggerView(
            id,
            KindOf(definition),
            definition.Name,
            definition.Description,
            definition.IsEnabled,
            definition.IsInitiallyOn,
            definition.RunOnMapInit,
            Of(TriggerFunctionType.Event),
            Of(TriggerFunctionType.Condition),
            Of(TriggerFunctionType.Action),
            definition.IsCustomTextTrigger || definition.Type == TriggerItemType.Script ? CodeOf(id) ?? "" : null);

        List<FunctionView> Of(TriggerFunctionType type) =>
            functions.Where(each => each.Function.Type == type).Select(each => each.View).ToList();
    }

    private static (int Branch, string Title, string Kind)[] BlocksOf(string name)
    {
        if (name == "IfThenElseMultiple")
        {
            return
            [
                (0, "WESTRING_TRIGSUBFUNC_IFCONDITIONS", "condition"),
                (1, "WESTRING_TRIGSUBFUNC_IFTHENACTIONS", "action"),
                (2, "WESTRING_TRIGSUBFUNC_IFELSEACTIONS", "action"),
            ];
        }

        if (name is "AndMultiple" or "OrMultiple")
        {
            return [(0, "WESTRING_TRIGSUBFUNC_ANDORCONDITIONS", "condition")];
        }

        return name.EndsWith("Multiple", StringComparison.Ordinal)
            ? [(0, "WESTRING_TRIGSUBFUNC_FORLOOPACTIONS", "action")]
            : [];
    }

    private FunctionView Function(TriggerFunction function, string path, ITriggerNames names)
    {
        var entry = _catalog.Function(function.Type, function.Name)
            ?? throw new InvalidDataException("No such function: " + function.Name);
        var category = _catalog.Categories.FirstOrDefault(each => each.Key == entry.Category);
        var parts = new List<TextPart>();
        foreach (var part in entry.Sentence)
        {
            if (part.Argument < 0)
            {
                parts.Add(new TextPart(part.Text));
            }
            else if (part.Argument < function.Parameters.Count)
            {
                var view = Parameter(function, part.Argument, part.Text, $"{path}/p{part.Argument}", names);
                parts.Add(new TextPart(view.Text, view));
            }
        }

        var blocks = BlocksOf(function.Name).Select(block => new BlockView(
            block.Branch,
            _catalog.Label(block.Title),
            block.Kind,
            function.ChildFunctions
                .Select((child, index) => (Child: child, Index: index))
                .Where(each => each.Child.Branch == block.Branch)
                .Select(each => Function(each.Child, $"{path}/c{each.Index}", names))
                .ToList()));
        return new FunctionView(
            path,
            function.Name,
            entry.Title,
            entry.Kind,
            entry.Category,
            function.IsEnabled,
            function.Branch,
            category is { Shown: true } ? category.Title : "",
            parts,
            blocks.ToList());
    }

    private ParameterView Parameter(TriggerFunction function, int index, string name, string path, ITriggerNames names)
    {
        var entry = _catalog.Function(function.Type, function.Name);
        var declared = entry != null && index < entry.Arguments.Count ? entry.Arguments[index] : "";
        return Parameter(function.Parameters[index], Effective(function, declared), name, path, names);
    }

    private ParameterView Parameter(
        TriggerFunctionParameter parameter, string type, string name, string path, ITriggerNames names)
    {
        var inner = parameter.Function == null ? null : Function(parameter.Function, path, names);
        var index = parameter.ArrayIndexer == null
            ? null
            : Parameter(parameter.ArrayIndexer, "integer", "", path + "/i", names);
        return new ParameterView(
            path,
            type,
            name,
            parameter.Type switch
            {
                TriggerFunctionParameterType.Preset => "preset",
                TriggerFunctionParameterType.Variable => "variable",
                TriggerFunctionParameterType.Function => "function",
                TriggerFunctionParameterType.String => "value",
                _ => "none",
            },
            parameter.Value,
            parameter.Type switch
            {
                TriggerFunctionParameterType.Preset => _catalog.Preset(parameter.Value)?.Text ?? parameter.Value,
                TriggerFunctionParameterType.Variable => Named(parameter.Value, names) + (index == null ? "" : $"[{index.Text}]"),
                TriggerFunctionParameterType.Function => "(" + Plain(inner!) + ")",
                TriggerFunctionParameterType.String => Literal(type, parameter.Value, names),
                _ => name,
            },
            parameter.Type == TriggerFunctionParameterType.String ? names.Text(parameter.Value) : "",
            inner,
            index);
    }

    private static string Plain(FunctionView function) => string.Concat(function.Sentence.Select(part => part.Text));

    // A value as World Editor shows it: reals with two decimals, an object by its name, a string from the strings file.
    private string Literal(string type, string value, ITriggerNames names)
    {
        if ((type == "real" || _catalog.Type(type)?.Base == "real")
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString("F2", CultureInfo.InvariantCulture);
        }

        return names.ObjectName(type, value) ?? names.Text(value);
    }

    // A generated name as World Editor shows it (gg_unit_hfoo_0001 is "Footman 0001 <gen>"), a variable as it is.
    private static string Named(string name, ITriggerNames names)
    {
        var parts = name.Split('_', 4);
        if (parts is ["gg", "unit" or "item" or "dest", _, _])
        {
            var type = parts[1] switch
            {
                "unit" => "unitcode",
                "item" => "itemcode",
                _ => "destructablecode",
            };
            return $"{names.ObjectName(type, parts[2]) ?? parts[2]} {parts[3]} <gen>";
        }

        var kept = name.Split('_', 3);
        return kept is ["gg", "rct" or "cam" or "trg" or "snd", _] ? kept[2].Replace('_', ' ') + " <gen>" : name;
    }
}
