using MoonwellEditor.App;

namespace MoonwellEditor.App.Tests;

public sealed class CloseGuardTests
{
    [Fact]
    public void With_no_page_and_nothing_unsaved_the_window_closes_at_once()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.Close, guard.Closing(dirty: false, pageLoaded: false));
    }

    [Fact]
    public void A_loaded_page_is_asked_even_with_nothing_unsaved()
    {
        // The page may hold a typed value it has not sent; it closes the window at once when it holds none.
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: false, pageLoaded: true));
        guard.PageAsking();
        guard.Approve();
        Assert.Equal(CloseStep.Close, guard.Closing(dirty: false, pageLoaded: true));
    }

    [Fact]
    public void With_unsaved_edits_the_page_is_asked_until_it_says_to_close()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true, pageLoaded: true));
        guard.PageAsking();
        Assert.Equal(CloseStep.Wait, guard.PageSilent(dirty: true));

        // The user picked Cancel in the page's dialog, then closes again: the page is asked again.
        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true, pageLoaded: true));

        guard.Approve();
        Assert.Equal(CloseStep.Close, guard.Closing(dirty: true, pageLoaded: true));
        Assert.Equal(CloseStep.Wait, guard.PageSilent(dirty: true));
    }

    [Fact]
    public void A_page_that_does_not_answer_is_silent_and_the_window_asks_the_user()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true, pageLoaded: true));

        Assert.Equal(CloseStep.AskUser, guard.PageSilent(dirty: true));
    }

    [Fact]
    public void A_silent_page_with_nothing_unsaved_lets_the_window_close()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: false, pageLoaded: true));

        Assert.Equal(CloseStep.Close, guard.PageSilent(dirty: false));
    }

    [Fact]
    public void With_no_page_and_unsaved_edits_the_window_asks_the_user()
    {
        var guard = new CloseGuard();

        Assert.Equal(CloseStep.AskPage, guard.Closing(dirty: true, pageLoaded: false));

        Assert.Equal(CloseStep.AskUser, guard.PageSilent(dirty: true));
    }

    [Fact]
    public void A_page_that_answered_before_is_silent_when_it_does_not_answer_the_next_close()
    {
        var guard = new CloseGuard();
        guard.Closing(dirty: true, pageLoaded: true);
        guard.PageAsking();

        guard.Closing(dirty: true, pageLoaded: true);

        Assert.Equal(CloseStep.AskUser, guard.PageSilent(dirty: true));
    }
}
