using System.Globalization;
using Moonwell.Generator.Objects;

namespace MoonwellEditor.GameData;

/// <summary>
/// One object's merged rows, read as the game reads them in each graphics mode. The skin files key some values by mode
/// (<c>modelScale:sd</c>, <c>modelScale:hd</c>, <c>modelScale:de</c>): Classic reads the <c>:sd</c> key, else the bare
/// key; Reforged the <c>:hd</c> key, else the bare key; Definitive Edition the <c>:de</c> key, else the <c>:hd</c> key,
/// else the bare key, as Moonwell's <c>StandardObjects</c> does. A key is looked up as spelled and, failing that,
/// without regard to case when one key matches, as Moonwell does.
/// </summary>
internal sealed class ModeRow
{
    /// <summary>The graphics modes, by the names <see cref="StandardObject.Modes"/> uses.</summary>
    public static readonly string[] Modes = ["sd", "hd", "de"];

    private readonly ObjectRows _rows;
    private readonly Dictionary<string, string?> _folded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _withModes = new(StringComparer.OrdinalIgnoreCase);

    public ModeRow(ObjectRows rows)
    {
        _rows = rows;
        foreach (var key in rows.Values.Keys)
        {
            // A key two keys spell alike without regard to case matches neither.
            _folded[key] = _folded.ContainsKey(key) ? null : key;
            var colon = key.LastIndexOf(':');
            if (colon > 0 && Modes.Contains(key.Substring(colon + 1).ToLowerInvariant()))
            {
                _withModes.Add(key.Substring(0, colon));
            }
        }
    }

    /// <summary>True when the rows hold a per-mode value of the key (<c>modelScale</c>, for <c>modelScale:hd</c>).
    /// </summary>
    public bool HasModes(string key) => _withModes.Contains(key);

    /// <summary>The key's value in the mode and whether the text file wrapped it in quotes; null for none.</summary>
    public (string Value, bool Wrapped)? Get(string key, string mode)
    {
        var spelled = mode switch
        {
            "sd" => Spelled(key + ":sd") ?? Spelled(key),
            "hd" => Spelled(key + ":hd") ?? Spelled(key),
            _ => Spelled(key + ":de") ?? Spelled(key + ":hd") ?? Spelled(key),
        };
        if (spelled == null)
        {
            return null;
        }

        return (_rows.Values[spelled], _rows.Wrapped != null && _rows.Wrapped.Contains(spelled));
    }

    private string? Spelled(string key) => _rows.Values.ContainsKey(key) ? key : _folded.GetValueOrDefault(key);
}

/// <summary>
/// How one field's values are found in an object's rows, as Moonwell's private <c>FieldPlan.Read</c> finds them: one
/// value, one element of a comma list (<c>Buttonpos[0]</c>), a value per level from a column per level
/// (<c>Cool{level}</c>), from a comma list (a tooltip), or from a column per level with the level appended
/// (<c>Requires</c>, <c>Requires1</c>).
/// </summary>
internal sealed class FieldReader
{
    private readonly string _id;
    private readonly int _element = -1;
    private readonly bool _perLevelColumn;
    private readonly bool _list;
    private readonly bool _appended;

    public FieldReader(Field field, MetaRow meta)
    {
        _id = field.Id;
        Column = field.Column;
        if (Column.EndsWith(']'))
        {
            var open = Column.LastIndexOf('[');
            var digits = Column.AsSpan(open + 1, Column.Length - open - 2);
            _element = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
            Column = Column.Substring(0, open);
        }

        _perLevelColumn = Column.Contains("{level}", StringComparison.Ordinal);
        _list = field.PerLevel && !_perLevelColumn;
        _appended = meta.Columns.TryGetValue("appendIndex", out var append) && append.Trim() == "1";
    }

    /// <summary>The key the field's value is read from, without an element's <c>[n]</c>.</summary>
    public string Column { get; }

    /// <summary>
    /// Adds the field's values in the mode, keyed as <see cref="StandardObject.Values"/> is: one, or one for each
    /// level up to <paramref name="levels"/> (null: a kind without a level count, whose lists have a value for each
    /// entry).
    /// </summary>
    public void Read(ModeRow row, string mode, int? levels, Dictionary<string, string> values)
    {
        if (_perLevelColumn)
        {
            for (var level = 1; level <= (levels ?? 0); level++)
            {
                var number = level.ToString(CultureInfo.InvariantCulture);
                var key = Column.Replace("{level}", number, StringComparison.Ordinal);
                Add(values, level, Clean(row.Get(key, mode)?.Value));
            }
        }
        else if (_list && _appended)
        {
            for (var level = 1; level <= (levels ?? 0); level++)
            {
                var key = level == 1 ? Column : Column + (level - 1).ToString(CultureInfo.InvariantCulture);
                Add(values, level, Clean(row.Get(key, mode)?.Value));
            }
        }
        else if (_list)
        {
            var entries = Entries(row.Get(Column, mode));
            for (var i = 0; entries != null && i < entries.Count && (levels == null || i < levels); i++)
            {
                Add(values, i + 1, entries[i]);
            }
        }
        else if (_element >= 0)
        {
            var entries = Entries(row.Get(Column, mode));
            if (entries != null && _element < entries.Count)
            {
                Add(values, 0, entries[_element]);
            }
        }
        else
        {
            Add(values, 0, Clean(row.Get(Column, mode)?.Value));
        }
    }

    private void Add(Dictionary<string, string> values, int level, string? value)
    {
        if (value != null)
        {
            values[level == 0 ? _id : _id + ":" + level.ToString(CultureInfo.InvariantCulture)] = value;
        }
    }

    /// <summary>A cell's value, trimmed: null when empty or a table's placeholder <c>-</c> or <c>_</c>.</summary>
    public static string? Clean(string? cell)
    {
        var value = cell?.Trim();
        return string.IsNullOrEmpty(value) || value == "-" || value == "_" ? null : value;
    }

    /// <summary>
    /// The entries of a comma list, null when there is no value; an empty entry is null. A value the text file wrapped
    /// in quotes is one entry; an entry that starts with a quote ends at the next quote, so it may hold commas.
    /// </summary>
    private static List<string?>? Entries((string Value, bool Wrapped)? cell)
    {
        if (cell is not { } found)
        {
            return null;
        }

        if (found.Wrapped)
        {
            return [Clean(found.Value)];
        }

        var text = found.Value;
        var entries = new List<string?>();
        var i = 0;
        while (true)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }

            string entry;
            var close = i < text.Length && text[i] == '"' ? text.IndexOf('"', i + 1) : -1;
            if (close >= 0)
            {
                entry = text.Substring(i + 1, close - i - 1);
                var comma = text.IndexOf(',', close + 1);
                i = comma < 0 ? text.Length : comma;
            }
            else
            {
                var comma = text.IndexOf(',', i);
                var end = comma < 0 ? text.Length : comma;
                entry = text.Substring(i, end - i);
                i = end;
            }

            entries.Add(Clean(entry));
            if (i >= text.Length)
            {
                return entries;
            }

            i++;
        }
    }
}
