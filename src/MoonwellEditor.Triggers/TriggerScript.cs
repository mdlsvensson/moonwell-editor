using MoonwellEditor.Files;
using War3Net.Build;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Script;
using War3Net.CodeAnalysis;
using War3Net.CodeAnalysis.Jass.Extensions;

namespace MoonwellEditor.Triggers;

/// <summary>
/// A JASS map's script with its triggers as they are now. It keeps the map's own <c>war3map.j</c> and replaces only what
/// triggers decide, as World Editor would write it: the user's variables and the trigger variables in <c>globals</c>,
/// <c>InitGlobals</c>, the custom script code, the trigger functions, <c>InitCustomTriggers</c> and
/// <c>RunInitializationTriggers</c>. War3Net cannot build the rest of the script as World Editor does, so the rest
/// stays as World Editor wrote it.
/// </summary>
public sealed class TriggerScript : MapScriptBuilder
{
    private const string Line = "//===========================================================================";
    private const string Banner = "//***************************************************************************";

    private readonly TriggerDocument _document;
    private readonly Map _map;

    // The trigger being written, for the problem when War3Net fails.
    private string _rendering = "";

    private TriggerScript(TriggerDocument document, MapInfo info)
        : base(document.Catalog.Data)
    {
        _document = document;
        _map = new Map { Info = info, Triggers = document.Triggers };
        SetDefaultOptionsForMap(_map);
    }

    /// <summary>
    /// The script <paramref name="script"/> with the triggers of <paramref name="document"/>; null, with the reason in
    /// <paramref name="problem"/>, when the script is not cut as World Editor writes it. Line ends are the script's.
    /// </summary>
    public static string? Write(string script, TriggerDocument document, MapInfo info, out string? problem)
    {
        var builder = new TriggerScript(document, info);
        var lineEnd = script.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = script.Replace("\r\n", "\n").Split('\n').ToList();
        try
        {
            problem = builder.Splice(lines);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // War3Net cannot write this trigger (a loop it has no code for, a parameter with no value).
            problem = Texts.Get("app.triggers.scriptFailed", ("trigger", builder._rendering), ("reason", exception.Message));
        }

        return problem == null ? string.Join(lineEnd, lines) : null;
    }

    private static int Find(List<string> lines, Func<string, bool> test, int from = 0)
    {
        for (var i = from; i < lines.Count; i++)
        {
            if (test(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    // The line of a banner's title; -1 when the script has no such banner.
    private static int BannerOf(List<string> lines, string title) => Find(lines, line => line == "//*  " + title);

    // Where the section after the banner at title ends: the next banner's first line.
    private static int NextBanner(List<string> lines, int title) =>
        Find(lines, line => line == Banner, title + 3);

    private static List<string> Text(Action<IndentedTextWriter> write)
    {
        using var text = new StringWriter();
        text.NewLine = "\n";
        using var writer = new IndentedTextWriter(text);
        write(writer);
        writer.Flush();
        var lines = text.ToString().Split('\n').ToList();
        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private string? Splice(List<string> lines)
    {
        var problem = Texts.Get("app.triggers.scriptCut");
        var globals = Find(lines, line => line == "globals");
        var end = Find(lines, line => line == "endglobals");
        if (globals < 0 || end < 0)
        {
            return problem;
        }

        // From the last place to the first, so a place found stays right.
        var triggers = BannerOf(lines, "Triggers");
        // Older World Editors wrote this banner twice, the code under the second.
        var custom = lines.LastIndexOf("//*  Custom Script Code");
        if (triggers < 0 || custom < 0)
        {
            return problem;
        }

        var main = Find(lines, line => line.StartsWith("function main takes", StringComparison.Ordinal));
        if (main < 0)
        {
            return problem;
        }

        ReplaceMainCalls(lines, main);
        var after = NextBanner(lines, triggers);
        if (after < 0)
        {
            return problem;
        }

        lines.RemoveRange(triggers + 3, after - triggers - 3);
        lines.InsertRange(triggers + 3, TriggerSection());

        var afterCustom = NextBanner(lines, custom);
        lines.RemoveRange(custom + 3, afterCustom - custom - 3);
        lines.InsertRange(custom + 3, CustomSection());

        var initGlobals = Find(lines, line => line.StartsWith("function InitGlobals takes", StringComparison.Ordinal));
        if (initGlobals >= 0)
        {
            var close = Find(lines, line => line == "endfunction", initGlobals);
            lines.RemoveRange(initGlobals, close - initGlobals + 1);
            lines.InsertRange(initGlobals, Text(WriteInitGlobals));
        }

        ReplaceGlobals(lines, globals, end);
        return null;
    }

    private void ReplaceGlobals(List<string> lines, int globals, int end)
    {
        var triggerLines = lines
            .Select((line, index) => (Line: line, Index: index))
            .Where(each => each.Index > globals && each.Index < end)
            .Where(each => each.Line.TrimStart().StartsWith("trigger", StringComparison.Ordinal)
                && each.Line.Contains(" gg_trg_", StringComparison.Ordinal))
            .Select(each => each.Index)
            .ToList();
        var place = triggerLines.Count > 0
            ? triggerLines[0]
            : Find(lines, line => IsAfterTriggers(line) || line == "endglobals", globals);
        for (var i = triggerLines.Count - 1; i >= 0; i--)
        {
            lines.RemoveAt(triggerLines[i]);
        }

        lines.InsertRange(place, Text(writer =>
        {
            writer.Indent();
            foreach (var definition in Definitions())
            {
                writer.WriteAlignedGlobal("trigger", "gg_trg_" + TriggerJass.Identifier(definition.Name), "null");
            }
        }));

        var user = Find(lines, line => line.Trim() == "// User-defined", globals);
        var generated = Find(lines, line => line.Trim() == "// Generated", globals);
        end = Find(lines, line => line == "endglobals", globals);
        if (user >= 0)
        {
            lines.RemoveRange(user, (generated >= 0 ? generated : end) - user);
            generated = generated >= 0 ? user : -1;
        }

        var position = user >= 0 ? user : globals + 1;
        var block = Text(writer =>
        {
            writer.Indent();
            if (ShouldGenerateUserDefinedVariables(_map))
            {
                writer.WriteLine("// User-defined");
                WriteVariables(writer);
                if (generated >= 0)
                {
                    writer.WriteLine();
                }
            }
        });
        lines.InsertRange(position, block);
    }

    // InitGlobals as World Editor writes it. A value goes by its type, so an object's id has its quotes.
    private void WriteInitGlobals(IndentedTextWriter writer)
    {
        var variables = _document.Triggers.Variables;
        var values = new TriggerJass(writer, _document);
        writer.WriteLine("function InitGlobals takes nothing returns nothing");
        writer.Indent();
        if (variables.Any(variable => (variable.IsArray || _map.Info!.FormatVersion >= MapInfoFormatVersion.v31)
            && InitialOf(variable, values) != null))
        {
            writer.WriteLocal("integer", "i", "0");
        }

        foreach (var variable in variables)
        {
            if (InitialOf(variable, values) is not { } value)
            {
                continue;
            }

            var name = "udg_" + variable.Name;
            if (!variable.IsArray)
            {
                writer.WriteSet(name, value);
                continue;
            }

            writer.WriteSet("i", "0");
            writer.WriteLoop();
            writer.WriteExitWhen($"(i > {variable.ArraySize})");
            writer.WriteSet($"{name}[i]", value);
            writer.WriteSet("i", "i + 1");
            writer.WriteEndLoop();
            writer.WriteLine();
        }

        writer.Unindent();
        writer.WriteLine("endfunction");
    }

    // What a variable starts as: its value, or its type's; null for neither.
    private string? InitialOf(VariableDefinition variable, TriggerJass values)
    {
        if (variable.IsInitialized)
        {
            return TriggerData.TriggerParams.TryGetValue(variable.InitialValue, out var preset)
                && preset.VariableType == variable.Type
                    ? preset.ScriptText
                    : values.Literal(variable.InitialValue, variable.Type);
        }

        if (TriggerData.TriggerTypeDefaults.TryGetValue(variable.Type, out var fallback))
        {
            return fallback.ScriptText;
        }

        return variable.Type == "string" ? "\"\"" : null;
    }

    // A variable's line in globals, by the script type its type is made of: unitcode is an integer.
    private void WriteVariables(IndentedTextWriter writer)
    {
        foreach (var variable in _document.Triggers.Variables)
        {
            var type = _document.Catalog.Type(variable.Type)?.Base ?? variable.Type;
            var name = "udg_" + variable.Name;
            if (variable.IsArray)
            {
                writer.WriteAlignedGlobal(type + " array", name);
            }
            else if (type == "string")
            {
                writer.WriteAlignedGlobal(type, name);
            }
            else
            {
                writer.WriteAlignedGlobal(type, name, type switch
                {
                    "integer" or "real" => "0",
                    "boolean" => "false",
                    _ => "null",
                });
            }
        }
    }

    private static bool IsAfterTriggers(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("unit ", StringComparison.Ordinal)
            || trimmed.StartsWith("destructable ", StringComparison.Ordinal);
    }

    private void ReplaceMainCalls(List<string> lines, int main)
    {
        var close = Find(lines, line => line == "endfunction", main);
        foreach (var call in new[] { "    call InitCustomTriggers(  )", "    call RunInitializationTriggers(  )" })
        {
            var at = Find(lines, line => line == call, main);
            if (at >= 0 && at < close)
            {
                lines.RemoveAt(at);
                close--;
            }
        }

        var initGlobals = Find(lines, line => line == "    call InitGlobals(  )", main);
        var blizzard = Find(lines, line => line == "    call InitBlizzard(  )", main);
        var place = (initGlobals >= 0 ? initGlobals : blizzard) + 1;
        var enabled = Definitions().Where(each => each.IsEnabled).ToList();
        if (enabled.Any(RunsAtStart))
        {
            lines.Insert(place, "    call RunInitializationTriggers(  )");
        }

        if (enabled.Count > 0)
        {
            lines.Insert(place, "    call InitCustomTriggers(  )");
        }
    }

    private List<string> CustomSection()
    {
        var lines = new List<string>();
        var scripts = _document.Triggers.TriggerItems
            .OfType<TriggerDefinition>()
            .Where(definition => definition.Type == TriggerItemType.Script && definition.IsEnabled)
            .Select(definition => _document.CodeOf(definition.Id) ?? "");
        foreach (var each in scripts.Prepend(_document.HeaderCode))
        {
            var code = each.Replace("\r\n", "\n").Trim('\0').TrimEnd('\n');
            if (code.Length > 0)
            {
                lines.AddRange(code.Split('\n'));
                lines.Add("");
            }
        }

        if (lines.Count == 0)
        {
            lines.Add("");
        }

        return lines;
    }

    private List<string> TriggerSection()
    {
        var lines = new List<string> { "" };
        foreach (var definition in Definitions().Where(each => each.IsEnabled))
        {
            _rendering = definition.Name;
            lines.AddRange(definition.IsCustomTextTrigger
                ? CustomTrigger(definition)
                : Text(writer => new TriggerJass(writer, _document).Write(definition)));
            if (definition.IsCustomTextTrigger)
            {
                lines.Add("");
            }
        }

        var enabled = Definitions().Where(each => each.IsEnabled).ToList();
        if (enabled.Count > 0)
        {
            lines.Add(Line);
            lines.AddRange(Text(writer =>
            {
                writer.WriteLine("function InitCustomTriggers takes nothing returns nothing");
                writer.Indent();
                foreach (var definition in enabled)
                {
                    writer.WriteCall("InitTrig_" + TriggerJass.Identifier(definition.Name));
                }

                writer.Unindent();
                writer.WriteLine("endfunction");
            }));
            lines.Add("");
        }

        var atStart = enabled.Where(RunsAtStart).ToList();
        if (atStart.Count > 0)
        {
            lines.Add(Line);
            lines.AddRange(Text(writer =>
            {
                writer.WriteLine("function RunInitializationTriggers takes nothing returns nothing");
                writer.Indent();
                foreach (var definition in atStart)
                {
                    writer.WriteCall("ConditionalTriggerExecute", "gg_trg_" + TriggerJass.Identifier(definition.Name));
                }

                writer.Unindent();
                writer.WriteLine("endfunction");
            }));
            lines.Add("");
        }

        return lines;
    }

    private static bool RunsAtStart(TriggerDefinition definition) =>
        definition.IsInitiallyOn
        && (definition.IsCustomTextTrigger
            ? definition.RunOnMapInit
            : definition.Functions.Any(function => function is
                { Type: TriggerFunctionType.Event, IsEnabled: true, Name: "MapInitializationEvent" }));

    // The triggers of the script. A custom script item is free code that goes with the map's own, so it has no function.
    private IEnumerable<TriggerDefinition> Definitions() => _document.Triggers.TriggerItems
        .OfType<TriggerDefinition>()
        .Where(definition => definition.Type == TriggerItemType.Gui && !definition.IsComment);

    private List<string> CustomTrigger(TriggerDefinition definition)
    {
        var lines = new List<string> { Line, "// Trigger: " + definition.Name };
        if (!string.IsNullOrEmpty(definition.Description))
        {
            lines.Add("//");
            lines.AddRange(definition.Description.Replace("\r\n", "\n").Split('\n').Select(line => "// " + line));
        }

        lines.Add(Line);
        lines.AddRange((_document.CodeOf(definition.Id) ?? "").Replace("\r\n", "\n").Trim('\0').TrimEnd('\n').Split('\n'));
        return lines;
    }
}
