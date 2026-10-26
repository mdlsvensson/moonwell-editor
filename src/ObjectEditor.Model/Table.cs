using ObjectEditor.Files;

namespace ObjectEditor.Model;

/// <summary>
/// One cell of an edit of many cells (<see cref="Session.SetCells"/>): a field of an object at a level.
/// </summary>
/// <param name="Id">The object's id.</param>
/// <param name="Field">The field's id.</param>
/// <param name="Level">The level, as <see cref="Session.Set"/> takes it (0 for a field without levels).</param>
/// <param name="Value">The new value as Set takes it; null resets the cell to its standard value.</param>
public sealed record CellEdit(string Id, string Field, int Level, string? Value);

/// <summary>The table's edits: many cells changed as one edit.</summary>
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
