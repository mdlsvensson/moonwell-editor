using System.Text;

namespace MoonwellEditor.Map;

/// <summary>
/// The names World Editor's script gives what is placed on the map, as triggers name them: <c>gg_unit_hfoo_0001</c>
/// (the type and the creation number), <c>gg_rct_Spawn_Point</c> (a region's name, each character that cannot be in
/// a name turned into <c>_</c>). Checked against the scripts of WarChasers and Forgotten Hollow.
/// </summary>
public static class TriggerNames
{
    /// <summary>A placed unit's name.</summary>
    public static string Unit(string typeId, int creationNumber) => $"gg_unit_{typeId}_{creationNumber:D4}";

    /// <summary>A placed item's name.</summary>
    public static string Item(string typeId, int creationNumber) => $"gg_item_{typeId}_{creationNumber:D4}";

    /// <summary>A placed destructable's name.</summary>
    public static string Destructable(string typeId, int creationNumber) => $"gg_dest_{typeId}_{creationNumber:D4}";

    /// <summary>A region's name.</summary>
    public static string Region(string name) => "gg_rct_" + Identifier(name);

    /// <summary>A camera's name.</summary>
    public static string Camera(string name) => "gg_cam_" + Identifier(name);

    // A letter, digit or _ stays; anything else becomes _.
    private static string Identifier(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            text.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return text.ToString();
    }
}
