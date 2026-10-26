namespace ObjectEditor.App;

/// <summary>What the window does when the user closes it.</summary>
public enum CloseStep
{
    /// <summary>Close: nothing is unsaved, or the page said to.</summary>
    Close,

    /// <summary>Stay open and ask the page, which asks the user to save, discard or cancel.</summary>
    AskPage,
}

/// <summary>
/// Keeps unsaved edits from being lost when the window is closed. While the map has unsaved edits, closing asks the
/// page; the page says it is asking (<see cref="PageAsking"/>), and the window closes only once it says to
/// (<see cref="Approve"/>). A page that never says it is asking (broken, or not loaded) is <see cref="PageSilent"/>,
/// and the window then asks the user itself. It knows nothing about the window, so it can be tested without one.
/// </summary>
public sealed class CloseGuard
{
    private bool _approved;
    private bool _answered;

    /// <summary>The user closes the window.</summary>
    /// <param name="dirty">True when the map has unsaved edits.</param>
    /// <returns>Whether to close, or to stay open and ask the page.</returns>
    public CloseStep Closing(bool dirty)
    {
        if (_approved || !dirty)
        {
            return CloseStep.Close;
        }

        _answered = false;
        return CloseStep.AskPage;
    }

    /// <summary>The page says it is asking the user (its dialog is open).</summary>
    public void PageAsking() => _answered = true;

    /// <summary>The user chose to close (after saving, or to discard the edits): the next close goes through.</summary>
    public void Approve() => _approved = true;

    /// <summary>
    /// True when the page was asked and has not said it is asking, so the window should ask the user itself. Call it
    /// a little while after <see cref="Closing"/> gave <see cref="CloseStep.AskPage"/>.
    /// </summary>
    public bool PageSilent() => !_approved && !_answered;
}
