namespace MoonwellEditor.GameData;

/// <summary>One of the game's standard objects (<c>hfoo</c>) with its standard values.</summary>
/// <param name="Id">The object id.</param>
/// <param name="Name">The in-game name, a <c>WESTRING_</c> key resolved to English; empty when it has none.</param>
/// <param name="Suffix">World Editor's suffix (<c>(Evil)</c>), resolved like the name; empty when it has none.</param>
/// <param name="Race">The data's <c>race</c> as written (<c>human</c>); empty for a kind without one.</param>
/// <param name="Campaign">True for a unit of the campaign (its <c>campaign</c> in <c>unitui.slk</c> is 1).</param>
/// <param name="Hero">True for a hero: a unit whose id starts with a capital letter.</param>
/// <param name="Building">True for a building: a unit whose <c>isbldg</c> is 1.</param>
/// <param name="Special">True for an object World Editor files under Special (its <c>special</c> is 1).</param>
/// <param name="Category">
/// The data column World Editor sorts the kind's tree by, as written: an item's <c>class</c> (<c>PowerUp</c>), a
/// destructable's or a doodad's <c>category</c> (<c>D</c>), an ability's <c>sort</c> (<c>hero</c>), a buff's
/// <c>isEffect</c> (<c>1</c>), a unit's <c>unitClass</c> in <c>unitui.slk</c> (<c>HUnit01</c>, its place in its
/// folder); empty for an upgrade, or no value.
/// </param>
/// <param name="Hidden">
/// True for an object World Editor does not show: an ability whose <c>useInEditor</c> is 0, a unit whose
/// <c>hiddenInEditor</c> in <c>unitui.slk</c> is 1.
/// </param>
/// <param name="Code">An ability's code, the ability it behaves as; null for every other kind.</param>
/// <param name="Values">
/// The standard values under the base tables, as the data writes them, keyed by field id, or <c>id:level</c> for a
/// field of each level (level 1 first). A field the object has no value for is absent.
/// </param>
/// <param name="Modes">
/// The values the game keeps per graphics mode, for the keys of <paramref name="Values"/> whose value differs between
/// the modes: key, then <c>sd</c> (Classic), <c>hd</c> (Reforged) and <c>de</c> (Definitive Edition), the empty string
/// for no value. <paramref name="Values"/> holds the <c>de</c> value, as Moonwell does.
/// </param>
/// <param name="Sets">
/// For each balance set the object exists in (<c>melee_v0</c>, <c>custom_v0</c>, <c>custom_v1</c>), the keys whose
/// value differs from <paramref name="Values"/> under it, as Moonwell's reader gives them (the empty string for a key
/// the set has no value for); null or a missing set for none.
/// </param>
/// <param name="MissingFrom">
/// The balance sets the object does not exist under, and <c>base</c> when the base tables lack it; null for none.
/// </param>
/// <param name="Hostile">
/// True for a unit World Editor files under Neutral Hostile: one of no playable race or the naga whose
/// <c>hostilePal</c> in <c>unitui.slk</c> is 1.
/// </param>
/// <param name="Sort">
/// The data column World Editor lists a folder by before the names, as written: a buff's <c>sort</c> (<c>hero</c>
/// before <c>unit</c>), a destructable's or a doodad's <c>doodClass</c> (tree walls <c>A</c> first); empty for others.
/// </param>
/// <param name="Row">
/// The object's place in its kind's base table (<c>units/unitdata.slk</c> lists the Paladin before the campaign
/// heroes), the order World Editor lists objects in where it names them by a file; after every row for an object the
/// base table lacks.
/// </param>
/// <param name="Version">The data's <c>version</c> as written (<c>2</c> for what Reforged added); empty for none.</param>
/// <param name="Own">
/// For an ability, the ids of the fields specific to it: those the metadata lists by the ability's own id (Cargo
/// Capacity, <c>Car1</c>, for <c>Sch2</c>), and those it lists by the ability's code unless a field listed by the id has
/// the same column; empty for an ability with none. May be empty for other kinds; null when not given.
/// </param>
public sealed record StandardObject(
    string Id,
    string Name,
    string Suffix,
    string Race,
    bool Campaign,
    bool Hero,
    bool Building,
    bool Special,
    string Category,
    bool Hidden,
    string? Code,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Modes,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Sets = null,
    IReadOnlyList<string>? MissingFrom = null,
    bool Hostile = false,
    string Sort = "",
    int Row = 0,
    string Version = "",
    IReadOnlyList<string>? Own = null);
