using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>One search result: an object, or a field of a kind.</summary>
/// <param name="Kind">The kind's key (<c>unit</c>), as <see cref="Kinds.Key"/> names it.</param>
/// <param name="Id">The object's id, or for a field match the field's id.</param>
/// <param name="Name">The object's name, or for a field match the field's label.</param>
/// <param name="Detail">
/// A short line to show beside the name: where a standard object sits in the tree (<c>Human · Melee · Units</c>),
/// <c>Custom · Footman</c> for a custom object, <c>Hit Points (Base) · uhpm</c> for a field.
/// </param>
/// <param name="Field">The field's id for a field match; null for an object.</param>
public sealed record Hit(string Kind, string Id, string Name, string Detail, string? Field);

public sealed partial class Session
{
    // Built at open and after Reload, then kept up to date by the edits (see Edits.cs).
    private readonly SearchIndex _search;

    /// <summary>
    /// Searches the objects of every kind, and the kinds' fields. Every word of <paramref name="query"/> (split at
    /// spaces, ignoring case) has to be found: in an object's name, suffix or id, where a standard object sits in the
    /// tree (its race, <c>Melee</c>, <c>Units</c>) or what a custom object is based on, or in a value the map sets
    /// for a custom object or a changed standard one; or, for a field, in its label or id (the hit then has
    /// <see cref="Hit.Field"/> set, one for each field of each kind). The order is: the exact id, names that start
    /// with the first word, names that hold every word, other object matches, then fields; a custom object before a
    /// standard one at equal rank.
    /// </summary>
    /// <param name="query">What to look for; an empty query finds nothing.</param>
    /// <param name="limit">The most hits to return.</param>
    public IReadOnlyList<Hit> Search(string query, int limit) => _search.Find(query, limit);
}

/// <summary>
/// What the search looks in: for each kind, one lowercase entry for each object and each field, so a query only
/// compares text. An object's entry is made again when an edit touches the object.
/// </summary>
internal sealed class SearchIndex
{
    // The ranks: an object's, then a field's.
    private const int IdExact = 0;
    private const int NamePrefix = 1;
    private const int NameContains = 2;
    private const int ObjectOther = 3;
    private const int FieldIdExact = 10;
    private const int FieldLabelPrefix = 11;
    private const int FieldOther = 12;

    private readonly GameData.GameData _game;
    private readonly Dictionary<ObjectKind, Dictionary<string, ObjectEntryText>> _objects = [];
    private readonly List<FieldEntryText> _fields = [];

    public SearchIndex(GameData.GameData game, Dictionary<ObjectKind, MapKind> maps, WtsFile? strings)
    {
        _game = game;
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            foreach (var field in game.Kind(kind).Fields)
            {
                _fields.Add(new FieldEntryText(
                    kind,
                    new Hit(Kinds.Key(kind), field.Id, field.Label, $"{field.Label} · {field.Id}", field.Id),
                    field.Id.ToLowerInvariant(),
                    field.Label.ToLowerInvariant()));
            }
        }

        Rebuild(maps, strings);
    }

    /// <summary>Makes every object's entry again, from the map as it is now.</summary>
    public void Rebuild(Dictionary<ObjectKind, MapKind> maps, WtsFile? strings)
    {
        _objects.Clear();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var map = maps[kind];
            var data = _game.Kind(kind);
            var resolver = new Resolver(kind, data, strings);
            var entries = new Dictionary<string, ObjectEntryText>(StringComparer.Ordinal);
            foreach (var custom in map.Customs())
            {
                entries[custom.Id] = Make(kind, data, resolver, data.Object(custom.BaseId), custom, custom.Id);
            }

            var originals = map.Originals();
            foreach (var standard in data.Objects)
            {
                entries[standard.Id] = Make(kind, data, resolver, standard, originals.GetValueOrDefault(standard.Id), standard.Id);
            }

            _objects[kind] = entries;
        }
    }

    /// <summary>Makes one object's entry again (or drops it, for a custom object that is gone).</summary>
    public void Update(ObjectKind kind, string id, MapKind map, KindData data, WtsFile? strings)
    {
        var entries = _objects[kind];
        var found = map.Find(id);
        var standard = found is { Custom: true } ? data.Object(found.BaseId) : data.Object(id);
        if (found is not { Custom: true } && standard == null)
        {
            entries.Remove(id);
            return;
        }

        entries[id] = Make(kind, data, new Resolver(kind, data, strings), standard, found, id);
    }

    /// <summary>The hits for a query, best first; see <see cref="Session.Search"/>.</summary>
    public IReadOnlyList<Hit> Find(string query, int limit)
    {
        var words = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || limit <= 0)
        {
            return [];
        }

        var whole = string.Join(' ', words);
        var found = new List<Candidate>();
        foreach (var (kind, entries) in _objects)
        {
            foreach (var entry in entries.Values)
            {
                if (!entry.Matches(words))
                {
                    continue;
                }

                var rank = entry.IdLower == whole ? IdExact
                    : entry.NameLower.StartsWith(words[0], StringComparison.Ordinal) ? NamePrefix
                    : entry.NameHasAll(words) ? NameContains
                    : ObjectOther;
                found.Add(new Candidate(rank, entry.Custom ? 0 : 1, kind, entry.NameLower, entry.Hit));
            }
        }

        foreach (var field in _fields)
        {
            if (!field.Matches(words))
            {
                continue;
            }

            var rank = field.IdLower == whole ? FieldIdExact
                : field.LabelLower.StartsWith(words[0], StringComparison.Ordinal) ? FieldLabelPrefix
                : FieldOther;
            found.Add(new Candidate(rank, 1, field.Kind, field.LabelLower, field.Hit));
        }

        found.Sort(Candidate.Compare);
        return found.Take(limit).Select(candidate => candidate.Hit).ToList();
    }

    private static ObjectEntryText Make(
        ObjectKind kind,
        KindData data,
        Resolver resolver,
        StandardObject? standard,
        MapObject? found,
        string id)
    {
        var custom = found is { Custom: true };
        var name = resolver.Name(standard, found, id);
        var suffix = resolver.Suffix(standard, found);
        string detail;
        if (custom)
        {
            var baseName = standard == null || standard.Name.Length == 0 ? found!.BaseId : standard.Name;
            detail = Texts.Get("app.search.custom", ("base", baseName));
        }
        else
        {
            detail = string.Join(" · ", Folders.Of(kind, standard!));
        }

        // The values the map sets, for a custom object and for a standard object it changes.
        var values = new List<string>();
        foreach (var mod in (found?.Main?.Mods ?? []).Concat(found?.Skin?.Mods ?? []))
        {
            values.Add(resolver.Show(mod.Value).ToLowerInvariant());
        }

        return new ObjectEntryText(
            custom,
            new Hit(Kinds.Key(kind), id, name, detail, null),
            id.ToLowerInvariant(),
            name.ToLowerInvariant(),
            suffix.ToLowerInvariant(),
            string.Join('\n', values),
            detail.ToLowerInvariant());
    }

    /// <summary>One object's text, lowercase, and the hit it gives.</summary>
    private sealed class ObjectEntryText(
        bool custom,
        Hit hit,
        string idLower,
        string nameLower,
        string suffixLower,
        string valuesLower,
        string detailLower)
    {
        public bool Custom { get; } = custom;

        public Hit Hit { get; } = hit;

        public string IdLower { get; } = idLower;

        public string NameLower { get; } = nameLower;

        /// <summary>Whether every word is in the id, name, suffix, detail line or set values.</summary>
        public bool Matches(string[] words)
        {
            foreach (var word in words)
            {
                if (!IdLower.Contains(word, StringComparison.Ordinal)
                    && !NameLower.Contains(word, StringComparison.Ordinal)
                    && !suffixLower.Contains(word, StringComparison.Ordinal)
                    && !detailLower.Contains(word, StringComparison.Ordinal)
                    && !valuesLower.Contains(word, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether every word is in the name.</summary>
        public bool NameHasAll(string[] words) =>
            words.All(word => NameLower.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>One field of one kind, lowercase, and the hit it gives.</summary>
    private sealed record FieldEntryText(ObjectKind Kind, Hit Hit, string IdLower, string LabelLower)
    {
        /// <summary>Whether every word is in the field's id or label.</summary>
        public bool Matches(string[] words) => words.All(word =>
            IdLower.Contains(word, StringComparison.Ordinal) || LabelLower.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>A hit with what it is sorted by: rank, custom first, name, kind, id.</summary>
    private readonly record struct Candidate(int Rank, int Standard, ObjectKind Kind, string NameLower, Hit Hit)
    {
        public static int Compare(Candidate a, Candidate b)
        {
            var order = a.Rank.CompareTo(b.Rank);
            if (order == 0)
            {
                order = a.Standard.CompareTo(b.Standard);
            }

            if (order == 0)
            {
                order = string.CompareOrdinal(a.NameLower, b.NameLower);
            }

            if (order == 0)
            {
                order = a.Kind.CompareTo(b.Kind);
            }

            return order != 0 ? order : string.CompareOrdinal(a.Hit.Id, b.Hit.Id);
        }
    }
}
