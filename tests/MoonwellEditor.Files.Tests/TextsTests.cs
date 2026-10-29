using System.Text.Json;
using System.Text.RegularExpressions;

namespace MoonwellEditor.Files.Tests;

/// <summary>The app's own text, read from the page's strings file.</summary>
public sealed partial class TextsTests
{
    [Fact]
    public void Fills_the_placeholders_given_and_leaves_the_rest()
    {
        Assert.Equal("There is no unit hfoo.", Texts.Get("app.error.noObject", ("kind", "unit"), ("id", "hfoo")));
        Assert.Equal("There is no unit {id}.", Texts.Get("app.error.noObject", ("kind", "unit")));
        Assert.Equal("Above World Editor's limit of 1.5.", Texts.Get("app.limit.above", ("limit", 1.5)));
        Assert.Equal("There is no unit {kind}.", Texts.Get("app.error.noObject", ("kind", "unit"), ("id", "{kind}")));
    }

    [Fact]
    public void A_missing_key_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Texts.Get("app.no.such"));
    }

    [Fact]
    public void Every_key_the_code_asks_for_is_in_the_file_and_every_app_key_is_asked_for()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(root, "web", "src", "text", "strings.json")))!;
        var asked = new HashSet<string>(StringComparer.Ordinal);
        // The app's code, and the page's (which shares app.name and app.windowTitle).
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "web", "src"), "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".svelte", StringComparison.Ordinal)
                    || (file.EndsWith(".ts", StringComparison.Ordinal)
                        && !file.EndsWith(".test.ts", StringComparison.Ordinal))));
        foreach (var file in files)
        {

            var code = File.ReadAllText(file);
            foreach (Match match in Asked().Matches(code))
            {
                asked.Add(match.Groups[1].Value);
            }

            // The folders' names are asked for by their last part (Folders.F).
            foreach (Match match in Folder().Matches(code))
            {
                asked.Add("app.folder." + match.Groups[1].Value);
            }
        }

        foreach (var key in asked)
        {
            Assert.True(strings.ContainsKey(key), $"The strings file has no {key}.");
        }

        foreach (var key in strings.Keys.Where(key => key.StartsWith("app.", StringComparison.Ordinal)))
        {
            Assert.True(asked.Contains(key), $"No code asks for {key}.");
        }
    }

    // A key in a call, on its line or the next: Texts.Get, the page's t, tCount and tParts, or the unsaved texts
    // RefuseUnsaved takes.
    [GeneratedRegex(@"(?:Texts\.Get\(|\bt\(|tCount\(|tParts\(|RefuseUnsaved\(discard, )\s*""(app\.[a-zA-Z.]*[a-zA-Z])""")]
    private static partial Regex Asked();

    [GeneratedRegex(@"\bF\(""(\w+)""\)")]
    private static partial Regex Folder();
}
