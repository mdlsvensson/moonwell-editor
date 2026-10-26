using ObjectEditor.App;

namespace ObjectEditor.App.Tests;

public sealed class CloseGuardTests
{
    [Fact]
    public void With_nothing_unsaved_the_window_closes_at_once()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.Close, guard.Closing(dirty: false));
    }

    [Fact]
    public void With_unsaved_edits_the_page_is_asked_until_it_says_to_close()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true));
        guard.PageAsking();
        Assert.False(guard.PageSilent());

        // The user picked Cancel in the page's dialog, then closes again: the page is asked again.
        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true));

        guard.Approve();
        Assert.Equal(CloseStep.Close, guard.Closing(dirty: true));
        Assert.False(guard.PageSilent());
    }

    [Fact]
    public void A_page_that_does_not_answer_is_silent()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true));

        Assert.True(guard.PageSilent());
    }

    [Fact]
    public void A_page_that_answered_before_is_silent_when_it_does_not_answer_the_next_close()
    {
        var guard = new CloseGuard();
        guard.Closing(dirty: true);
        guard.PageAsking();

        guard.Closing(dirty: true);

        Assert.True(guard.PageSilent());
    }
}
