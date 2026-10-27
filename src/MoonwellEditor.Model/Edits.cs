using System.Globalization;
using System.Text;
using MoonwellEditor.Files;
using MoonwellEditor.GameData;

namespace MoonwellEditor.Model;

/// <summary>
/// The edits: setting and resetting a field, and new, copied and deleted objects. Each one changes the files in place
/// and records its steps in the history, so it can be undone and redone. An edit that is refused changes nothing.
/// </summary>
public sealed partial class Session
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// True when the map differs from its files as opened or last saved: edits were made since and they changed an
    /// object (see <see cref="ChangedObjects"/>). An edit made and then undone, or changed back, leaves nothing unsaved
    /// (Sven, 2026-10-27: the status bar showed "0 objects changed, not saved").
    /// </summary>
    public bool Dirty => _history.Dirty && ChangedObjects() > 0;

    /// <summary>Undoes the last edit; false when there is none.</summary>
    public bool Undo() => Reindex(_history.Undo());

    /// <summary>Makes the last undone edit again; false when there is none.</summary>
    public bool Redo() => Reindex(_history.Redo());

    /// <summary>
    /// Sets a field of an object at a level (0 for a field without levels; a doodad's variation, from 1). The value is
    /// read by how the file stores the field: a whole number (a yes/no field takes <c>1</c> or <c>0</c>), a number
    /// with a <c>.</c>, or text; a choice of one value takes one of its list's values (see <see cref="Parse"/>). It goes to the skin file when the game keeps the field there, else to the main file.
    /// A value equal to the standard one (numbers compared as numbers) removes the map's modification instead, unless
    /// the standard value differs between graphics modes. Text whose modification holds <c>TRIGSTR_n</c> changes
    /// that entry of <c>war3map.wts</c> and keeps the reference, unless another modification of the map refers to the
    /// same entry (or the map has an object file War3Net cannot read, which might): then the new text is written
    /// inline, so the other field keeps its text.
    /// </summary>
    /// <exception cref="EditException">
    /// <c>noObject</c>, <c>noField</c>, <c>readOnly</c>, <c>notNumber</c>, <c>badValue</c> (a yes/no field's value
    /// other than 1 and 0, or a value a choice's list does not have), or <c>badText</c> for text an object file or
    /// the strings file cannot hold.
    /// </exception>
    public void Set(ObjectKind kind, string id, string field, int level, string value)
    {
        var cell = CheckCell(kind, id, field, level, value);
        var edit = new Edit();
        edit.Touch(kind, id);
        ApplyCell(edit, cell, null);
        Commit(edit);
    }

    /// <summary>
    /// Resets a field of an object at a level to its standard value: removes the map's modifications of it, in both
    /// files.
    /// </summary>
    /// <exception cref="EditException"><c>noObject</c>, <c>noField</c> or <c>readOnly</c>.</exception>
    public void Reset(ObjectKind kind, string id, string field, int level)
    {
        var cell = CheckCell(kind, id, field, level, null);
        var edit = new Edit();
        edit.Touch(kind, id);
        ApplyCell(edit, cell, null);
        Commit(edit);
    }

    /// <summary>
    /// Makes a new custom object based on a standard one, with no modifications, in both the main and the skin file
    /// (as World Editor writes it). Its id is <paramref name="id"/>, or World Editor's next free id when that is null.
    /// </summary>
    /// <returns>The new object's id.</returns>
    /// <exception cref="EditException">
    /// <c>noObject</c> (no standard object <paramref name="baseId"/>), <c>readOnly</c>, <c>idInvalid</c> or
    /// <c>idTaken</c>.
    /// </exception>
    public string Create(ObjectKind kind, string baseId, string? id)
    {
        var map = _kinds[kind];
        var data = _game.Kind(kind);
        if (data.Object(baseId) == null)
        {
            throw new EditException("noObject", $"There is no standard {ObjectKinds.Key(kind)} {baseId}.");
        }

        RefuseReadOnly(map, null);
        var newId = NewId(kind, map, data, baseId, id);
        var edit = new Edit();
        edit.Touch(kind, newId);
        foreach (var skin in new[] { false, true })
        {
            AddCustom(edit, map, kind, skin, baseId, newId);
        }

        Commit(edit);
        return newId;
    }

    /// <summary>
    /// The id <see cref="Create"/> would give a new object based on <paramref name="baseId"/> when given none: World
    /// Editor's next free id. Nothing is made.
    /// </summary>
    /// <exception cref="EditException">
    /// <c>noObject</c> (no standard object <paramref name="baseId"/>) or <c>idTaken</c> (every id is taken).
    /// </exception>
    public string NextId(ObjectKind kind, string baseId)
    {
        var data = _game.Kind(kind);
        if (data.Object(baseId) == null)
        {
            throw new EditException("noObject", $"There is no standard {ObjectKinds.Key(kind)} {baseId}.");
        }

        return NewId(kind, _kinds[kind], data, baseId, null);
    }

    /// <summary>
    /// Makes a new custom object as a copy of another: of a custom object's modifications, or of the map's changes to
    /// a standard object, in both files. A <c>TRIGSTR_n</c> text is copied as the text it stands for, so the copy has
    /// its own. Its id is <paramref name="id"/>, or World Editor's next free id when that is null.
    /// </summary>
    /// <returns>The new object's id.</returns>
    /// <exception cref="EditException">
    /// <c>noObject</c>, <c>readOnly</c>, <c>idInvalid</c> or <c>idTaken</c>.
    /// </exception>
    public string Copy(ObjectKind kind, string fromId, string? id)
    {
        var (map, data, found, _) = Locate(kind, fromId);
        RefuseReadOnly(map, found);
        var baseId = found is { Custom: true } ? found.BaseId : fromId;
        var newId = NewId(kind, map, data, baseId, id);
        var edit = new Edit();
        edit.Touch(kind, newId);
        foreach (var skin in new[] { false, true })
        {
            var entry = AddCustom(edit, map, kind, skin, baseId, newId);
            var source = skin ? found?.Skin : found?.Main;
            foreach (var mod in source?.Mods ?? [])
            {
                // The new entry belongs to this edit alone, so its modifications need no steps of their own.
                entry.Set(mod.Field, mod.Level, mod.Pointer, Inline(mod.Value));
            }
        }

        Commit(edit);
        return newId;
    }

    /// <summary>Deletes a custom object: its entries in both files.</summary>
    /// <exception cref="EditException">
    /// <c>noObject</c> (also for a standard object, which cannot be deleted) or <c>readOnly</c>.
    /// </exception>
    public void Delete(ObjectKind kind, string id)
    {
        var map = _kinds[kind];
        var found = map.Find(id);
        if (found is not { Custom: true })
        {
            throw new EditException(
                "noObject",
                found == null && _game.Kind(kind).Object(id) == null
                    ? $"There is no {ObjectKinds.Key(kind)} {id}."
                    : $"{id} is a standard {ObjectKinds.Key(kind)}; only custom objects can be deleted.");
        }

        RefuseReadOnly(map, found);
        var edit = new Edit();
        edit.Touch(kind, id);
        foreach (var file in new[] { map.Main, map.Skin })
        {
            foreach (var entry in file?.Custom.Where(entry => entry.Id == id).ToList() ?? [])
            {
                var index = IndexOf(file!.Custom, entry);
                file.Remove(entry);
                edit.Done(new Step(() => file.Remove(entry), () => file.Insert(index, entry)));
            }
        }

        Commit(edit);
    }

    /// <summary>Keeps an edit that was just made in the history, and brings the search index up to date.</summary>
    private void Commit(Edit edit)
    {
        _history.Add(edit);
        Reindex(edit);
    }

    /// <summary>Updates the search index for the objects an edit touched; false when there is no edit.</summary>
    private bool Reindex(Edit? edit)
    {
        if (edit == null)
        {
            return false;
        }

        foreach (var (kind, id) in edit.Touched)
        {
            _search.Update(kind, id, _kinds[kind], _game.Kind(kind), _strings);
        }

        return true;
    }

    /// <summary>
    /// The check part of <see cref="Set"/> (a value) and <see cref="Reset"/> (a null value): the object, field and
    /// level (<see cref="Target"/>) and the value, read by the field's store. Throws the refusal; changes nothing.
    /// </summary>
    private CheckedCell CheckCell(ObjectKind kind, string id, string field, int level, string? value)
    {
        var target = Target(kind, id, field, level);
        return new CheckedCell(target, value == null ? null : Parse(target.Field, value));
    }

    /// <summary>
    /// The apply part of <see cref="Set"/> and <see cref="Reset"/>: makes a checked cell's change, recording its steps
    /// in <paramref name="edit"/>. A null value removes the map's modifications; so does a value equal to the standard
    /// one, unless the standard value differs between graphics modes; other text may change a strings file entry (see
    /// <see cref="SetTriggerString"/>); else the value is written into the field's file. The object's records are
    /// looked up again, as an earlier cell of the same edit may have added or removed them. <paramref name="before"/>
    /// holds the map's <c>TRIGSTR_n</c> references from before an edit of many cells (null for one cell). Throws
    /// <c>badText</c>, before any step of this cell, for text the strings file cannot hold.
    /// </summary>
    private void ApplyCell(Edit edit, CheckedCell cell, IReadOnlyList<(FieldMod Mod, int Number)>? before)
    {
        var target = cell.Target with { Found = cell.Target.Map.Find(cell.Target.Id) };
        if (cell.Value is not { } value)
        {
            RemoveMods(edit, target);
            return;
        }

        var info = target.Field;
        var resolver = ResolverOf(target.Kind, target.Data);
        var standardValue = resolver.StandardValue(info, target.Level, target.Standard);

        // A yes/no field the data has no value for is No in the game, so setting No is no change.
        if (info.Type == "bool" && standardValue.Length == 0)
        {
            standardValue = "0";
        }

        var modes = target.Standard.Modes.ContainsKey(Resolver.Key(info, target.Level));
        if (!modes && SameValue(value, standardValue, IsSingleChoice(info)))
        {
            RemoveMods(edit, target);
        }
        else if (!SetTriggerString(edit, target, value, before))
        {
            WriteMod(edit, target, value);
        }
    }

    /// <summary>
    /// The object and field an edit of a field is about, checked: the object exists, the field applies to it at that
    /// level, and the object can be edited.
    /// </summary>
    private FieldTarget Target(ObjectKind kind, string id, string field, int level)
    {
        var (map, data, found, standard) = Locate(kind, id);
        var info = data.Field(field);
        if (info == null || standard == null || !data.Applies(info, standard))
        {
            throw new EditException("noField", $"{ObjectKinds.Key(kind)} {id} has no field {field}.");
        }

        if (info.PerLevel)
        {
            var levels = ResolverOf(kind, data).Levels(standard, found);
            if (level < 1 || level > levels)
            {
                throw new EditException(
                    "noField",
                    $"{field} of {id} has a value for each level, from 1 to {levels}; there is no level {level}.");
            }
        }
        else if (level != 0)
        {
            throw new EditException("noField", $"{field} of {id} has no levels; its level is 0, not {level}.");
        }

        RefuseReadOnly(map, found);
        return new FieldTarget(kind, map, data, info, level, id, standard, found);
    }

    /// <summary>
    /// The map's records of an object and its standard object (a custom object's standard base, which is null when
    /// the game has no object of that id). Throws <c>noObject</c> when the kind has no such object.
    /// </summary>
    private (MapKind Map, KindData Data, MapObject? Found, StandardObject? Standard) Locate(ObjectKind kind, string id)
    {
        var map = _kinds[kind];
        var data = _game.Kind(kind);
        var found = map.Find(id);
        var standard = found is { Custom: true } ? data.Object(found.BaseId) : data.Object(id);
        if (standard == null && found is not { Custom: true })
        {
            throw new EditException("noObject", $"There is no {ObjectKinds.Key(kind)} {id}.");
        }

        return (map, data, found, standard);
    }

    private static void RefuseReadOnly(MapKind map, MapObject? found)
    {
        var reason = map.ReadOnly(found);
        if (reason != null)
        {
            throw new EditException("readOnly", reason);
        }
    }

    /// <summary>
    /// A typed value as the field's file stores it. A yes/no field takes <c>1</c> or <c>0</c> only. A choice of one
    /// value takes one of its list's values (case does not matter, and the other spellings the game's data uses count
    /// too), and is stored as the list spells it; a label is not a value. Throws <c>notNumber</c>, <c>badValue</c> or
    /// <c>badText</c>.
    /// </summary>
    private FieldValue Parse(FieldInfo field, string value)
    {
        if (field.Type == "bool")
        {
            return ParseBool(field, value);
        }

        if (field.Type == "choice" && field.Choice != null
            && _game.Choices.TryGetValue(field.Choice, out var list) && !list.Flags)
        {
            value = ChoiceValue(field, list, value);
        }

        switch (field.Store)
        {
            case "int":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    return new FieldValue.Int(number);
                }

                throw new EditException("notNumber", $"{field.Label} takes a whole number, not \"{value}\".");
            case "real":
            case "unreal":
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                    && float.IsFinite(real))
                {
                    return field.Store == "real" ? new FieldValue.Real(real) : new FieldValue.Unreal(real);
                }

                throw new EditException("notNumber", $"{field.Label} takes a number (such as 1.5), not \"{value}\".");
            default:
                CheckText(value);
                return new FieldValue.Text(value);
        }
    }

    /// <summary>A yes/no field's value: <c>1</c> or <c>0</c>, as a whole number; throws <c>badValue</c>.</summary>
    private static FieldValue ParseBool(FieldInfo field, string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number is 0 or 1)
        {
            return new FieldValue.Int(number);
        }

        throw new EditException("badValue", $"{field.Label} takes 1 (yes) or 0 (no), not \"{value}\".");
    }

    /// <summary>
    /// The list's spelling of a choice's value: the entry whose value (or one of whose other spellings) is
    /// <paramref name="value"/>, case aside; for a list stored as a whole number, the number as the list writes it.
    /// Throws <c>badValue</c> when no entry is.
    /// </summary>
    private static string ChoiceValue(FieldInfo field, ChoiceList list, string value)
    {
        var text = value;
        if (list.Store == "int"
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            text = number.ToString(CultureInfo.InvariantCulture);
        }

        bool Same(string spelling) => string.Equals(spelling, text, StringComparison.OrdinalIgnoreCase);
        var entry = list.Entries.FirstOrDefault(entry => Same(entry.Value))
            ?? list.Entries.FirstOrDefault(entry => entry.Alternates.Any(Same));
        if (entry != null)
        {
            return entry.Value;
        }

        const int shown = 12;
        var values = string.Join(", ", list.Entries.Take(shown).Select(entry => entry.Value));
        var more = list.Entries.Count > shown ? ", ..." : "";
        throw new EditException(
            "badValue", $"{field.Label} takes one of its list's values ({values}{more}), not \"{value}\".");
    }

    /// <summary>
    /// Object files store text as UTF-8 ended by a NUL, so it can hold neither a NUL nor broken Unicode.
    /// </summary>
    private static void CheckText(string text)
    {
        var valid = !text.Contains('\0');
        try
        {
            StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException)
        {
            valid = false;
        }

        if (!valid)
        {
            throw new EditException("badText", "The text holds a NUL character or broken Unicode, which a map cannot.");
        }
    }

    /// <summary>
    /// Whether a value is the standard one (shown as text): numbers are compared as numbers, and a single choice
    /// ignoring letter case (the game's data spells some standard values unlike World Editor's list: a doodad's
    /// occlusion is "no occlusion" there and "No occlusion" in the list), so setting the standard value in the list's
    /// spelling removes the modification instead of writing one.
    /// </summary>
    private static bool SameValue(FieldValue value, string standard, bool singleChoice)
    {
        return value switch
        {
            FieldValue.Int number => SameWhole(number.Value, standard),
            FieldValue.Real real => SameReal(real.Value, standard),
            FieldValue.Unreal unreal => SameReal(unreal.Value, standard),
            FieldValue.Text text => singleChoice
                ? string.Equals(text.Value, standard, StringComparison.OrdinalIgnoreCase)
                : text.Value == standard,
            _ => false,
        };
    }

    /// <summary>Whether a field holds one value of a choice list (not a list of flags).</summary>
    private bool IsSingleChoice(FieldInfo field) =>
        field.Choice != null && _game.Choices.TryGetValue(field.Choice, out var list) && !list.Flags;

    private static bool SameWhole(int value, string standard) =>
        double.TryParse(standard, NumberStyles.Float, CultureInfo.InvariantCulture, out var other) && other == value;

    private static bool SameReal(float value, string standard) =>
        float.TryParse(standard, NumberStyles.Float, CultureInfo.InvariantCulture, out var other) && other == value;

    /// <summary>
    /// When the value shown is a <c>TRIGSTR_n</c> text the strings file has, and no other modification refers to that
    /// entry (now, or in <paramref name="before"/> when given), sets the entry to the new text and returns true; the
    /// modification keeps the reference. Returns false otherwise, and always while the strings file is read only (the
    /// value is then written inline).
    /// </summary>
    private bool SetTriggerString(
        Edit edit, FieldTarget target, FieldValue value, IReadOnlyList<(FieldMod Mod, int Number)>? before)
    {
        // The value shown is the field's own file's, else the other file's.
        var (field, level, skin) = (target.Field.Id, target.Level, target.Field.Skin);
        var shown = Entry(target, skin)?.Find(field, level) ?? Entry(target, !skin)?.Find(field, level);
        if (_strings == null || _stringsReadOnly || value is not FieldValue.Text text
            || shown?.Value is not FieldValue.Text reference)
        {
            return false;
        }

        var number = WtsFile.TriggerString(reference.Value);
        var old = number == null ? null : _strings.Get(number.Value);
        if (old == null)
        {
            return false;
        }

        if (old == text.Value)
        {
            return true;
        }

        // An entry another modification also shows is not changed under it: the new text goes inline instead.
        if (SharedTriggerString(number!.Value, shown, before))
        {
            return false;
        }

        var strings = _strings;
        var entry = number!.Value;
        if (!strings.Set(entry, text.Value))
        {
            throw new EditException(
                "badText", $"war3map.wts cannot hold a line that is only \"}}\" (in {reference.Value}).");
        }

        edit.Done(new Step(() => strings.Set(entry, text.Value), () => strings.Set(entry, old)));
        return true;
    }

    /// <summary>
    /// Whether a modification other than <paramref name="own"/>, in any object file of the map (any kind, main or
    /// skin), refers to entry <paramref name="number"/> of the strings file: now, or in <paramref name="before"/> (the
    /// references from before an edit of many cells) when given, so an entry two cells of one edit share is shared
    /// for both, though the first one's text has gone inline by the time the second is written. While the map has an
    /// object file War3Net cannot read, what that file refers to is not known, so every entry counts as shared.
    /// </summary>
    private bool SharedTriggerString(int number, FieldMod own, IReadOnlyList<(FieldMod Mod, int Number)>? before)
    {
        if (_unreadable)
        {
            return true;
        }

        return TriggerStringMods().Concat(before ?? [])
            .Any(reference => reference.Number == number && !ReferenceEquals(reference.Mod, own));
    }

    /// <summary>
    /// Every modification whose text is a <c>TRIGSTR_n</c> reference, in any object file of the map (any kind, main or
    /// skin), with the number of the entry it refers to.
    /// </summary>
    private List<(FieldMod Mod, int Number)> TriggerStringMods()
    {
        var references = new List<(FieldMod Mod, int Number)>();
        var mods = _kinds.Values
            .SelectMany(map => new[] { map.Main, map.Skin })
            .SelectMany(file => (file?.Original ?? []).Concat(file?.Custom ?? []))
            .SelectMany(entry => entry.Mods);
        foreach (var mod in mods)
        {
            if (mod.Value is FieldValue.Text text && WtsFile.TriggerString(text.Value) is { } number)
            {
                references.Add((mod, number));
            }
        }

        return references;
    }

    /// <summary>
    /// Writes the value into the field's file (skin or main), making the file and the object's entry when they are
    /// missing. A modification already there is changed in place and keeps its data pointer; a new one is added at the
    /// end and takes the field's. The steps work on that modification itself, so undo and redo keep the record's order.
    /// </summary>
    private static void WriteMod(Edit edit, FieldTarget target, FieldValue value)
    {
        var skin = target.Field.Skin;
        var existing = Entry(target, skin)?.Find(target.Field.Id, target.Level);
        if (existing != null)
        {
            var before = existing.Value;
            if (before == value)
            {
                return;
            }

            existing.Value = value;
            edit.Done(new Step(() => existing.Value = value, () => existing.Value = before));
            return;
        }

        var file = FileFor(edit, target.Map, target.Kind, skin);
        var entry = EntryFor(edit, file, target);
        var pointer = ObjectKinds.HasLevels(target.Kind) ? target.Field.Pointer : 0;
        var mod = entry.Set(target.Field.Id, target.Level, pointer, value);
        var index = IndexOf(entry.Mods, mod);
        edit.Done(new Step(() => entry.Insert(index, mod), () => entry.Remove(mod)));
    }

    /// <summary>
    /// Removes the map's modification of the field at the level from both files (the first one, should a file hold
    /// two). A change to a standard object that is left with no modifications goes too. The undo puts that very
    /// modification back where it was, so the record keeps its order and a second one of the field is left as it is.
    /// </summary>
    private static void RemoveMods(Edit edit, FieldTarget target)
    {
        var (field, level) = (target.Field.Id, target.Level);
        foreach (var skin in new[] { false, true })
        {
            var entry = Entry(target, skin);
            var mod = entry?.Find(field, level);
            if (entry == null || mod == null)
            {
                continue;
            }

            var modIndex = IndexOf(entry.Mods, mod);
            entry.Remove(mod);
            edit.Done(new Step(() => entry.Remove(mod), () => entry.Insert(modIndex, mod)));

            var file = (skin ? target.Map.Skin : target.Map.Main)!;
            if (entry.Id == "" && entry.Mods.Count == 0)
            {
                var index = IndexOf(file.Original, entry);
                file.Remove(entry);
                edit.Done(new Step(() => file.Remove(entry), () => file.Insert(index, entry)));
            }
        }
    }

    /// <summary>The object's entry in the main or the skin file, or null.</summary>
    private static ObjectEntry? Entry(FieldTarget target, bool skin) => skin ? target.Found?.Skin : target.Found?.Main;

    /// <summary>The kind's main or skin file; an empty one is made when the map has none.</summary>
    private static ObjectFile FileFor(Edit edit, MapKind map, ObjectKind kind, bool skin)
    {
        var file = skin ? map.Skin : map.Main;
        if (file != null)
        {
            return file;
        }

        var made = ObjectFile.Empty(kind);
        SetFile(map, skin, made);
        edit.Done(new Step(() => SetFile(map, skin, made), () => SetFile(map, skin, null)));
        return made;
    }

    private static void SetFile(MapKind map, bool skin, ObjectFile? file)
    {
        if (skin)
        {
            map.Skin = file;
        }
        else
        {
            map.Main = file;
        }
    }

    /// <summary>The object's entry in a file, added (with no modifications) when the file has none.</summary>
    private static ObjectEntry EntryFor(Edit edit, ObjectFile file, FieldTarget target)
    {
        var custom = target.Found is { Custom: true };
        var entry = custom
            ? file.Custom.FirstOrDefault(entry => entry.Id == target.Id)
            : file.Original.FirstOrDefault(entry => entry.BaseId == target.Id);
        if (entry != null)
        {
            return entry;
        }

        var added = custom ? file.AddCustom(target.Found!.BaseId, target.Id) : file.AddOriginal(target.Id);
        Added(edit, file, added);
        return added;
    }

    /// <summary>Adds a new custom object's entry, with no modifications, to the main or the skin file.</summary>
    private static ObjectEntry AddCustom(Edit edit, MapKind map, ObjectKind kind, bool skin, string baseId, string id)
    {
        var file = FileFor(edit, map, kind, skin);
        var entry = file.AddCustom(baseId, id);
        Added(edit, file, entry);
        return entry;
    }

    /// <summary>Records that an entry was added at the end of its table.</summary>
    private static void Added(Edit edit, ObjectFile file, ObjectEntry entry)
    {
        var index = IndexOf(entry.Id == "" ? file.Original : file.Custom, entry);
        edit.Done(new Step(() => file.Insert(index, entry), () => file.Remove(entry)));
    }

    /// <summary>
    /// A new object's id: the one the user typed, checked, or World Editor's next free one. An id is taken by any of
    /// the kind's standard or custom objects.
    /// </summary>
    private static string NewId(ObjectKind kind, MapKind map, KindData data, string baseId, string? id)
    {
        var taken = new HashSet<string>(data.Objects.Select(obj => obj.Id), StringComparer.Ordinal);
        taken.UnionWith(map.Customs().Select(custom => custom.Id));
        if (id != null)
        {
            if (!NewIds.Valid(id))
            {
                throw new EditException("idInvalid", $"An id is four ASCII letters or digits, not \"{id}\".");
            }

            if (taken.Contains(id))
            {
                throw new EditException("idTaken", $"The id {id} is taken by another {ObjectKinds.Key(kind)}.");
            }

            return id;
        }

        var letter = NewIds.Letter(kind, baseId);
        return NewIds.Next(letter, taken)
            ?? throw new EditException("idTaken", $"Every id from {letter}000 to {letter}ZZZ is taken.");
    }

    /// <summary>A copied value: a <c>TRIGSTR_n</c> text the strings file has becomes that text.</summary>
    private FieldValue Inline(FieldValue value)
    {
        if (value is not FieldValue.Text text || WtsFile.TriggerString(text.Value) is not { } number)
        {
            return value;
        }

        var resolved = _strings?.Get(number);
        return resolved == null ? value : new FieldValue.Text(resolved);
    }

    /// <summary>Where an entry or a modification is in its list (by reference); throws when it is not there.</summary>
    private static int IndexOf<T>(IReadOnlyList<T> items, T item)
        where T : class
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        throw new ArgumentException("The item is not in the list.", nameof(item));
    }

    /// <summary>
    /// A cell of an edit, checked: the field it is about, and its value as the file stores it (null to reset).
    /// </summary>
    private sealed record CheckedCell(FieldTarget Target, FieldValue? Value);

    /// <summary>A field of an object at a level, checked for an edit.</summary>
    private sealed record FieldTarget(
        ObjectKind Kind,
        MapKind Map,
        KindData Data,
        FieldInfo Field,
        int Level,
        string Id,
        StandardObject Standard,
        MapObject? Found);
}

/// <summary>
/// An edit that was refused; it changed nothing. <see cref="Code"/> says why, the message says it in words.
/// </summary>
public sealed class EditException : Exception
{
    /// <summary>An edit refused for the reason <paramref name="code"/>.</summary>
    public EditException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Why: <c>noObject</c> (no such object), <c>noField</c> (no such field, or not at that level), <c>notNumber</c>
    /// (text in a number field), <c>badValue</c> (a value the field's type does not take: a yes/no field's other than
    /// 1 and 0, a choice's that its list does not have), <c>idTaken</c>, <c>idInvalid</c>, <c>readOnly</c> (the kind
    /// or the object cannot be edited), or <c>badText</c> (text a map file cannot hold).
    /// </summary>
    public string Code { get; }
}
