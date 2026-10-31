using System.Text;
using System.Text.RegularExpressions;
using War3Net.Build.Extensions;
using War3Net.Build.Info;

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
