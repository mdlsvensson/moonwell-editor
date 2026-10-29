using System.Globalization;
using MoonwellEditor.Files;

namespace MoonwellEditor.GameData;

/// <summary>The fields and standard objects of one kind of object.</summary>
public sealed class KindData
{
    private readonly ObjectKind _kind;
    private readonly Dictionary<string, FieldInfo> _fields;
    private readonly Dictionary<string, StandardObject> _objects;

    internal KindData(
        ObjectKind kind,
        string? levelField,
        IReadOnlyList<FieldInfo> fields,
        IReadOnlyList<StandardObject> objects,
        IReadOnlyDictionary<string, IReadOnlyList<UpgradeEffectField>>? effects = null)
    {
        _kind = kind;
        LevelField = levelField;
        Fields = fields;
        Objects = objects;
        Effects = effects ?? new Dictionary<string, IReadOnlyList<UpgradeEffectField>>();
        _fields = fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        _objects = objects.ToDictionary(obj => obj.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// The field that holds an object's number of levels: <c>alev</c> for abilities, <c>glvl</c> for upgrades; null for
    /// other kinds.
    /// </summary>
    public string? LevelField { get; }

    /// <summary>The kind's fields, ordered by field id.</summary>
    public IReadOnlyList<FieldInfo> Fields { get; }

    /// <summary>The kind's standard objects, ordered by id.</summary>
    public IReadOnlyList<StandardObject> Objects { get; }

    /// <summary>
    /// For upgrades, the fields each effect (<c>ratd</c>) uses, in the metadata's order; empty for other kinds.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<UpgradeEffectField>> Effects { get; }

    /// <summary>The field with this id, or null when the kind has none.</summary>
    public FieldInfo? Field(string id) => _fields.GetValueOrDefault(id);

    /// <summary>The standard object with this id, or null when the kind has none.</summary>
    public StandardObject? Object(string id) => _objects.GetValueOrDefault(id);

    /// <summary>
    /// Whether the field belongs to the object, by Moonwell's rules: a unit field by its use flags (a building's when
    /// <see cref="FieldInfo.ForBuilding"/>, a hero's when <see cref="FieldInfo.ForHero"/>, any other unit's when
    /// <see cref="FieldInfo.ForUnit"/>); an ability field when the metadata lists it for the ability, whether by the
    /// ability's id or its code (<see cref="StandardObject.Own"/>), or, for a field of every ability, when the ability's
    /// code is not one of its <see cref="FieldInfo.NotSpecific"/> codes; any field of another kind always.
    /// </summary>
    public bool Applies(FieldInfo field, StandardObject obj) => Applies(_kind, field, obj);

    /// <summary>
    /// The standard object the fields of the object with this id go by (the object itself, or a custom object's base):
    /// a unit is a hero by its own id's first letter, not its base's, as World Editor makes a copy of the Peasant with
    /// an upper-case <c>H</c> id a hero with the hero fields (docs/game-facts.md, seen 10-28, Sven).
    /// </summary>
    public StandardObject ByOwnId(StandardObject standard, string id)
    {
        if (_kind != ObjectKind.Unit || id.Length == 0)
        {
            return standard;
        }

        var hero = char.IsUpper(id[0]);
        return hero == standard.Hero ? standard : standard with { Hero = hero };
    }

    /// <summary>Whether a field of the kind belongs to the object; see the instance <c>Applies</c>.</summary>
    internal static bool Applies(ObjectKind kind, FieldInfo field, StandardObject obj)
    {
        switch (kind)
        {
            case ObjectKind.Unit:
                return obj.Building ? field.ForBuilding : obj.Hero ? field.ForHero : field.ForUnit;
            case ObjectKind.Ability:
                var code = obj.Code ?? "";
                return field.Specific.Count > 0
                    ? obj.Own != null && obj.Own.Contains(field.Id)
                    : !field.NotSpecific.Contains(code);
            default:
                return true;
        }
    }

    /// <summary>
    /// The object's number of levels, from its <see cref="LevelField"/>; 0 for kinds without levels or an object with
    /// no count.
    /// </summary>
    public int Levels(StandardObject obj) => Levels(LevelField, obj);

    /// <summary>The object's number of levels from its <paramref name="levelField"/> value, as above.</summary>
    internal static int Levels(string? levelField, StandardObject obj)
    {
        if (levelField == null || !obj.Values.TryGetValue(levelField, out var text))
        {
            return 0;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var levels) ? levels : 0;
    }
}
