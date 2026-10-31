using MoonwellEditor.Files;
using War3Net.Build;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Script;
using War3Net.CodeAnalysis;

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
        var custom = BannerOf(lines, "Custom Script Code");
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
            lines.InsertRange(initGlobals, Text(writer => GenerateInitGlobals(_map, writer)));
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
            GenerateTriggerVariables(_map, writer);
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
                GenerateUserDefinedVariables(_map, writer);
                if (generated >= 0)
                {
                    writer.WriteLine();
                }
            }
        });
        lines.InsertRange(position, block);
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
        if (ShouldGenerateRunInitializationTriggers(_map))
        {
            lines.Insert(place, "    call RunInitializationTriggers(  )");
        }

        if (ShouldGenerateInitCustomTriggers(_map))
        {
            lines.Insert(place, "    call InitCustomTriggers(  )");
        }
    }

    private List<string> CustomSection()
    {
        var code = _document.HeaderCode.Replace("\r\n", "\n");
        var lines = new List<string>();
        if (code.Length > 0)
        {
            lines.AddRange(code.TrimEnd('\n').Split('\n'));
        }

        lines.Add("");
        return lines;
    }

    private List<string> TriggerSection()
    {
        var lines = new List<string> { "" };
        var triggers = _document.Triggers;
        foreach (var definition in triggers.TriggerItems.OfType<TriggerDefinition>())
        {
            if (definition.Type == TriggerItemType.Comment || !ShouldRenderTrigger(_map, definition))
            {
                continue;
            }

            _rendering = definition.Name;
            lines.AddRange(definition.IsCustomTextTrigger ? CustomTrigger(definition) : Text(writer =>
                new TriggerRenderer(writer, TriggerData, triggers.Variables).RenderTrigger(definition)));
            lines.Add("");
        }

        if (ShouldGenerateInitCustomTriggers(_map))
        {
            lines.Add(Line);
            lines.AddRange(Text(writer => GenerateInitCustomTriggers(_map, writer)));
            lines.Add("");
        }

        if (ShouldGenerateRunInitializationTriggers(_map))
        {
            lines.Add(Line);
            lines.AddRange(Text(writer => GenerateRunInitializationTriggers(_map, writer)));
            lines.Add("");
        }

        return lines;
    }

    private List<string> CustomTrigger(TriggerDefinition definition)
    {
        var lines = new List<string> { Line, "// Trigger: " + definition.Name };
        if (!string.IsNullOrEmpty(definition.Description))
        {
            lines.Add("//");
            lines.AddRange(definition.Description.Replace("\r\n", "\n").Split('\n').Select(line => "// " + line));
        }

        lines.Add(Line);
        lines.AddRange((_document.CodeOf(definition.Id) ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
        return lines;
    }
}
