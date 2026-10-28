namespace MoonwellEditor.GameData;

/// <summary>The values World Editor offers for one metadata type (<c>attackType</c>, <c>targetList</c>).</summary>
/// <param name="Flags">True when a field holds any of the values (a comma list or bits), false for one.</param>
/// <param name="Store">How an object file stores a field of the type: <c>int</c> or <c>string</c>.</param>
/// <param name="Entries">The values, in World Editor's order.</param>
public sealed record ChoiceList(bool Flags, string Store, IReadOnlyList<ChoiceEntry> Entries);

/// <summary>One value of a <see cref="ChoiceList"/>.</summary>
/// <param name="Value">How the data and the object file spell it: <c>normal</c>, or a bit's number for bits.</param>
/// <param name="Label">World Editor's English label (<c>Normal</c>), or the value when the tables have none.</param>
/// <param name="Alternates">
/// The other spellings the game's data uses for the value (<c>aliv</c> for <c>alive</c>); empty when none.
/// </param>
/// <param name="Group">The heading the value is listed under (a building's shadow under Buildings); null for none.</param>
public sealed record ChoiceEntry(string Value, string Label, IReadOnlyList<string> Alternates, string? Group = null);
