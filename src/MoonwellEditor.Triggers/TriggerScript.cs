using System.Globalization;
using System.Text.RegularExpressions;
using MoonwellEditor.Files;
using War3Net.Build;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Script;
using War3Net.Build.Widget;
using War3Net.CodeAnalysis;
using War3Net.CodeAnalysis.Jass.Extensions;
using War3Net.Common.Extensions;

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

    // Banners World Editor writes after the custom script code. Code pasted there can hold banners of its own (blizzard.j's
    // have this very shape), so a section ends only at one of these.
    private static readonly HashSet<string> Sections =
    [
        "Random Groups", "Map Item Tables", "Items", "Unit Item Tables", "Destructable Item Tables", "Sound Assets",
        "Sounds", "Destructable Objects", "Unit Creation", "Regions", "Cameras", "Custom Script Code", "Triggers",
        "Upgrades", "TechTree", "Players", "Main Initialization", "Map Configuration",
    ];

    private static readonly Regex CreatedAt = new(@"'(?<id>.{4})', (?<x>-?\d+(?:\.\d+)?), (?<y>-?\d+(?:\.\d+)?)");

    private static readonly Regex VJass = new(
        @"^\s*(?:library|library_once|scope|struct|module|interface|globals)\b|^\s*//!",
        RegexOptions.Multiline);

    private readonly TriggerDocument _document;
    private readonly Map _map;
    private readonly MapUnits? _units;

    // The trigger being written, for the problem when one cannot be.
    private string _rendering = "";

    private TriggerScript(TriggerDocument document, MapInfo info, MapUnits? units)
        : base(document.Catalog.Data)
    {
        _document = document;
        _units = units;
        _map = new Map { Info = info, Triggers = document.Triggers };
        SetDefaultOptionsForMap(_map);
    }

    /// <summary>
    /// The script <paramref name="script"/> with the triggers of <paramref name="document"/>; null, with the reason in
    /// <paramref name="problem"/>, when it cannot be written: a script not cut as World Editor writes it, vJass, a
    /// parameter with no value, a placed thing the script does not make. <paramref name="units"/> is the map's placed
    /// units and items. Line ends are the script's.
    /// </summary>
    public static string? Write(string script, TriggerDocument document, MapInfo info, MapUnits? units, out string? problem)
    {
        var builder = new TriggerScript(document, info, units);
        var lineEnd = script.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = script.Replace("\r\n", "\n").Split('\n').ToList();
        try
        {
            problem = builder.Splice(lines);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
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

    // The title's line of a banner of five lines.
    private static bool IsBanner(List<string> lines, int title) =>
        title >= 2 && title + 2 < lines.Count
        && lines[title - 2] == Banner && lines[title - 1] == "//*" && lines[title + 1] == "//*" && lines[title + 2] == Banner;

    private static string TitleAt(List<string> lines, int line) => lines[line]["//*  ".Length..];

    // The title line of the banner called title, from a line on; -1 when there is none.
    private static int BannerOf(List<string> lines, string title, int from = 0)
    {
        for (var i = from; i < lines.Count; i++)
        {
            if (lines[i] == "//*  " + title && IsBanner(lines, i))
            {
                return i;
            }
        }

        return -1;
    }

    // Where a section ends: the first line of the next banner World Editor writes, from a line on; -1 when there is none.
    private static int SectionEnd(List<string> lines, int from)
    {
        for (var i = from; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("//*  ", StringComparison.Ordinal) && IsBanner(lines, i) && Sections.Contains(TitleAt(lines, i)))
            {
                return i - 2;
            }
        }

        return -1;
    }

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
        if (UsesVJass())
        {
            return Texts.Get("app.triggers.scriptVJass");
        }

        var globals = lines.IndexOf("globals");
        var endGlobals = lines.IndexOf("endglobals");
        var main = Find(lines, line => line.StartsWith("function main takes", StringComparison.Ordinal));

        // Older World Editors wrote the custom script banner twice, the code under the second.
        var custom = -1;
        for (var at = BannerOf(lines, "Custom Script Code"); at >= 0; at = BannerOf(lines, "Custom Script Code", at + 1))
        {
            custom = at;
        }

        var triggers = custom < 0 ? -1 : BannerOf(lines, "Triggers", custom + 1);
        if (globals < 0 || endGlobals < 0 || main < 0 || custom < 0 || triggers < 0)
        {
            return problem;
        }

        // From the last place to the first, so a place found stays right.
        var firstFunction = Find(lines, line => line.StartsWith("function InitCustomTriggers takes", StringComparison.Ordinal), triggers);
        var afterTriggers = SectionEnd(lines, firstFunction >= 0 ? firstFunction : triggers + 3);
        var afterCustom = SectionEnd(lines, custom + 3);
        if (afterTriggers < 0 || afterCustom < 0 || !ReplaceMainCalls(lines, main))
        {
            return problem;
        }

        lines.RemoveRange(triggers + 3, afterTriggers - triggers - 3);
        lines.InsertRange(triggers + 3, TriggerSection());
        lines.RemoveRange(custom + 3, afterCustom - custom - 3);
        lines.InsertRange(custom + 3, CustomSection());

        var initGlobals = Find(lines, line => line.StartsWith("function InitGlobals takes", StringComparison.Ordinal));
        if (initGlobals >= 0)
        {
            var close = Find(lines, line => line == "endfunction", initGlobals);
            lines.RemoveRange(initGlobals, close - initGlobals + 1);
            lines.InsertRange(initGlobals, Text(WriteInitGlobals));
        }
        else if (NeedsInitGlobals())
        {
            lines.InsertRange(lines.IndexOf("endglobals") + 1, ["", .. Text(WriteInitGlobals)]);
        }

        ReplaceGlobals(lines);
        return AddPlaced(lines);
    }

    // vJass is turned into JASS by JassHelper when World Editor saves, and only its output is in the script.
    private bool UsesVJass() => VJass.IsMatch(_document.HeaderCode)
        || _document.Triggers.TriggerItems.OfType<TriggerDefinition>().Any(each => VJass.IsMatch(_document.CodeOf(each.Id) ?? ""));

    private bool NeedsInitGlobals() => _document.Triggers.Variables.Count > 0;

    private void ReplaceGlobals(List<string> lines)
    {
        var globals = lines.IndexOf("globals");
        var user = Find(lines, line => line.Trim() == "// User-defined", globals);
        var generated = Find(lines, line => line.Trim() == "// Generated", globals);
        var end = lines.IndexOf("endglobals");
        user = user < end ? user : -1;
        generated = generated < end ? generated : -1;
        var userPlace = user >= 0 ? user : globals + 1;
        if (user >= 0)
        {
            lines.RemoveRange(user, (generated > user ? generated : end) - user);
        }

        end = lines.IndexOf("endglobals");
        generated = Find(lines, line => line.Trim() == "// Generated", globals);
        generated = generated < end ? generated : -1;
        var triggerLines = Enumerable.Range(globals + 1, end - globals - 1)
            .Where(index => lines[index].TrimStart().StartsWith("trigger", StringComparison.Ordinal)
                && lines[index].Contains(" gg_trg_", StringComparison.Ordinal))
            .ToList();
        var place = triggerLines.Count > 0
            ? triggerLines[0]
            : Find(lines, line => IsAfterTriggers(line) || line == "endglobals", globals + 1);
        foreach (var line in Enumerable.Reverse(triggerLines))
        {
            lines.RemoveAt(line);
        }

        var names = Definitions().ToList();
        if (names.Count > 0 && generated < 0)
        {
            lines.Insert(place++, "    // Generated");
            generated = place - 1;
        }

        lines.InsertRange(place, Text(writer =>
        {
            writer.Indent();
            foreach (var definition in names)
            {
                writer.WriteAlignedGlobal("trigger", "gg_trg_" + TriggerJass.Identifier(definition.Name), "null");
            }
        }));
        lines.InsertRange(userPlace, Text(writer =>
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
        }));
    }

    // World Editor makes a variable, and sets it where the thing is made, only for a placed thing a trigger names. A unit
    // or item picked for the first time gets both here. A destructable named for the first time would have to change
    // war3map.doo too: the game makes it from there unless that says not to, so the script would make a second one.
    private string? AddPlaced(List<string> lines)
    {
        var end = lines.IndexOf("endglobals");
        var declared = string.Join("\n", lines.Take(end));
        foreach (var name in _document.PlacedNames().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (declared.Contains(" " + name + " ", StringComparison.Ordinal))
            {
                continue;
            }

            if (name.StartsWith("gg_dest_", StringComparison.Ordinal))
            {
                return Texts.Get("app.triggers.scriptDestructable", ("name", name));
            }

            var isItem = name.StartsWith("gg_item_", StringComparison.Ordinal);
            if (!SetWhereMade(lines, name, isItem))
            {
                return Texts.Get("app.triggers.scriptPlaced", ("name", name));
            }

            lines.InsertRange(lines.IndexOf("endglobals"), Text(writer =>
            {
                writer.Indent();
                writer.WriteAlignedGlobal(isItem ? "item" : "unit", name, "null");
            }));
        }

        return null;
    }

    // Finds the line that makes a placed thing, by its type and place, and sets the thing's variable there.
    private bool SetWhereMade(List<string> lines, string name, bool isItem)
    {
        var type = name.Substring("gg_unit_".Length, 4);
        var number = int.Parse(name["gg_unit_0000_".Length..], CultureInfo.InvariantCulture);
        var placed = _units?.Units.FirstOrDefault(each => each.CreationNumber == number && each.TypeId.ToRawcode() == type);
        if (placed == null)
        {
            return false;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var match = CreatedAt.Match(lines[i]);
            if (!match.Success
                || match.Groups["id"].Value != type
                || !Near(match.Groups["x"].Value, placed.Position.X)
                || !Near(match.Groups["y"].Value, placed.Position.Y))
            {
                continue;
            }

            var trimmed = lines[i].TrimStart();
            if (isItem && trimmed.StartsWith("call ", StringComparison.Ordinal))
            {
                lines[i] = $"    set {name} = {trimmed["call ".Length..]}";
                return true;
            }

            if (!isItem && trimmed.StartsWith("set u = ", StringComparison.Ordinal))
            {
                lines.Insert(i + 1, $"    set {name} = u");
                return true;
            }
        }

        return false;
    }

    private static bool Near(string text, float value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && Math.Abs(number - value) < 0.06;

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

    // A line World Editor writes after the trigger variables.
    private static bool IsAfterTriggers(string line) =>
        line.Contains(" gg_unit_", StringComparison.Ordinal)
        || line.Contains(" gg_item_", StringComparison.Ordinal)
        || line.Contains(" gg_dest_", StringComparison.Ordinal)
        || line.Contains(" gg_rg_", StringComparison.Ordinal);

    // The calls of main that depend on the triggers. False when main is not as World Editor writes it.
    private bool ReplaceMainCalls(List<string> lines, int main)
    {
        var close = Find(lines, line => line == "endfunction", main);
        if (close < 0)
        {
            return false;
        }

        foreach (var call in new[] { "    call InitCustomTriggers(  )", "    call RunInitializationTriggers(  )" })
        {
            var at = lines.IndexOf(call, main, close - main);
            if (at >= 0)
            {
                lines.RemoveAt(at);
                close--;
            }
        }

        var blizzard = lines.IndexOf("    call InitBlizzard(  )", main, close - main);
        var initGlobals = lines.IndexOf("    call InitGlobals(  )", main, close - main);
        if (blizzard < 0 && initGlobals < 0)
        {
            return false;
        }

        if (initGlobals < 0 && NeedsInitGlobals())
        {
            initGlobals = blizzard + 1;
            lines.Insert(initGlobals, "    call InitGlobals(  )");
        }

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

        return true;
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
