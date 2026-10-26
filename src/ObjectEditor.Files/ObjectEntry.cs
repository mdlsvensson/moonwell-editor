using War3Net.Build.Object;
using War3Net.Common.Extensions;

namespace ObjectEditor.Files;

/// <summary>
/// One object's record in an object file: a change to a standard object, or a custom object. It wraps War3Net's record
/// and edits it in place.
/// </summary>
public sealed class ObjectEntry
{
    // War3Net has one record class per modification layout; exactly one of these is set.
    private readonly SimpleObjectModification? simple;
    private readonly LevelObjectModification? levels;
    private readonly VariationObjectModification? variations;
    private readonly List<FieldMod> mods = [];

    internal ObjectEntry(object record)
    {
        Record = record;
        switch (record)
        {
            case SimpleObjectModification simpleRecord:
                simple = simpleRecord;
                foreach (var mod in simpleRecord.Modifications)
                {
                    mods.Add(new FieldMod(mod));
                }

                break;
            case LevelObjectModification levelRecord:
                levels = levelRecord;
                foreach (var mod in levelRecord.Modifications)
                {
                    mods.Add(new FieldMod(mod));
                }

                break;
            case VariationObjectModification variationRecord:
                variations = variationRecord;
                foreach (var mod in variationRecord.Modifications)
                {
                    mods.Add(new FieldMod(mod));
                }

                break;
            default:
                throw new ArgumentException("Not a War3Net object record.", nameof(record));
        }
    }

    /// <summary>War3Net's record this wraps.</summary>
    internal object Record { get; }

    /// <summary>
    /// The standard object's id: for a custom object the one it is based on, for a change to a standard object that
    /// object itself.
    /// </summary>
    public string BaseId
    {
        get => OldId.ToRawcode();
        set => OldId = Rawcode.From(value, nameof(BaseId));
    }

    /// <summary>The custom object's id; "" for a change to a standard object.</summary>
    public string Id
    {
        get => NewId == 0 ? "" : NewId.ToRawcode();
        set => NewId = value == "" ? 0 : Rawcode.From(value, nameof(Id));
    }

    /// <summary>
    /// How many set flags the record has (version 3 files; World Editor writes one, 0). Always 0 in older versions,
    /// which have none.
    /// </summary>
    public int SetCount => Flags.Count;

    /// <summary>The field modifications, in file order.</summary>
    public IReadOnlyList<FieldMod> Mods => mods;

    /// <summary>The modification of this field at this level (0 for kinds without levels), or null.</summary>
    public FieldMod? Find(string field, int level) =>
        mods.FirstOrDefault(mod => mod.Field == field && mod.Level == level);

    /// <summary>
    /// Sets a field at a level: changes the modification already there in place (keeping its position), or appends a
    /// new one. Level and pointer must be 0 for kinds without levels.
    /// </summary>
    public FieldMod Set(string field, int level, int pointer, FieldValue value)
    {
        var id = Rawcode.From(field, nameof(field));
        ArgumentNullException.ThrowIfNull(value);
        if (simple is not null && (level != 0 || pointer != 0))
        {
            throw new ArgumentException("Level and pointer must be 0 for a kind without levels.", nameof(level));
        }

        var existing = Find(field, level);
        if (existing is not null)
        {
            existing.Value = value;
            existing.Pointer = pointer;
            return existing;
        }

        // The new modification starts as an empty text, so a refused value leaves nothing behind.
        ObjectDataModification record = simple is not null
            ? new SimpleObjectDataModification()
            : levels is not null
                ? new LevelObjectDataModification()
                : new VariationObjectDataModification();
        record.Id = id;
        record.Type = ObjectDataType.String;
        record.Value = "";
        var mod = new FieldMod(record) { Value = value, Level = level, Pointer = pointer };

        switch (record)
        {
            case SimpleObjectDataModification simpleMod:
                simple!.Modifications.Add(simpleMod);
                break;
            case LevelObjectDataModification levelMod:
                levels!.Modifications.Add(levelMod);
                break;
            case VariationObjectDataModification variationMod:
                variations!.Modifications.Add(variationMod);
                break;
        }

        mods.Add(mod);
        return mod;
    }

    /// <summary>Removes a modification of this entry.</summary>
    public void Remove(FieldMod mod)
    {
        if (!mods.Remove(mod))
        {
            throw new ArgumentException("The modification is not in this entry.", nameof(mod));
        }

        switch (mod.Record)
        {
            case SimpleObjectDataModification simpleMod:
                simple!.Modifications.Remove(simpleMod);
                break;
            case LevelObjectDataModification levelMod:
                levels!.Modifications.Remove(levelMod);
                break;
            case VariationObjectDataModification variationMod:
                variations!.Modifications.Remove(variationMod);
                break;
        }
    }

    /// <summary>A new record of the layout the kind uses.</summary>
    internal static ObjectEntry Create(ObjectKind kind, int oldId, int newId, bool withSetFlag)
    {
        List<int> flags = withSetFlag ? [0] : [];
        object record = kind switch
        {
            ObjectKind.Doodad => new VariationObjectModification { OldId = oldId, NewId = newId, Unk = flags },
            ObjectKind.Ability or ObjectKind.Upgrade =>
                new LevelObjectModification { OldId = oldId, NewId = newId, Unk = flags },
            _ => new SimpleObjectModification { OldId = oldId, NewId = newId, Unk = flags },
        };
        return new ObjectEntry(record);
    }

    private int OldId
    {
        get => simple?.OldId ?? levels?.OldId ?? variations!.OldId;
        set
        {
            if (simple is not null)
            {
                simple.OldId = value;
            }
            else if (levels is not null)
            {
                levels.OldId = value;
            }
            else
            {
                variations!.OldId = value;
            }
        }
    }

    private int NewId
    {
        get => simple?.NewId ?? levels?.NewId ?? variations!.NewId;
        set
        {
            if (simple is not null)
            {
                simple.NewId = value;
            }
            else if (levels is not null)
            {
                levels.NewId = value;
            }
            else
            {
                variations!.NewId = value;
            }
        }
    }

    // War3Net calls the set flags Unk.
    private List<int> Flags => simple?.Unk ?? levels?.Unk ?? variations!.Unk;
}

/// <summary>
/// Four-character ids (rawcodes) as War3Net stores them: one int, the first character in the lowest byte.
/// </summary>
internal static class Rawcode
{
    /// <summary>
    /// The int for a four-character id; throws when the text is not four characters of one byte each.
    /// </summary>
    public static int From(string? text, string name)
    {
        if (text is null || text.Length != 4 || text.Any(c => c > 0xFF))
        {
            throw new ArgumentException($"An id must be four characters, not \"{text}\".", name);
        }

        return text.FromRawcode();
    }
}
