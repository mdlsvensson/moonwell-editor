using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Script;
using War3Net.CodeAnalysis;
using War3Net.CodeAnalysis.Jass;
using War3Net.CodeAnalysis.Jass.Extensions;

namespace MoonwellEditor.Triggers;

/// <summary>
/// A GUI trigger as World Editor writes it in <c>war3map.j</c>. It follows War3Net's <c>TriggerRenderer</c> (MIT),
/// which has no multiple-action loops and writes some values without their quotes, with the game's own catalog deciding
/// how a value is written; it is checked against World Editor's script of every JASS map of the install.
/// </summary>
internal sealed partial class TriggerJass(IndentedTextWriter writer, TriggerDocument document)
{
    private const string Separator = "//===========================================================================";

    private readonly TriggerCatalog _catalog = document.Catalog;

    /// <summary>The name of a trigger in the script: spaces and other signs as underscores, a sign's bytes each one.</summary>
    public static string Identifier(string name) =>
        NotIdentifier().Replace(name.TrimEnd(), match => new string('_', Encoding.UTF8.GetBytes(match.Value).Length));

    /// <summary>Writes the trigger's functions, helpers first.</summary>
    public void Write(TriggerDefinition trigger)
    {
        var name = Identifier(trigger.Name);
        writer.WriteLine(Separator);
        writer.WriteLine("// Trigger: " + trigger.Name);
        if (!string.IsNullOrEmpty(trigger.Description))
        {
            writer.WriteLine("//");
            using var lines = new StringReader(trigger.Description.Replace("\r\r\n", "\n"));
            while (lines.ReadLine() is { } line)
            {
                writer.WriteLine("// " + line);
            }
        }

        writer.WriteLine(Separator);
        var builder = new NameBuilder($"Trig_{name}_");
        if (trigger.Functions.Any(function => function.Type == TriggerFunctionType.Condition && function.IsEnabled))
        {
            WriteConditions(builder, $"Trig_{name}_Conditions", true, trigger.Functions);
        }

        WriteActions(builder, $"Trig_{name}_Actions", trigger.Functions);
        writer.WriteLine(Separator);
        WriteInit(trigger, name);
    }

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex NotIdentifier();

    private void WriteInit(TriggerDefinition trigger, string name)
    {
        var variable = "gg_trg_" + name;
        writer.WriteLine($"function InitTrig_{name} takes nothing returns nothing");
        writer.Indent();
        writer.WriteSet(variable, "CreateTrigger(  )");
        if (!trigger.IsInitiallyOn)
        {
            writer.WriteCall("DisableTrigger", variable);
        }

        var builder = new NameBuilder($"{name}_Func");
        foreach (var function in trigger.Functions.Where(each => each.Type == TriggerFunctionType.Event && each.IsEnabled))
        {
            if (function.Name != "MapInitializationEvent")
            {
                writer.WriteCall(function.Name, [variable, .. Arguments(function, builder)]);
            }
        }

        if (trigger.Functions.Any(function => function.Type == TriggerFunctionType.Condition && function.IsEnabled))
        {
            writer.WriteCall("TriggerAddCondition", variable, $"Condition( function Trig_{name}_Conditions )");
        }

        writer.WriteCall("TriggerAddAction", variable, $"function Trig_{name}_Actions");
        writer.Unindent();
        writer.WriteLine("endfunction");
        writer.WriteLine();
    }

    private void WriteActions(NameBuilder builder, string name, List<TriggerFunction> functions)
    {
        builder.Push("Func");
        var body = IndentedTextWriter.New(writer);
        var rendering = new TriggerJass(body, document);
        body.WriteLine($"function {name} takes nothing returns nothing");
        body.Indent();
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i].Type == TriggerFunctionType.Action && functions[i].IsEnabled)
            {
                builder.Push(i + 1);
                rendering.WriteAction(functions[i], builder);
                builder.Pop();
            }
        }

        body.Unindent();
        body.WriteLine("endfunction");
        body.WriteLine();
        writer.Write(rendering._helpers.ToString());
        writer.Write(body.ToString());
        builder.Pop();
    }

    // What a function's body needs before it: helpers written while the body is being written.
    private readonly StringBuilder _helpers = new();

    private void WriteConditions(NameBuilder builder, string name, bool all, List<TriggerFunction> functions)
    {
        builder.Push("Func");
        var body = IndentedTextWriter.New(writer);
        var rendering = new TriggerJass(body, document);
        body.WriteLine($"function {name} takes nothing returns boolean");
        body.Indent();
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i].Type != TriggerFunctionType.Condition || !functions[i].IsEnabled)
            {
                continue;
            }

            builder.Push(i + 1);
            var expression = rendering.Condition(functions[i], builder);
            builder.Pop();
            body.WriteLine(all ? $"if ( not {expression} ) then" : $"if ( {expression} ) then");
            body.Indent();
            body.WriteLine(all ? "return false" : "return true");
            body.Unindent();
            body.WriteLine("endif");
        }

        body.WriteLine(all ? "return true" : "return false");
        body.Unindent();
        body.WriteLine("endfunction");
        body.WriteLine();
        writer.Write(rendering._helpers.ToString());
        writer.Write(body.ToString());
        builder.Pop();
    }

    private void WriteConditionFunction(NameBuilder builder, string name, TriggerFunctionParameter parameter)
    {
        var function = Required(parameter.Function);
        var body = IndentedTextWriter.New(writer);
        var rendering = new TriggerJass(body, document);
        body.WriteLine($"function {name} takes nothing returns boolean");
        body.Indent();
        body.WriteLine("return " + rendering.Condition(function, builder));
        body.Unindent();
        body.WriteLine("endfunction");
        body.WriteLine();
        Helper(rendering, body);
    }

    private void WriteActionFunction(NameBuilder builder, string name, TriggerFunctionParameter parameter)
    {
        var function = Required(parameter.Function);
        var body = IndentedTextWriter.New(writer);
        var rendering = new TriggerJass(body, document);
        body.WriteLine($"function {name} takes nothing returns nothing");
        body.Indent();
        rendering.WriteAction(function, builder);
        body.Unindent();
        body.WriteLine("endfunction");
        body.WriteLine();
        Helper(rendering, body);
    }

    // Puts a finished helper, with the helpers it needs, before the function being written.
    private void Helper(TriggerJass rendering, IndentedTextWriter body)
    {
        _helpers.Append(rendering._helpers).Append(body);
    }

    private static TriggerFunction Required(TriggerFunction? function) =>
        function ?? throw new InvalidDataException("A parameter has no function.");

    private string Condition(TriggerFunction function, NameBuilder builder)
    {
        if (function.Name is "OrMultiple" or "AndMultiple")
        {
            var name = $"{builder}C";
            WriteHelperConditions(builder, name, function.Name == "AndMultiple", function.ChildFunctions);
            return name + "()";
        }

        if (function.Name is "GetBooleanAnd" or "GetBooleanOr")
        {
            builder.Push(1);
            var first = builder.ToString();
            WriteConditionFunction(builder, first, function.Parameters[0]);
            builder.Pop();
            builder.Push(2);
            var second = builder.ToString();
            WriteConditionFunction(builder, second, function.Parameters[1]);
            builder.Pop();
            return $"{function.Name}( {first}(), {second}() )";
        }

        var arguments = Arguments(function, builder).ToList();
        var symbol = arguments.Count > 1 ? arguments[1].Trim('"') : "";
        return arguments.Count == 3 ? $"( {arguments[0]} {symbol} {arguments[2]} )" : $"{function.Name}({string.Join(", ", arguments)})";
    }

    private void WriteHelperConditions(NameBuilder builder, string name, bool all, List<TriggerFunction> functions)
    {
        var helpers = IndentedTextWriter.New(writer);
        new TriggerJass(helpers, document).WriteConditions(builder, name, all, functions);
        _helpers.Append(helpers);
    }

    private void WriteHelperActions(NameBuilder builder, string name, List<TriggerFunction> functions)
    {
        var helpers = IndentedTextWriter.New(writer);
        new TriggerJass(helpers, document).WriteActions(builder, name, functions);
        _helpers.Append(helpers);
    }

    private void WriteAction(TriggerFunction function, NameBuilder builder)
    {
        switch (function.Name)
        {
            case "SetVariable":
                WriteSetVariable(function, builder);
                break;
            case "WaitForCondition":
                WriteWaitForCondition(function, builder);
                break;
            case "ForLoopA":
                WriteLoop(function, builder, "bj_forLoopAIndex", "bj_forLoopAIndexEnd", false);
                break;
            case "ForLoopB":
                WriteLoop(function, builder, "bj_forLoopBIndex", "bj_forLoopBIndexEnd", false);
                break;
            case "ForLoopAMultiple":
                WriteLoop(function, builder, "bj_forLoopAIndex", "bj_forLoopAIndexEnd", true);
                break;
            case "ForLoopBMultiple":
                WriteLoop(function, builder, "bj_forLoopBIndex", "bj_forLoopBIndexEnd", true);
                break;
            case "ForLoopVar":
                WriteVariableLoop(function, builder, false);
                break;
            case "ForLoopVarMultiple":
                WriteVariableLoop(function, builder, true);
                break;
            case "IfThenElse":
                WriteIf(function, builder);
                break;
            case "IfThenElseMultiple":
                WriteIfMultiple(function, builder);
                break;
            case "EnumDestructablesInCircleBJ":
            case "EnumDestructablesInRectAll":
            case "EnumItemsInRectBJ":
            case "ForForce":
            case "ForGroup":
                WriteForeach(function, builder);
                break;
            case "EnumDestructablesInCircleBJMultiple":
            case "EnumDestructablesInRectAllMultiple":
            case "EnumItemsInRectBJMultiple":
            case "ForForceMultiple":
            case "ForGroupMultiple":
                WriteForeachMultiple(function, builder);
                break;
            case "CommentString":
                writer.WriteLine("// " + function.Parameters[0].Value);
                break;
            case "CustomScriptCode":
                writer.WriteLine(function.Parameters[0].Value);
                break;
            case "ReturnAction":
                writer.WriteLine("return");
                break;
            default:
                writer.WriteCall(ScriptNameOf(function), [.. Arguments(function, builder)]);
                break;
        }
    }

    private void WriteSetVariable(TriggerFunction function, NameBuilder builder)
    {
        var variable = function.Parameters[0];
        var name = VariableName(variable.Value);
        if (variable.ArrayIndexer != null)
        {
            name += $"[{Value(variable.ArrayIndexer, "integer", 0, builder)}]";
        }

        var type = document.VariableType(variable.Value) ?? throw new InvalidDataException("No such variable: " + variable.Value);
        writer.WriteSet(name, Value(function.Parameters[1], type, 1, builder));
    }

    private void WriteWaitForCondition(TriggerFunction function, NameBuilder builder)
    {
        builder.Push(1);
        var name = builder.ToString();
        WriteConditionFunction(builder, name, function.Parameters[0]);
        builder.Pop();
        writer.WriteLine("loop");
        writer.Indent();
        writer.WriteLine($"exitwhen ( {name}() )");
        var interval = Value(function.Parameters[1], "real", 1, builder);
        writer.WriteLine($"call TriggerSleepAction(RMaxBJ(bj_WAIT_FOR_COND_MIN_INTERVAL, {interval}))");
        writer.Unindent();
        writer.WriteLine("endloop");
    }

    private void WriteLoop(TriggerFunction function, NameBuilder builder, string index, string end, bool multiple)
    {
        writer.WriteSet(index, Value(function.Parameters[0], "integer", 0, builder));
        writer.WriteSet(end, Value(function.Parameters[1], "integer", 1, builder));
        writer.WriteLine("loop");
        writer.Indent();
        writer.WriteLine($"exitwhen {index} > {end}");
        WriteLoopBody(function, builder, multiple, 3);
        writer.WriteSet(index, $"{index} + 1");
        writer.Unindent();
        writer.WriteLine("endloop");
    }

    private void WriteVariableLoop(TriggerFunction function, NameBuilder builder, bool multiple)
    {
        var name = Value(function.Parameters[0], "integer", 0, builder);
        writer.WriteSet(name, Value(function.Parameters[1], "integer", 1, builder));
        writer.WriteLine("loop");
        writer.Indent();
        writer.WriteLine($"exitwhen {name} > {Value(function.Parameters[2], "integer", 2, builder)}");
        WriteLoopBody(function, builder, multiple, 4);
        writer.WriteSet(name, $"{name} + 1");
        writer.Unindent();
        writer.WriteLine("endloop");
    }

    private void WriteLoopBody(TriggerFunction function, NameBuilder builder, bool multiple, int action)
    {
        if (!multiple)
        {
            builder.Push(action);
            WriteAction(Required(function.Parameters[action - 1].Function), builder);
            builder.Pop();
            return;
        }

        builder.Push("Func");
        WriteChildren(function, 0, builder);
        builder.Pop();
    }

    private void WriteChildren(TriggerFunction function, int branch, NameBuilder builder)
    {
        for (var i = 0; i < function.ChildFunctions.Count; i++)
        {
            var child = function.ChildFunctions[i];
            if (child.Branch == branch && child.IsEnabled && child.Type == TriggerFunctionType.Action)
            {
                builder.Push(i + 1);
                WriteAction(child, builder);
                builder.Pop();
            }
        }
    }

    private void WriteIf(TriggerFunction function, NameBuilder builder)
    {
        builder.Push(1);
        var name = builder.ToString();
        WriteConditionFunction(builder, name, function.Parameters[0]);
        builder.Pop();
        writer.WriteLine($"if ( {name}() ) then");
        writer.Indent();
        builder.Push(2);
        WriteAction(Required(function.Parameters[1].Function), builder);
        builder.Pop();
        writer.Unindent();
        writer.WriteLine("else");
        writer.Indent();
        builder.Push(3);
        WriteAction(Required(function.Parameters[2].Function), builder);
        builder.Pop();
        writer.Unindent();
        writer.WriteLine("endif");
    }

    private void WriteIfMultiple(TriggerFunction function, NameBuilder builder)
    {
        // World Editor goes through what is inside in its order, conditions and actions alike, each one's helpers as
        // it comes, and writes the helper of the conditions last.
        var conditions = IndentedTextWriter.New(writer);
        var then = new StringBuilder();
        var otherwise = new StringBuilder();
        builder.Push("Func");
        for (var i = 0; i < function.ChildFunctions.Count; i++)
        {
            var child = function.ChildFunctions[i];
            if (!child.IsEnabled)
            {
                continue;
            }

            builder.Push(i + 1);
            if (child.Type == TriggerFunctionType.Condition)
            {
                conditions.WriteLine($"if ( not {Condition(child, builder)} ) then");
                conditions.WriteLine("    return false");
                conditions.WriteLine("endif");
            }
            else if (child.Type == TriggerFunctionType.Action && child.Branch is 1 or 2)
            {
                var inner = IndentedTextWriter.New(writer);
                var rendering = new TriggerJass(inner, document);
                rendering.WriteAction(child, builder);
                _helpers.Append(rendering._helpers);
                (child.Branch == 1 ? then : otherwise).Append(inner);
            }

            builder.Pop();
        }

        builder.Pop();
        var name = $"{builder}C";
        var helper = IndentedTextWriter.New(writer);
        helper.WriteLine($"function {name} takes nothing returns boolean");
        helper.Indent();
        foreach (var line in (conditions.ToString() ?? "").Split(writer.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            helper.WriteLine(line);
        }

        helper.WriteLine("return true");
        helper.Unindent();
        helper.WriteLine("endfunction");
        helper.WriteLine();
        _helpers.Append(helper);
        writer.WriteLine($"if ( {name}() ) then");
        writer.Indent();
        WriteLines(then.ToString());
        writer.Unindent();
        writer.WriteLine("else");
        writer.Indent();
        WriteLines(otherwise.ToString());
        writer.Unindent();
        writer.WriteLine("endif");
    }

    private void WriteLines(string text)
    {
        foreach (var line in text.Split(writer.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            writer.WriteLine(line);
        }
    }

    private void WriteForeach(TriggerFunction function, NameBuilder builder)
    {
        var arguments = function.Parameters.SkipLast(1).Select((parameter, index) =>
            Value(parameter, ArgumentType(function, index), index, builder)).ToList();
        builder.Push(function.Parameters.Count);
        var name = builder.ToString();
        WriteActionFunction(builder, name, function.Parameters[^1]);
        builder.Pop();
        writer.WriteCall(ScriptNameOf(function), [.. arguments, "function " + name]);
    }

    private void WriteForeachMultiple(TriggerFunction function, NameBuilder builder)
    {
        var arguments = Arguments(function, builder).ToList();
        var name = $"{builder}A";
        WriteHelperActions(builder, name, function.ChildFunctions);
        writer.WriteCall(ScriptNameOf(function), [.. arguments, "function " + name]);
    }

    private string ScriptNameOf(TriggerFunction function)
    {
        if (function.Type == TriggerFunctionType.Action)
        {
            return _catalog.Data.TriggerActions[function.Name].ScriptName ?? function.Name;
        }

        return _catalog.CallScriptNames.GetValueOrDefault(function.Name, function.Name);
    }

    private string ArgumentType(TriggerFunction function, int index) =>
        _catalog.Function(function.Type, function.Name)?.Arguments[index] ?? "";

    private IEnumerable<string> Arguments(TriggerFunction function, NameBuilder builder)
    {
        var entry = _catalog.Function(function.Type, function.Name)
            ?? throw new InvalidDataException("No such function: " + function.Name);
        for (var i = 0; i < entry.Arguments.Count; i++)
        {
            yield return Value(function.Parameters[i], entry.Arguments[i], i, builder);
        }
    }

    private string Value(TriggerFunctionParameter parameter, string type, int index, NameBuilder builder)
    {
        builder.Push(index + 1);
        try
        {
            return parameter.Type switch
            {
                TriggerFunctionParameterType.Preset => Preset(parameter.Value),
                TriggerFunctionParameterType.Variable => parameter.ArrayIndexer == null
                    ? VariableName(parameter.Value)
                    : $"{VariableName(parameter.Value)}[{Value(parameter.ArrayIndexer, "integer", 0, builder)}]",
                TriggerFunctionParameterType.Function => Call(parameter, type, builder),
                TriggerFunctionParameterType.String => Literal(parameter.Value, type),
                _ => throw new InvalidDataException(Files.Texts.Get("app.triggers.noValue")),
            };
        }
        finally
        {
            builder.Pop();
        }
    }

    private static string VariableName(string name) =>
        name.StartsWith("gg_", StringComparison.Ordinal) ? name : "udg_" + name;

    private string Preset(string key)
    {
        var code = _catalog.Preset(key)?.Code ?? throw new InvalidDataException("No such preset: " + key);
        return code.StartsWith('`') && code.EndsWith('`') ? $"\"{code[1..^1]}\"" : code;
    }

    private string Call(TriggerFunctionParameter parameter, string type, NameBuilder builder)
    {
        var function = Required(parameter.Function);
        if (type == "boolexpr")
        {
            var name = builder.ToString();
            WriteConditionFunction(builder, name, parameter);
            return $"Condition(function {name})";
        }

        if (function.Name is "OperatorInt" or "OperatorReal")
        {
            var parts = Arguments(function, builder).ToList();
            return $"( {parts[0]} {parts[1].Trim('"')} {parts[2]} )";
        }

        if (function.Name == "OperatorString")
        {
            var parts = Arguments(function, builder).ToList();
            return $"( {parts[0]} + {parts[1]} )";
        }

        return $"{ScriptNameOf(function)}({string.Join(", ", Arguments(function, builder))})";
    }

    // A value by its type: text in double quotes, an object's id in single quotes, the rest as it is.
    public string Literal(string value, string type)
    {
        var declared = _catalog.Type(type);
        var script = declared?.Base ?? type;
        if (script is "string" or "String" or "StringExt" or "stringnoformat")
        {
            return JassLiteral.String(value);
        }

        return script == "integer" && type != "integer" && value.Length == 4 && !int.TryParse(value, CultureInfo.InvariantCulture, out _)
            ? $"'{value}'"
            : value;
    }

    /// <summary>The name of a helper function, built up as the writing goes into a function.</summary>
    private sealed class NameBuilder(string start)
    {
        private readonly StringBuilder _name = new(start);
        private readonly Stack<int> _lengths = new();

        public void Push(int number) => Push(number.ToString("D3", CultureInfo.InvariantCulture));

        public void Push(string text)
        {
            _name.Append(text);
            _lengths.Push(text.Length);
        }

        public void Pop() => _name.Length -= _lengths.Pop();

        public override string ToString() => _name.ToString();
    }
}
