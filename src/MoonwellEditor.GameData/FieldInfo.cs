namespace MoonwellEditor.GameData;

/// <summary>One field of one kind of object, as World Editor's metadata and Moonwell's generator describe it.</summary>
/// <param name="Id">The field id (<c>uhpm</c>).</param>
/// <param name="Label">
/// World Editor's English label (<c>Hit Points Maximum (Base)</c>), or the field's Moonwell name when the export has
/// none.
/// </param>
/// <param name="Category">The metadata's <c>category</c> as written (<c>stats</c>, <c>art</c>); empty for none.</param>
/// <param name="Sort">The metadata's <c>sort</c> (<c>c4a00</c>), World Editor's order; empty for none.</param>
/// <param name="Type">
/// The kind of value: <c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>, <c>objectList</c>, <c>objectRef</c> or
/// <c>choice</c> (Moonwell's <c>FieldType</c> in lower camel case).
/// </param>
/// <param name="Store">How an object file stores it: <c>int</c>, <c>real</c>, <c>unreal</c> or <c>string</c>.</param>
/// <param name="ListOf">
/// For an object list or reference, what it holds: <c>Ability</c>, <c>Unit</c>, <c>Item</c>, <c>Upgrade</c>,
/// <c>Buff</c>, or <c>object</c> for a requirement list of units and upgrades; null for other types.
/// </param>
/// <param name="PerLevel">True when the field has a value for each level (each variation for a doodad).</param>
/// <param name="Pointer">An ability's data field letter as a number (<c>DataA</c> is 1), else 0.</param>
/// <param name="Skin">True when the game keeps the value in the skin files, which every player sees.</param>
/// <param name="Specific">The ability codes the field belongs to; empty for a field of every ability.</param>
/// <param name="NotSpecific">
/// The ability codes a field of every ability does not belong to (the metadata's <c>notSpecific</c>).
/// </param>
/// <param name="ForUnit">True when the metadata's <c>useUnit</c> is 1: the field is a plain unit's.</param>
/// <param name="ForHero">True when the metadata's <c>useHero</c> is 1: the field is a hero's.</param>
/// <param name="ForBuilding">True when the metadata's <c>useBuilding</c> is 1: the field is a building's.</param>
/// <param name="Min">The lowest value World Editor allows; null for none.</param>
/// <param name="Max">The highest value World Editor allows; null for none.</param>
/// <param name="MaxLength">For a text, the most characters allowed; null for none.</param>
/// <param name="MaxEntries">For a list, the most entries allowed; null for none.</param>
/// <param name="Required">True for a name, which may not be empty.</param>
/// <param name="NoNegative">True when a number below 0 is never allowed, not even past the limits.</param>
/// <param name="Choice">
/// For a <c>choice</c> field, its metadata type, the key of its list in <see cref="GameData.Choices"/>; null for other
/// types.
/// </param>
public sealed record FieldInfo(
    string Id,
    string Label,
    string Category,
    string Sort,
    string Type,
    string Store,
    string? ListOf,
    bool PerLevel,
    int Pointer,
    bool Skin,
    IReadOnlyList<string> Specific,
    IReadOnlyList<string> NotSpecific,
    bool ForUnit,
    bool ForHero,
    bool ForBuilding,
    double? Min,
    double? Max,
    int? MaxLength,
    int? MaxEntries,
    bool Required,
    bool NoNegative,
    string? Choice);
