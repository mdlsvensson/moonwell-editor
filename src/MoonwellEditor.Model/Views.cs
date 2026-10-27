namespace MoonwellEditor.Model;

/// <summary>One object in a kind's tree: a custom object of the map, or one of the game's standard objects.</summary>
/// <param name="Id">The object's id (<c>h000</c>, <c>hfoo</c>).</param>
/// <param name="BaseId">For a custom object, the standard object it is based on; null for a standard object.</param>
/// <param name="Name">The name: the map's when it changes the name, else the standard one.</param>
/// <param name="Suffix">World Editor's suffix (<c>(Evil)</c>), resolved like the name; empty when it has none.</param>
/// <param name="Path">
/// Where the object sits in the tree, as World Editor names its folders: <c>["Custom"]</c> for a custom object; for a
/// standard unit its race, <c>Melee</c> or <c>Campaign</c>, then <c>Units</c>, <c>Buildings</c>, <c>Heroes</c> or
/// <c>Special</c>; for an item its class, for a destructable or a doodad its category, for an ability its race then
/// <c>Units</c>, <c>Heroes</c> or <c>Items</c>, for a buff its race then <c>Buffs</c> or <c>Effects</c>, for an
/// upgrade its race (see <c>Folders</c>).
/// </param>
/// <param name="Changed">True when the map has a modification of the object.</param>
/// <param name="Icon">
/// The path of its icon (its Art - Icon field: the map's when it changes it, else the standard one); empty for a kind
/// without icons (destructables, doodads) or none.
/// </param>
/// <param name="IconModes">
/// The icon in each graphics mode (<c>sd</c>, <c>hd</c>, <c>de</c>) when it differs by mode; null when it does not.
/// </param>
public sealed record TreeEntry(
    string Id,
    string? BaseId,
    string Name,
    string Suffix,
    IReadOnlyList<string> Path,
    bool Changed,
    string Icon = "",
    IReadOnlyDictionary<string, string>? IconModes = null);

/// <summary>One object with every field that applies to it, resolved against the map and the standard data.</summary>
/// <param name="Kind">The kind's key (<c>unit</c>), as <see cref="Files.ObjectKinds.Key"/> names it.</param>
/// <param name="Id">The object's id.</param>
/// <param name="BaseId">For a custom object, the standard object it is based on; null for a standard object.</param>
/// <param name="Name">The name: the map's when it changes the name, else the standard one.</param>
/// <param name="Chain">
/// Where the object comes from, broadest first: the kind (<c>Unit</c>), for a hero or a building that type, the
/// standard object, and for a custom object the object itself. A link that is not an object has the id "".
/// </param>
/// <param name="Levels">
/// The number of levels (variations for a doodad): the map's level count when it changes it, else the standard one;
/// 0 for kinds without levels.
/// </param>
/// <param name="ReadOnly">Why the object cannot be edited; null when it can.</param>
/// <param name="Fields">The fields, in the kind's field order; a per-level field once for each level.</param>
public sealed record ObjectView(
    string Kind,
    string Id,
    string? BaseId,
    string Name,
    IReadOnlyList<ChainLink> Chain,
    int Levels,
    string? ReadOnly,
    IReadOnlyList<FieldView> Fields);

/// <summary>One link of an object's chain.</summary>
/// <param name="Name">What the link is called (<c>Unit</c>, <c>Paladin</c>).</param>
/// <param name="Id">The object's id, or "" for a link that is not an object (the kind, a hero, a building).</param>
public sealed record ChainLink(string Name, string Id);

/// <summary>One field of one object at one level, with its value.</summary>
/// <param name="Id">The field id (<c>uhpm</c>).</param>
/// <param name="Label">World Editor's English label.</param>
/// <param name="Category">The metadata's category (<c>stats</c>).</param>
/// <param name="Sort">World Editor's sort key within the category.</param>
/// <param name="Type">
/// The kind of value (<c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>, <c>choice</c> ...).
/// </param>
/// <param name="Choice">For a <c>choice</c> field, the key of its list of values; null for other types.</param>
/// <param name="ListOf">
/// For an <c>objectList</c> or <c>objectRef</c> field, what it holds: <c>Ability</c>, <c>Unit</c>, <c>Item</c>,
/// <c>Upgrade</c>, <c>Buff</c>, or <c>object</c> for units and upgrades together; null for other types.
/// </param>
/// <param name="Level">
/// The level (variation for a doodad), from 1, for a per-level field; 0 for any other field.
/// </param>
/// <param name="Value">
/// The value shown: the map's when it changes the field, else <paramref name="Base"/>. A <c>TRIGSTR_n</c> text shows
/// the strings file's text; a real number is written as <c>float.ToString("R", CultureInfo.InvariantCulture)</c>
/// writes it.
/// </param>
/// <param name="Base">
/// The standard value: a standard object's own, a custom object's standard base's (never the map's change to the base).
/// Empty when there is none.
/// </param>
/// <param name="Changed">True when the map has a modification of the field at this level.</param>
/// <param name="Skin">True when the field is kept in the skin file.</param>
/// <param name="Warning">
/// What is wrong with the map's value by World Editor's limits (<c>Above World Editor's limit of 500000.</c>), several
/// joined by a space; null when nothing is or the map does not change the field.
/// </param>
/// <param name="Modes">
/// The standard values per graphics mode (<c>sd</c>, <c>hd</c>, <c>de</c>) when they differ; null when they do not.
/// </param>
/// <param name="Asset">What game file the field names (<c>icon</c>, <c>model</c>, <c>texture</c>); null for none.</param>
/// <param name="Meta">
/// The metadata's type as written (<c>model</c>, <c>soundLabel</c>), which says how World Editor shows the field; null
/// when not known.
/// </param>
/// <param name="Data">
/// An ability's data field letter as a number (<c>DataA</c> is 1), which its tooltips name it by
/// (<c>&lt;AHbz,DataA1&gt;</c>); 0 for any other field.
/// </param>
public sealed record FieldView(
    string Id,
    string Label,
    string Category,
    string Sort,
    string Type,
    string? Choice,
    string? ListOf,
    int Level,
    string Value,
    string Base,
    bool Changed,
    bool Skin,
    string? Warning,
    IReadOnlyDictionary<string, string>? Modes,
    string? Asset = null,
    string? Meta = null,
    int Data = 0);
