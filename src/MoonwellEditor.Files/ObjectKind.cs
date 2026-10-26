namespace MoonwellEditor.Files;

/// <summary>The seven kinds of object, one per Object Editor tab, each with its own object file.</summary>
public enum ObjectKind
{
    /// <summary>Units (<c>.w3u</c>).</summary>
    Unit,

    /// <summary>Items (<c>.w3t</c>).</summary>
    Item,

    /// <summary>Destructables (<c>.w3b</c>).</summary>
    Destructable,

    /// <summary>Doodads (<c>.w3d</c>), whose fields are per variation.</summary>
    Doodad,

    /// <summary>Abilities (<c>.w3a</c>), whose fields are per level.</summary>
    Ability,

    /// <summary>Buffs and effects (<c>.w3h</c>).</summary>
    Buff,

    /// <summary>Upgrades (<c>.w3q</c>), whose fields are per level.</summary>
    Upgrade,
}

/// <summary>Facts about each <see cref="ObjectKind"/>.</summary>
public static class ObjectKinds
{
    /// <summary>The kind's file extension without the dot: "w3u" for units.</summary>
    public static string Extension(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit => "w3u",
        ObjectKind.Item => "w3t",
        ObjectKind.Destructable => "w3b",
        ObjectKind.Doodad => "w3d",
        ObjectKind.Ability => "w3a",
        ObjectKind.Buff => "w3h",
        ObjectKind.Upgrade => "w3q",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Whether the kind's modifications carry a level (a variation for doodads) and a data pointer: doodads, abilities
    /// and upgrades.
    /// </summary>
    public static bool HasLevels(ObjectKind kind) =>
        kind is ObjectKind.Doodad or ObjectKind.Ability or ObjectKind.Upgrade;

    /// <summary>The kind's name in the app's messages: "unit", "item", "destructable" and so on.</summary>
    public static string Key(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit => "unit",
        ObjectKind.Item => "item",
        ObjectKind.Destructable => "destructable",
        ObjectKind.Doodad => "doodad",
        ObjectKind.Ability => "ability",
        ObjectKind.Buff => "buff",
        ObjectKind.Upgrade => "upgrade",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The kind with this <see cref="Key"/>, or null when no kind has it.</summary>
    public static ObjectKind? FromKey(string key)
    {
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            if (Key(kind) == key)
            {
                return kind;
            }
        }

        return null;
    }
}
