using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Script;
using War3Net.Build.Widget;
using War3Net.CodeAnalysis;
using War3Net.Common.Extensions;
using War3Net.CodeAnalysis.Jass;

namespace MoonwellEditor.Triggers.Tests;

public sealed class TriggerScriptTests(ITestOutputHelper output)
{
    // World Editor's versions space parentheses differently, which does not change the code.
    private static string Normal(string script)
    {
        var lines = Regex.Replace(script.Replace("\r\n", "\n"), @"[ \t]+\n", "\n").Trim();

        // A map saved by an older World Editor has no unused local in InitGlobals.
        lines = lines.Replace("nothing\n    local integer i = 0\n    set udg_", "nothing\n    set udg_");
        // Nor do blank lines, or the empty lines of a trigger's comment.
        lines = Regex.Replace(lines, @"\n//(?=\n)", "");
        lines = Regex.Replace(lines, @"\n{3,}", "\n\n");
        return Regex.Replace(Regex.Replace(lines, @"\([ ]+", "("), @"[ ]+\)", ")");
    }

    private static TriggerDocument Read(MoonwellEditor.Files.IMapStore store, TriggerCatalog catalog) =>
        TriggerDocument.Read(store.Read(TriggerDocument.TriggersFile), store.Read(TriggerDocument.CustomTextFile), catalog);

    private static MapUnits Units(MoonwellEditor.Files.IMapStore store)
    {
        using var reader = new BinaryReader(new MemoryStream(store.Read(MapUnits.FileName)!));
        return reader.ReadMapUnits();
    }

    private static MapInfo Info(MoonwellEditor.Files.IMapStore store)
    {
        using var reader = new BinaryReader(new MemoryStream(store.Read(MapInfo.FileName)!));
        return reader.ReadMapInfo();
    }

    [Fact]
    public void A_custom_script_goes_with_the_maps_own_and_has_no_trigger_function()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.WarChasers);
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = Read(store, catalog);
        var category = document.Tree().First(item => item.Kind == "category").Id;
        var script = document.Add("script", category);
        document.SetCode(script, "function Hello takes nothing returns nothing\nendfunction");
        document.SetHeader("", "function Top takes nothing returns nothing\nendfunction");

        var written = TriggerScript.Write(Encoding.UTF8.GetString(store.Read("war3map.j")!), document, Info(store), Units(store), out var problem);

        Assert.Null(problem);
        Assert.True(written!.IndexOf("function Top", StringComparison.Ordinal) < written.IndexOf("function Hello", StringComparison.Ordinal));
        Assert.True(written.IndexOf("function Hello", StringComparison.Ordinal) < written.IndexOf("//*  Sound Assets", StringComparison.Ordinal)
            || !written.Contains("//*  Sound Assets", StringComparison.Ordinal));
        Assert.DoesNotContain("InitTrig_Untitled_Script", written);
        Assert.DoesNotContain("gg_trg_Untitled_Script", written);
    }

    [Fact]
    public void Every_function_with_its_defaults_writes_jass_that_calls_only_what_the_game_has()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var known = new HashSet<string>(StringComparer.Ordinal) { "function", "if", "not", "and", "or" };
        foreach (var file in new[] { @"Scripts\common.j", @"Scripts\Blizzard.j" })
        {
            var text = Encoding.UTF8.GetString(InstallMaps.ReadGame(file)!);
            foreach (Match match in Regex.Matches(text, @"(?:native|function)\s+(\w+)\s+takes"))
            {
                known.Add(match.Groups[1].Value);
            }
        }

        var document = Read(store, catalog);
        var id = document.Add("trigger", document.Tree().First(item => item.Kind == "category").Id);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var unparsed = new List<string>();
        var written = 0;
        var unset = 0;
        foreach (var function in catalog.Functions.Where(each => each.Kind != "call"))
        {
            var before = document.Snapshot();
            document.AddFunction(id, null, 0, 0, function.Kind, function.Name);
            var definition = document.Triggers.TriggerItems.OfType<TriggerDefinition>().Single(each => each.Id == id);
            string jass;
            try
            {
                using var text = new StringWriter();
                text.NewLine = "\n";
                using var writer = new IndentedTextWriter(text);
                new TriggerJass(writer, document).Write(definition);
                writer.Flush();
                jass = text.ToString();
            }
            catch (InvalidDataException)
            {
                unset++; // the game gives it no default for a parameter
                document.Restore(before);
                continue;
            }

            document.Restore(before);
            written++;
            if (!JassSyntaxFactory.TryParseCompilationUnit(jass, out _))
            {
                unparsed.Add(function.Name);
            }

            var strings = Regex.Replace(jass, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");
            foreach (Match match in Regex.Matches(strings, @"\b([A-Za-z]\w*)\s*\("))
            {
                var name = match.Groups[1].Value;
                if (!known.Contains(name) && !name.StartsWith("Trig_", StringComparison.Ordinal))
                {
                    missing.Add($"{function.Name} calls {name}");
                }
            }
        }

        output.WriteLine($"{written} written, {unset} need a value first");
        output.WriteLine("not parsed: " + string.Join(", ", unparsed.Take(30)));
        output.WriteLine("missing: " + string.Join("; ", missing.Take(40)));
        Assert.True(written > 400);
        Assert.Empty(unparsed);
        Assert.Empty(missing);
    }

    private static (TriggerDocument Document, string Script, MapInfo Info, MapUnits Units)? Map(string path)
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(path);
        return catalog == null || store == null
            ? null
            : (Read(store, catalog), Encoding.UTF8.GetString(store.Read("war3map.j") ?? store.Read(@"scripts\war3map.j")!), Info(store), Units(store));
    }

    [Fact]
    public void A_unit_named_for_the_first_time_gets_its_variable_and_is_set_where_it_is_made()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var placed = units.Units.First(each => each.HeroLevel > 0 && script.Contains($"'{each.TypeId.ToRawcode()}'", StringComparison.Ordinal));
        var name = $"gg_unit_{placed.TypeId.ToRawcode()}_{placed.CreationNumber:D4}";
        Assert.DoesNotContain(name, script);
        var id = document.Add("trigger", document.Tree().First(item => item.Kind == "category").Id);
        var kill = document.AddFunction(id, null, 0, 0, "action", "KillUnit");
        document.SetParameter(id, kill + "/p0", new ParamInput("variable", name));

        var written = TriggerScript.Write(script, document, info, units, out var problem);

        Assert.Null(problem);
        var lines = written!.Replace("\r\n", "\n").Split('\n');
        Assert.Single(lines, line => line.Trim().StartsWith("unit", StringComparison.Ordinal) && line.Contains(name, StringComparison.Ordinal) && line.EndsWith("= null", StringComparison.Ordinal));
        var set = Array.IndexOf(lines, $"    set {name} = u");
        Assert.True(set > 0);
        Assert.Contains($"'{placed.TypeId.ToRawcode()}'", lines[set - 1]);
        Assert.Contains($"call KillUnit( {name} )", written);
    }

    [Fact]
    public void A_destructable_named_for_the_first_time_leaves_the_script_as_it_is_and_says_why()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", document.Tree().First(item => item.Kind == "category").Id);
        var kill = document.AddFunction(id, null, 0, 0, "action", "KillDestructable");
        document.SetParameter(id, kill + "/p0", new ParamInput("variable", "gg_dest_LTlt_9999"));

        var written = TriggerScript.Write(script, document, info, units, out var problem);

        Assert.Null(written);
        Assert.Contains("gg_dest_LTlt_9999", problem);
    }

    [Fact]
    public void A_placed_thing_the_map_does_not_have_is_a_problem()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", document.Tree().First(item => item.Kind == "category").Id);
        var kill = document.AddFunction(id, null, 0, 0, "action", "KillUnit");
        document.SetParameter(id, kill + "/p0", new ParamInput("variable", "gg_unit_hfoo_9999"));

        Assert.Null(TriggerScript.Write(script, document, info, units, out var problem));
        Assert.Contains("gg_unit_hfoo_9999", problem);
    }

    [Fact]
    public void Trigger_variables_are_kept_when_only_a_user_variable_of_a_unit_type_was_there()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, _, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        const string Script = """
            globals
                // User-defined
                unit                    udg_Hero                   = null
            endglobals

            function InitGlobals takes nothing returns nothing
            endfunction

            //***************************************************************************
            //*
            //*  Custom Script Code
            //*
            //***************************************************************************

            //***************************************************************************
            //*
            //*  Triggers
            //*
            //***************************************************************************

            //***************************************************************************
            //*
            //*  Main Initialization
            //*
            //***************************************************************************
            function main takes nothing returns nothing
                call InitBlizzard(  )
                call InitGlobals(  )
            endfunction
            """;
        var id = document.Add("trigger", document.Tree().First(item => item.Kind == "category").Id);
        var variable = new VariableInput("Hero", "unit", false, 1, "");
        document.AddVariable(variable, document.Tree().First(item => item.Kind == "category").Id);

        var written = TriggerScript.Write(Script.Replace("\r\n", "\n"), document, info, units, out var problem);

        Assert.Null(problem);
        Assert.Contains("gg_trg_Untitled_Trigger_001", written!.Split("endglobals")[0]);
        Assert.Contains("gg_trg_Melee_Initialization", written.Split("endglobals")[0]);
        Assert.Contains("udg_Hero", written.Split("endglobals")[0]);
        Assert.Single(Regex.Matches(written, "// User-defined"));
        Assert.Single(Regex.Matches(written, "// Generated"));
        Assert.True(id > 0);
    }

    [Fact]
    public void Writing_again_gives_the_same_script_with_banners_in_the_custom_code()
    {
        if (Map(InstallMaps.WarChasers) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        document.SetHeader("", """
            //***************************************************************************
            //*
            //*  Pasted From Blizzard.j
            //*
            //***************************************************************************
            function Top takes nothing returns nothing
            endfunction
            """.Replace("\r\n", "\n"));

        var once = TriggerScript.Write(script, document, info, units, out var problem);
        var twice = TriggerScript.Write(once!, document, info, units, out var again);

        Assert.Null(problem);
        Assert.Null(again);
        Assert.Equal(once, twice);
        Assert.Single(Regex.Matches(once!, "function Top "));
    }

    [Fact]
    public void A_main_not_as_World_Editor_writes_it_is_a_problem()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var changed = script.Replace("call InitBlizzard(  )", "call InitBlizzard()", StringComparison.Ordinal);
        var withoutGlobals = changed.Replace("call InitGlobals(  )", "call InitGlobals()", StringComparison.Ordinal);

        Assert.Null(TriggerScript.Write(withoutGlobals, document, info, units, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void Variables_added_to_a_script_with_no_InitGlobals_get_it()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var start = script.IndexOf("function InitGlobals takes", StringComparison.Ordinal);
        var without = start < 0
            ? script
            : script[..start] + script[(script.IndexOf("endfunction", start, StringComparison.Ordinal) + "endfunction".Length)..];
        without = without.Replace("    call InitGlobals(  )\r\n", "", StringComparison.Ordinal).Replace("    call InitGlobals(  )\n", "", StringComparison.Ordinal);
        document.AddVariable(new VariableInput("Timer", "timer", false, 1, ""), document.Tree().First(item => item.Kind == "category").Id);

        var written = TriggerScript.Write(without, document, info, units, out var problem);

        Assert.Null(problem);
        Assert.Contains("function InitGlobals takes", written);
        Assert.Contains("set udg_Timer = CreateTimer()", written);
        Assert.Contains("call InitGlobals(  )", written);
    }

    [Fact]
    public void A_map_with_vjass_keeps_its_script()
    {
        if (Map(InstallMaps.LostTemple) is not var (document, script, info, units))
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        document.SetHeader("", "library Hello\nendlibrary");

        Assert.Null(TriggerScript.Write(script, document, info, units, out var problem));
        Assert.Contains("vJass", problem);
    }

    [Fact]
    public void A_parameter_with_no_value_is_a_problem_that_names_its_trigger()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.WarChasers);
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = Read(store, catalog);
        var category = document.Tree().First(item => item.Kind == "category").Id;
        var id = document.Add("trigger", category);
        document.AddFunction(id, null, 0, 0, "action", "SetVariable");

        var written = TriggerScript.Write(Encoding.UTF8.GetString(store.Read("war3map.j")!), document, Info(store), Units(store), out var problem);

        Assert.Null(written);
        Assert.Contains("Untitled Trigger 001", problem);
    }

    [Fact]
    public void A_map_that_was_not_edited_gets_its_own_script_back()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var maps = 0;
        var same = 0;
        var kinds = new Dictionary<string, int>();
        foreach (var path in InstallMaps.All())
        {
            var store = InstallMaps.Open(path)!;
            var info = Info(store);
            if (info.ScriptLanguage != ScriptLanguage.Jass)
            {
                continue;
            }

            var bytes = store.Read("war3map.j") ?? store.Read(@"scripts\war3map.j");
            if (bytes == null)
            {
                continue;
            }

            maps++;
            var original = Encoding.UTF8.GetString(bytes);
            var document = Read(store, catalog);
            var built = TriggerScript.Write(original, document, info, null, out var problem);
            if (built != null && Normal(built) == Normal(original))
            {
                same++;
                continue;
            }

            var left = Normal(original).Split('\n');
            var right = built == null ? [] : Normal(built).Split('\n');
            var line = 0;
            while (line < left.Length && line < right.Length && left[line] == right[line])
            {
                line++;
            }

            var key = problem ?? $"[{(line < left.Length ? left[line] : "")}] [{(line < right.Length ? right[line] : "")}]";
            key = key.Length > 170 ? key[..170] : key;
            kinds[key] = kinds.GetValueOrDefault(key) + 1;
            if (kinds[key] == 1)
            {
                output.WriteLine($"{path}: line {line}");
                var from = Math.Max(0, line - 3);
                output.WriteLine("  ORIG " + string.Join(" ~ ", left.Skip(from).Take(7)));
                output.WriteLine("  OURS " + string.Join(" ~ ", right.Skip(from).Take(7)));
            }
        }

        output.WriteLine($"{same} of {maps} the same");
        foreach (var (key, count) in kinds.OrderByDescending(pair => pair.Value).Take(30))
        {
            output.WriteLine($"{count}: {key}");
        }

        // Skibi's Castle TD has an action with hidden children left from a function it was changed from; World Editor
        // writes an unused helper for them.
        Assert.True(maps > 200);
        Assert.Equal(maps - 1, same);
    }
}
