using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Script;
using War3Net.CodeAnalysis;
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

        var written = TriggerScript.Write(Encoding.UTF8.GetString(store.Read("war3map.j")!), document, Info(store), out var problem);

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

        var written = TriggerScript.Write(Encoding.UTF8.GetString(store.Read("war3map.j")!), document, Info(store), out var problem);

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
            var built = TriggerScript.Write(original, document, info, out var problem);
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
