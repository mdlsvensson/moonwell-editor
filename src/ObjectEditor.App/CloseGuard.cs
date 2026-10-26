namespace ObjectEditor.App;

/// <summary>What the window does when the user closes it.</summary>
public enum CloseStep
{
    /// <summary>Close: nothing is unsaved and no page could hold a typed value, or the page said to.</summary>
    Close,

    /// <summary>Stay open and ask the page, which asks the user to save, discard or cancel when it needs to.</summary>
    AskPage,

    /// <summary>Stay open: the page is asking the user, and closes the window once they have answered.</summary>
    Wait,

    /// <summary>The page did not answer and the map has unsaved edits: the window asks the user itself.</summary>
    AskUser,
}

/// <summary>
/// Keeps unsaved edits from being lost when the window is closed. While a page is loaded, closing asks it, since it may
/// hold a typed value it has not sent even when the map has no unsaved edits; it closes the window at once when it
/// holds none and the map is clean. With no page, only unsaved edits keep the window open. The page says it is
/// handling the close (<see cref="PageAsking"/>), and the window closes only once it says to (<see cref="Approve"/>).
/// A page that never says so (broken) is silent (<see cref="PageSilent"/>): the window then closes, or asks the user
/// itself when the map has unsaved edits. It knows nothing about the window, so it can be tested without one.
/// </summary>
public sealed class CloseGuard
{
    private bool _approved;
    private bool _answered;

    /// <summary>The user closes the window.</summary>
    /// <param name="dirty">True when the map has unsaved edits.</param>
    /// <param name="pageLoaded">True when the page has loaded (it may hold a typed value).</param>
    /// <returns>Whether to close, or to stay open and ask the page.</returns>
    public CloseStep Closing(bool dirty, bool pageLoaded)
    {
        if (_approved || (!dirty && !pageLoaded))
        {
            return CloseStep.Close;
        }

        _answered = false;
        return CloseStep.AskPage;
    }

    /// <summary>The page says it is handling the close (asking the user, or about to close the window).</summary>
    public void PageAsking() => _answered = true;

    /// <summary>The user chose to close (after saving, or to discard the edits): the next close goes through.</summary>
    public void Approve() => _approved = true;

    /// <summary>
    /// What to do a little while after <see cref="Closing"/> gave <see cref="CloseStep.AskPage"/>:
    /// <see cref="CloseStep.Wait"/> when the page answered (or the close was approved), else the page is silent, and
    /// the window closes (<see cref="CloseStep.Close"/>) or, with unsaved edits, asks the user itself
    /// (<see cref="CloseStep.AskUser"/>).
    /// </summary>
    /// <param name="dirty">True when the map has unsaved edits.</param>
    public CloseStep PageSilent(bool dirty)
    {
        if (_approved || _answered)
        {
            return CloseStep.Wait;
        }

        return dirty ? CloseStep.AskUser : CloseStep.Close;
    }
}
