namespace MoonwellEditor.GameData;

/// <summary>One of the game's standard objects (<c>hfoo</c>) with its standard values.</summary>
/// <param name="Id">The object id.</param>
/// <param name="Name">The in-game name, a <c>WESTRING_</c> key resolved to English; empty when it has none.</param>
/// <param name="Suffix">World Editor's suffix (<c>(Evil)</c>), resolved like the name; empty when it has none.</param>
/// <param name="Race">The data's <c>race</c> as written (<c>human</c>); empty for a kind without one.</param>
/// <param name="Campaign">True for a unit of the campaign (its <c>campaign</c> is 1).</param>
/// <param name="Hero">True for a hero: a unit whose id starts with a capital letter.</param>
/// <param name="Building">True for a building: a unit whose <c>isbldg</c> is 1.</param>
/// <param name="Special">True for an object World Editor files under Special (its <c>special</c> is 1).</param>
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
public sealed record StandardObject(
    string Id,
    string Name,
    string Suffix,
    string Race,
    bool Campaign,
    bool Hero,
    bool Building,
    bool Special,
    string? Code,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Modes);
