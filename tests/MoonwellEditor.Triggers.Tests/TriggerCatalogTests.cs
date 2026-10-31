namespace MoonwellEditor.Triggers.Tests;

public sealed class TriggerCatalogTests(ITestOutputHelper output)
{
    [Fact]
    public void The_games_file_is_read_with_its_repeated_line()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        Assert.NotEmpty(catalog.Functions);
        Assert.Single(catalog.Presets, preset => preset.Key == "UnitOrderChallengingCall");
    }

    [Fact]
    public void A_sentence_is_its_text_and_arguments_in_order()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var periodic = catalog.Event("TriggerRegisterTimerEventPeriodic")!;

        Assert.Equal("Periodic Event", periodic.Title);
        Assert.Equal(["real"], periodic.Arguments);
        Assert.Equal(
            [new SentencePart("Every "), new SentencePart("Time", 0), new SentencePart(" seconds of game time")],
            periodic.Sentence);
        Assert.Equal("TC_TIME", periodic.Category);
    }

    [Fact]
    public void A_sentence_that_starts_with_its_title_drops_it()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var set = catalog.Action("SetVariable")!;

        Assert.Equal("Set ", set.Sentence[0].Text);
        Assert.Equal(0, set.Sentence[1].Argument);
    }

    [Fact]
    public void Each_argument_shows_once_in_its_sentence()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var wrong = catalog.Functions
            .Where(function => function.Sentence.Count(part => part.Argument >= 0) != function.Arguments.Count)
            .Select(function => $"{function.Kind} {function.Name}: {function.Arguments.Count}")
            .ToList();
        output.WriteLine(string.Join("\n", wrong));

        Assert.Empty(wrong);
    }

    [Fact]
    public void A_presets_text_is_the_editors()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var red = catalog.Preset("Player00")!;

        Assert.Equal("player", red.Type);
        Assert.Equal("Player 1 (Red)", red.Text);
    }

    [Fact]
    public void Types_keep_the_order_of_the_variables_dialog()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var names = catalog.Types.Select(type => type.Key).ToList();

        Assert.True(names.IndexOf("abilcode") < names.IndexOf("ability"));
        Assert.True(names.IndexOf("ability") < names.IndexOf("attacktype"));
        Assert.Equal("integer", catalog.Type("unitcode")!.Base);
    }

    [Fact]
    public void A_category_gives_its_title_and_icon()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var unit = catalog.Categories.Single(category => category.Key == "TC_UNIT");

        Assert.Equal("Unit", unit.Title);
        Assert.EndsWith("Actions-Unit.blp", unit.Icon);
        Assert.NotNull(InstallMaps.ReadGame(unit.Icon));
        Assert.True(unit.Shown);
    }

    [Fact]
    public void A_hint_comes_from_the_trigger_strings()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        Assert.StartsWith("The facing angle is expected in degrees", catalog.Action("CreateNUnitsAtLoc")!.Hint);
    }
}
