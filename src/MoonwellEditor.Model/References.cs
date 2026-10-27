using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>An object whose fields name another object (the details panel's Used by).</summary>
/// <param name="Kind">The kind's key (<c>unit</c>), as <see cref="ObjectKinds.Key"/> names it.</param>
/// <param name="Id">The object's id.</param>
/// <param name="Name">The object's name, as the tree shows it.</param>
/// <param name="Fields">The labels of the fields that name the other object, once each, in field order.</param>
public sealed record Reference(string Kind, string Id, string Name, IReadOnlyList<string> Fields);

public sealed partial class Session
{
    // Who names whom in the standard data alone; built on the first Used by, as it reads every standard object.
    private StandardReferences? _standardReferences;

    /// <summary>
    /// Every object, of any kind, whose fields name the object <paramref name="id"/> of <paramref name="kind"/>: an
    /// object list or reference field that holds it (a unit's abilities, a building's trained units, an ability's
    /// buffs, a requirement). Standard objects as the game data has them, unless the map changes them; the map's
    /// changed and custom objects as they are now. In the kinds' order, then the tree's.
    /// </summary>
    public IReadOnlyList<Reference> UsedBy(ObjectKind kind, string id)
    {
        _standardReferences ??= new StandardReferences(_game);
        var result = new List<Reference>();
        foreach (var each in Enum.GetValues<ObjectKind>())
        {
            var map = _kinds[each];
            var data = _game.Kind(each);
            var resolver = ResolverOf(each, data);
            var originals = map.Originals();

            // The standard objects the map leaves as they are, from the index.
            foreach (var (sourceId, fields) in _standardReferences.Of(each, kind, id))
            {
                if (!originals.ContainsKey(sourceId))
                {
                    var standard = data.Object(sourceId)!;
                    result.Add(new Reference(ObjectKinds.Key(each), sourceId, standard.Name, fields));
                }
            }

            // The map's own versions, read from their views.
            foreach (var found in map.Customs().Concat(originals.Values))
            {
                var standard = data.Object(found.BaseId);
                var view = resolver.View(found.Id, standard, found, null);
                var fields = view.Fields
                    .Where(field => Names(field.ListOf, field.Value, kind, id))
                    .Select(field => field.Label)
                    .Distinct()
                    .ToList();
                if (fields.Count > 0)
                {
                    result.Add(new Reference(ObjectKinds.Key(each), found.Id, view.Name, fields));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether a list field's value holds the object <paramref name="id"/> of <paramref name="kind"/>.
    /// </summary>
    internal static bool Names(string? listOf, string value, ObjectKind kind, string id) =>
        listOf != null && Targets(listOf).Contains(kind) && Ids(value).Contains(id);

    /// <summary>The kinds a list field holds: <c>object</c> (requirements) holds units and upgrades.</summary>
    internal static IReadOnlyList<ObjectKind> Targets(string listOf) => listOf switch
    {
        "Ability" => [ObjectKind.Ability],
        "Unit" => [ObjectKind.Unit],
        "Item" => [ObjectKind.Item],
        "Upgrade" => [ObjectKind.Upgrade],
        "Buff" => [ObjectKind.Buff],
        "object" => [ObjectKind.Unit, ObjectKind.Upgrade],
        _ => [],
    };

    /// <summary>The ids a list value holds (comma separated).</summary>
    internal static IEnumerable<string> Ids(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// For each object, the standard objects whose standard values name it, and in which fields; read once from the game
/// data.
/// </summary>
internal sealed class StandardReferences
{
    // (the named object's kind, its id) to the naming objects of each kind: id and field labels, in tree order.
    private readonly Dictionary<(ObjectKind Kind, string Id), Dictionary<ObjectKind, List<Source>>> _named = [];

    public StandardReferences(GameData.GameData game)
    {
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var data = game.Kind(kind);
            foreach (var standard in Folders.Sorted(kind, data.Objects))
            {
                foreach (var field in data.Fields)
                {
                    if (field.ListOf == null || !data.Applies(field, standard))
                    {
                        continue;
                    }

                    foreach (var value in Values(standard, field))
                    {
                        foreach (var target in Session.Targets(field.ListOf))
                        {
                            foreach (var id in Session.Ids(value))
                            {
                                Add(target, id, kind, standard.Id, field.Label);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The standard objects of <paramref name="kind"/> that name the object <paramref name="id"/> of
    /// <paramref name="named"/>, with the fields that do.
    /// </summary>
    public IEnumerable<(string Id, IReadOnlyList<string> Fields)> Of(ObjectKind kind, ObjectKind named, string id)
    {
        if (!_named.TryGetValue((named, id), out var byKind) || !byKind.TryGetValue(kind, out var list))
        {
            return [];
        }

        return list.Select(each => (each.Id, (IReadOnlyList<string>)each.Fields));
    }

    // The field's standard values: under its id, or one per level (keyed id:1, id:2 ...).
    private static IEnumerable<string> Values(StandardObject standard, FieldInfo field)
    {
        if (!field.PerLevel)
        {
            if (standard.Values.TryGetValue(field.Id, out var value))
            {
                yield return value;
            }

            yield break;
        }

        for (var level = 1; standard.Values.TryGetValue($"{field.Id}:{level}", out var value); level++)
        {
            yield return value;
        }
    }

    private void Add(ObjectKind named, string id, ObjectKind kind, string source, string label)
    {
        if (!_named.TryGetValue((named, id), out var byKind))
        {
            _named[(named, id)] = byKind = [];
        }

        if (!byKind.TryGetValue(kind, out var list))
        {
            byKind[kind] = list = [];
        }

        if (list.Count > 0 && list[^1].Id == source)
        {
            if (!list[^1].Fields.Contains(label))
            {
                list[^1].Fields.Add(label);
            }

            return;
        }

        list.Add(new Source(source, [label]));
    }

    // One standard object that names another, and the labels of the fields that do.
    private sealed record Source(string Id, List<string> Fields);
}
