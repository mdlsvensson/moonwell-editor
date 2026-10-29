using MoonwellEditor.Files;

namespace MoonwellEditor.Model;

/// <summary>Which objects differ from the files as they were opened or last saved.</summary>
public sealed partial class Session
{
    // Every object the map has records of, as Describe writes it, at open, reload or the last save.
    private Dictionary<string, string> _savedObjects;

    /// <summary>
    /// How many objects differ from the map as it was opened, reloaded or last saved: a custom object that is new,
    /// deleted or has other modifications, and a standard object whose modifications are other. A <c>TRIGSTR_n</c>
    /// text counts by the text it stands for, so an object whose name changed only in <c>war3map.wts</c> counts too.
    /// An edit that was changed back counts for nothing, even though <see cref="Dirty"/> is then still true.
    /// </summary>
    public int ChangedObjects()
    {
        var now = Describe();
        var changed = now.Count(pair => _savedObjects.GetValueOrDefault(pair.Key) != pair.Value);
        changed += _savedObjects.Keys.Count(key => !now.ContainsKey(key));
        return changed;
    }

    /// <summary>
    /// Every object the map has records of, by kind and id, as one line of text: for each of its entries (main file
    /// first) the file, a custom object's base, and its modifications sorted. Two equal lines mean equal records.
    /// A standard object with no modifications is left out, as it differs from nothing.
    /// </summary>
    private Dictionary<string, string> Describe()
    {
        var objects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var map = _kinds[kind];
            var key = Kinds.Key(kind);
            foreach (var (file, side) in new[] { (map.Main, "main"), (map.Skin, "skin") })
            {
                foreach (var entry in file?.Custom ?? [])
                {
                    Add(objects, $"{key} custom {entry.Id}", $"{side} from {entry.BaseId}: {Describe(entry)}");
                }

                foreach (var entry in file?.Original ?? [])
                {
                    if (entry.Mods.Count > 0)
                    {
                        Add(objects, $"{key} standard {entry.BaseId}", $"{side}: {Describe(entry)}");
                    }
                }
            }
        }

        return objects;
    }

    /// <summary>An entry's modifications, sorted, with a <c>TRIGSTR_n</c> text as the text it stands for.</summary>
    private string Describe(ObjectEntry entry)
    {
        var mods = entry.Mods
            .Select(mod => $"{mod.Field} {mod.Level} {mod.Pointer} {Describe(mod.Value)}")
            .Order(StringComparer.Ordinal);
        return string.Join(" | ", mods);
    }

    /// <summary>
    /// A value with its type. A real number is written as its bits, so only an equal number is equal; a text is
    /// written with its length first, so no text can look like the end of one and the start of the next.
    /// </summary>
    private string Describe(FieldValue value)
    {
        switch (value)
        {
            case FieldValue.Int whole:
                return "int " + whole.Value;
            case FieldValue.Real real:
                return "real " + BitConverter.SingleToInt32Bits(real.Value);
            case FieldValue.Unreal unreal:
                return "unreal " + BitConverter.SingleToInt32Bits(unreal.Value);
            case FieldValue.Text text:
                var number = WtsFile.TriggerString(text.Value);
                var shown = number == null ? text.Value : _strings?.Get(number.Value) ?? text.Value;
                return $"text {shown.Length}:{shown}";
            default:
                return value.ToString();
        }
    }

    /// <summary>Adds a line to an object's description, after what it has so far.</summary>
    private static void Add(Dictionary<string, string> objects, string key, string line)
    {
        objects[key] = objects.TryGetValue(key, out var before) ? before + " || " + line : line;
    }
}
