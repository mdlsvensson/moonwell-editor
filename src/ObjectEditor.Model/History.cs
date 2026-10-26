using ObjectEditor.Files;

namespace ObjectEditor.Model;

/// <summary>
/// The edits made since the map was opened, for undo and redo. Undo walks back one edit at a time; a new edit drops
/// the edits that were undone, so they can no longer be redone.
/// </summary>
internal sealed class History
{
    private readonly List<Edit> _edits = [];

    // How many edits of the list are done; the ones after it were undone.
    private int _done;

    // How many edits were done when the files were last saved (0 when opened); -1 when that state is gone.
    private int _saved;

    /// <summary>True when the files differ from what was opened or last saved.</summary>
    public bool Dirty => _done != _saved;

    /// <summary>Adds an edit that was just made; an edit with no steps (nothing changed) is not kept.</summary>
    public void Add(Edit edit)
    {
        if (edit.Steps.Count == 0)
        {
            return;
        }

        _edits.RemoveRange(_done, _edits.Count - _done);
        if (_saved > _done)
        {
            _saved = -1;
        }

        _edits.Add(edit);
        _done++;
    }

    /// <summary>Undoes the last edit and returns it; null when there is none.</summary>
    public Edit? Undo()
    {
        if (_done == 0)
        {
            return null;
        }

        _done--;
        _edits[_done].Undo();
        return _edits[_done];
    }

    /// <summary>Makes the last undone edit again and returns it; null when there is none.</summary>
    public Edit? Redo()
    {
        if (_done == _edits.Count)
        {
            return null;
        }

        _edits[_done].Redo();
        _done++;
        return _edits[_done - 1];
    }

    /// <summary>Marks the current state as the one on disk (after a save).</summary>
    public void Saved() => _saved = _done;
}

/// <summary>
/// One edit the user made (a set, a reset, a new object ...), as the steps it took, in order. Each step was done when
/// it was added; undo takes the steps back from the last one, redo does them again from the first.
/// </summary>
internal sealed class Edit
{
    private readonly List<Step> _steps = [];
    private readonly List<(ObjectKind Kind, string Id)> _touched = [];

    /// <summary>The steps, in the order they were done.</summary>
    public IReadOnlyList<Step> Steps => _steps;

    /// <summary>The objects the edit is about (their kind and id), for the search index.</summary>
    public IReadOnlyList<(ObjectKind Kind, string Id)> Touched => _touched;

    /// <summary>Records that the edit is about this object.</summary>
    public void Touch(ObjectKind kind, string id) => _touched.Add((kind, id));

    /// <summary>Adds a step that was just done.</summary>
    public void Done(Step step) => _steps.Add(step);

    public void Undo()
    {
        for (var i = _steps.Count - 1; i >= 0; i--)
        {
            _steps[i].Undo();
        }
    }

    public void Redo()
    {
        foreach (var step in _steps)
        {
            step.Redo();
        }
    }
}

/// <summary>One change to the map's files, with how to make it again and how to take it back.</summary>
/// <param name="Redo">Makes the change again, after <paramref name="Undo"/>.</param>
/// <param name="Undo">Takes the change back.</param>
internal sealed record Step(Action Redo, Action Undo);
