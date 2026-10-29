using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MoonwellEditor.Files;

/// <summary>
/// The editor's own text for what the app shows (its errors, World Editor's limits, the tree's folders, its window),
/// from the page's strings file (<c>web/src/text/strings.json</c>, built into this assembly), so every text the editor
/// shows is edited in one place (Sven, 2026-10-29). The app's keys start with <c>app.</c>; a placeholder is written
/// <c>{name}</c>, as the page's are.
/// </summary>
public static partial class Texts
{
    private static readonly Lazy<Dictionary<string, string>> All = new(Load);

    /// <summary>
    /// The key's text with its placeholders filled from <paramref name="values"/> (numbers as the invariant culture
    /// writes them); a placeholder with no value stays as written, and a value's own braces are not filled again.
    /// Throws when the strings file has no such key.
    /// </summary>
    public static string Get(string key, params (string Name, object? Value)[] values)
    {
        if (!All.Value.TryGetValue(key, out var text))
        {
            throw new KeyNotFoundException($"The strings file has no text {key}.");
        }

        return values.Length == 0
            ? text
            : Placeholder().Replace(text, match =>
            {
                foreach (var (name, value) in values)
                {
                    if (name == match.Groups[1].Value)
                    {
                        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    }
                }

                return match.Value;
            });
    }

    /// <summary>Whether the strings file has the key.</summary>
    public static bool Has(string key) => All.Value.ContainsKey(key);

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(Texts).Assembly.GetManifestResourceStream("strings.json")
            ?? throw new InvalidOperationException("The strings file is not built into the app.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("The strings file is empty.");
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
