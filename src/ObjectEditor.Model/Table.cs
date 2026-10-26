using ObjectEditor.Files;
using ObjectEditor.GameData;

namespace ObjectEditor.Model;

/// <summary>
/// One cell of an edit of many cells (<see cref="Session.SetCells"/>): a field of an object at a level.
/// </summary>
/// <param name="Id">The object's id.</param>
/// <param name="Field">The field's id.</param>
/// <param name="Level">The level, as <see cref="Session.Set"/> takes it (0 for a field without levels).</param>
/// <param name="Value">The new value as Set takes it; null resets the cell to its standard value.</param>
public sealed record CellEdit(string Id, string Field, int Level, string? Value);

/// <summary>One column of the table: a field, at a level for a field with levels.</summary>
/// <param name="Field">The field's id.</param>
/// <param name="Level">The level, from 1, for a field with levels; 0 for any other field.</param>
public sealed record Column(string Field, int Level);

/// <summary>One cell of the table: what <see cref="Session.Object"/> shows for the field at the level.</summary>
/// <param name="Value">The value shown (see <see cref="FieldView.Value"/>); empty when the cell does not apply.</param>
/// <param name="Base">The standard value (see <see cref="FieldView.Base"/>); empty when the cell does not apply.</param>
/// <param name="Changed">True when the map has a modification of the field at this level.</param>
/// <param name="Applies">
/// False when the field does not belong to the object, or the level is above the object's level count.
/// </param>
/// <param name="Warning">What is wrong with the map's value (see <see cref="FieldView.Warning"/>); else null.</param>
public sealed record TableCell(string Value, string Base, bool Changed, bool Applies, string? Warning);

/// <summary>One object's row of the table.</summary>
/// <param name="Id">The object's id.</param>
/// <param name="Name">The name, as <see cref="ObjectView.Name"/> shows it.</param>
/// <param name="BaseId">For a custom object, the standard object it is based on; null for a standard object.</param>
/// <param name="ReadOnly">Why the object cannot be edited; null when it can.</param>
/// <param name="Cells">One cell for each column, in the order of the columns.</param>
public sealed record TableRow(string Id, string Name, string? BaseId, string? ReadOnly, IReadOnlyList<TableCell> Cells);

/// <summary>The table: the cells of many objects for chosen fields, and its edits.</summary>
public sealed partial class Session
{
    /// <summary>
    /// Sets (or, for a null value, resets) many cells of one kind as one edit, so one undo takes them all back. Every
    /// cell is checked first, as <see cref="Set"/> and <see cref="Reset"/> check one; when one is refused, nothing
    /// changes and the refusal's message starts with the cell (<c>h001 Hit Points (uhpm): ...</c>). Each cell then
    /// follows <see cref="Set"/>'s rules, with one addition: a strings file entry that another modification refers to
    /// before this edit counts as shared for every cell of it, so two cells that share an entry are both written
    /// inline.
    /// </summary>
    /// <returns>How many cells changed (a cell that already has its value changes nothing).</returns>
    /// <exception cref="EditException">The first refused cell's, with the codes of <see cref="Set"/>.</exception>
    public int SetCells(ObjectKind kind, IReadOnlyList<CellEdit> edits)
    {
        var cells = new List<CheckedCell>();
        foreach (var cell in edits)
        {
            try
            {
                cells.Add(CheckCell(kind, cell.Id, cell.Field, cell.Level, cell.Value));
            }
            catch (EditException refused)
            {
                throw Refused(kind, cell, refused);
            }
        }

        if (cells.Count == 0)
        {
            return 0;
        }

        var before = TriggerStringMods();
        var edit = new Edit();
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            if (touched.Add(edits[i].Id))
            {
                edit.Touch(kind, edits[i].Id);
            }

            var steps = edit.Steps.Count;
            try
            {
                ApplyCell(edit, cells[i], before);
            }
            catch (EditException refused)
            {
                // Only text the strings file cannot hold is found this late; the cells before it are taken back.
                edit.Undo();
                throw Refused(kind, edits[i], refused);
            }
            catch
            {
                edit.Undo();
                throw;
            }

            if (edit.Steps.Count > steps)
            {
                changed++;
            }
        }

        Commit(edit);
        return changed;
    }

    /// <summary>
    /// The table's cells: one row for each object of <paramref name="ids"/> that the kind has, in the order of the ids
    /// (an unknown id is left out), and one cell for each column. A cell is what <see cref="Object"/> shows for the
    /// field at the level; only the asked fields are worked out, so a long table stays fast.
    /// </summary>
    /// <exception cref="EditException"><c>noField</c>: a column's field is not a field of the kind.</exception>
    public IReadOnlyList<TableRow> Table(ObjectKind kind, IReadOnlyList<string> ids, IReadOnlyList<Column> columns)
    {
        var map = _kinds[kind];
        var data = _game.Kind(kind);
        var fields = new List<FieldInfo>();
        foreach (var column in columns)
        {
            fields.Add(data.Field(column.Field)
                ?? throw new EditException("noField", $"{ObjectKinds.Key(kind)} has no field {column.Field}."));
        }

        var resolver = new Resolver(kind, data, _strings);
        var rows = new List<TableRow>();
        foreach (var id in ids)
        {
            var found = map.Find(id);
            var standard = found is { Custom: true } ? data.Object(found.BaseId) : data.Object(id);
            if (standard == null && found is not { Custom: true })
            {
                continue;
            }

            var levels = resolver.Levels(standard, found);
            var cells = new List<TableCell>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                var field = fields[i];
                var level = columns[i].Level;
                var applies = standard != null
                    && data.Applies(field, standard)
                    && (field.PerLevel ? level >= 1 && level <= levels : level == 0);
                if (!applies)
                {
                    cells.Add(new TableCell("", "", false, false, null));
                    continue;
                }

                var view = resolver.Field(field, level, standard!, found);
                cells.Add(new TableCell(view.Value, view.Base, view.Changed, true, view.Warning));
            }

            rows.Add(new TableRow(
                id,
                resolver.Name(standard, found, id),
                found is { Custom: true } ? found.BaseId : null,
                map.ReadOnly(found),
                cells));
        }

        return rows;
    }

    /// <summary>A cell's refusal, with the cell named at the start of its message.</summary>
    private EditException Refused(ObjectKind kind, CellEdit cell, EditException refused)
    {
        var label = _game.Kind(kind).Field(cell.Field)?.Label;
        var name = label == null ? cell.Id
            : cell.Level > 0 ? $"{cell.Id} {label} · {cell.Level}"
            : $"{cell.Id} {label}";
        return new EditException(refused.Code, $"{name} ({cell.Field}): {refused.Message}");
    }
}
