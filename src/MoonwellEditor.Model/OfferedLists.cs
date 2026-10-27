using System.Text;
using MoonwellEditor.Files;
using MoonwellEditor.GameData;
using War3Net.IO.Slk;

namespace MoonwellEditor.Model;

/// <summary>
/// The lists World Editor offers for fields the data keeps as text (Sven, 2026-10-27): a unit's ground texture is one
/// of the game's splats (<c>Splats\UberSplatData.slk</c>, by World Editor's names for them), and a building's or a
/// destructable's shadow is one of the shadows the standard buildings and destructables use, listed under those two
/// headings by the objects that use each.
/// </summary>
public static class OfferedLists
{
    /// <summary>The heading of the shadows the standard units (the buildings) use.</summary>
    public const string Buildings = "Buildings";

    /// <summary>The heading of the shadows the standard destructables use.</summary>
    public const string Destructables = "Destructibles";

    // A shadow is named by the objects that use it, cut after this many characters (with ", ...").
    private const int NamesLimit = 48;

    /// <summary>
    /// The ground textures: None, then each splat of the table by World Editor's name for it (the
    /// <c>WESTRING_UBERSPLAT_</c> string of its code), by name; a splat with no name shows its code. The table's first
    /// row, INIT, is not a splat. Null when the table cannot be read.
    /// </summary>
    public static ChoiceList? Splats(byte[]? table, IReadOnlyDictionary<string, string> strings)
    {
        if (table == null)
        {
            return null;
        }

        SylkTable sylk;
        try
        {
            using var stream = new MemoryStream(table);
            sylk = new SylkParser(keepText: true).Parse(stream);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException)
        {
            return null;
        }

        var entries = new List<ChoiceEntry>();
        for (var y = 1; y <= sylk.Rows; y++)
        {
            if (sylk[0, y]?.ToString()?.Trim() is not { Length: > 0 } code || code == "INIT")
            {
                continue;
            }

            entries.Add(new ChoiceEntry(code, strings.GetValueOrDefault("WESTRING_UBERSPLAT_" + code, code), []));
        }

        return new ChoiceList(
            false,
            "string",
            [None, .. entries.OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Value)]);
    }

    /// <summary>
    /// The shadows: None, then the standard units' building shadows under <see cref="Buildings"/> and the standard
    /// destructables' shadows under <see cref="Destructables"/>, each once (as the first object that uses it spells
    /// it), named by the objects that use it and listed by that name.
    /// </summary>
    public static ChoiceList Shadows(GameData.GameData data)
    {
        var entries = new List<ChoiceEntry> { None };
        foreach (var (kind, field, heading) in new[]
                 {
                     (ObjectKind.Unit, "ushb", Buildings), (ObjectKind.Destructable, "bshd", Destructables),
                 })
        {
            var kindData = data.Kind(kind);
            if (kindData.Field(field) == null)
            {
                continue;
            }

            var byFile = new Dictionary<string, (string Value, List<string> Names)>(StringComparer.Ordinal);
            foreach (var standard in kindData.Objects.OrderBy(each => each.Row))
            {
                var value = standard.Values.GetValueOrDefault(field, "").Trim();
                if (value.Length == 0 || value == "_")
                {
                    continue;
                }

                var key = AssetNames.Key(value);
                if (!byFile.TryGetValue(key, out var file))
                {
                    byFile[key] = file = (value, []);
                }

                var bare = standard.Name.Length > 0 ? standard.Name : standard.Id;
                var name = standard.Suffix.Length > 0 ? $"{bare} {standard.Suffix}" : bare;
                if (!file.Names.Contains(name))
                {
                    file.Names.Add(name);
                }
            }

            entries.AddRange(byFile.Values
                .Select(file => new ChoiceEntry(file.Value, NamesText(file.Names), [], heading))
                .OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase));
        }

        return new ChoiceList(false, "string", entries);
    }

    /// <summary>
    /// World Editor's strings from a <c>WorldEditStrings.txt</c>: each <c>KEY=Text</c> line, its text without
    /// surrounding quotes; the first of a key that repeats counts.
    /// </summary>
    public static Dictionary<string, string> Strings(byte[]? text)
    {
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (text == null)
        {
            return strings;
        }

        foreach (var line in Encoding.UTF8.GetString(text).Split('\n'))
        {
            var equals = line.IndexOf('=');
            if (equals <= 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('['))
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            strings.TryAdd(line[..equals].Trim(), value);
        }

        return strings;
    }

    private static ChoiceEntry None => new("", "None", []);

    private static string NamesText(List<string> names)
    {
        var text = "";
        foreach (var name in names)
        {
            var next = text.Length == 0 ? name : $"{text}, {name}";
            if (text.Length > 0 && next.Length > NamesLimit)
            {
                return text + ", ...";
            }

            text = next;
        }

        return text;
    }
}
