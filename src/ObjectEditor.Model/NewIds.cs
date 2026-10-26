using ObjectEditor.Files;

namespace ObjectEditor.Model;

/// <summary>
/// The ids World Editor gives new objects: a letter for the kind, then the lowest free three characters, counting
/// <c>0-9</c> then <c>A-Z</c> from <c>000</c> (<c>h000</c>, <c>h001</c> ... <c>h009</c>, <c>h00A</c> ...).
/// </summary>
internal static class NewIds
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The first letter of a new object's id: <c>h</c> for a unit, <c>H</c> for one based on a hero (whose id starts
    /// with a capital letter), <c>I</c>, <c>B</c> for a destructable, <c>D</c>, <c>A</c>, <c>B</c> for a buff and
    /// <c>R</c>.
    /// </summary>
    public static char Letter(ObjectKind kind, string baseId) => kind switch
    {
        ObjectKind.Unit => char.IsAsciiLetterUpper(baseId[0]) ? 'H' : 'h',
        ObjectKind.Item => 'I',
        ObjectKind.Destructable => 'B',
        ObjectKind.Doodad => 'D',
        ObjectKind.Ability => 'A',
        ObjectKind.Buff => 'B',
        ObjectKind.Upgrade => 'R',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The lowest id with this letter that is not in <paramref name="taken"/>; null when all are.</summary>
    public static string? Next(char letter, IReadOnlySet<string> taken)
    {
        var count = Digits.Length;
        for (var number = 0; number < count * count * count; number++)
        {
            var id = string.Concat(
                letter,
                Digits[number / (count * count)],
                Digits[number / count % count],
                Digits[number % count]);
            if (!taken.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an id the user typed can be an object's id: four characters, each an ASCII letter or digit (the ids the
    /// game and World Editor use, safe in a script's <c>'h000'</c>).
    /// </summary>
    public static bool Valid(string id) => id.Length == 4 && id.All(char.IsAsciiLetterOrDigit);
}
