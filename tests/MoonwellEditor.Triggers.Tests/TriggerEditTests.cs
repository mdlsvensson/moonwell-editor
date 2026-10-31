using War3Net.Build.Extensions;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers.Tests;

public sealed class TriggerEditTests(ITestOutputHelper output)
{
    private sealed class Names : ITriggerNames
    {
        public string? ObjectName(string type, string id) => (type, id) switch
        {
            ("unitcode", "hfoo") => "Footman",
            _ => null,
        };

        public string Text(string value) => value;
    }

    private static TriggerDocument? LostTemple()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        return catalog == null || store == null
            ? null
            : TriggerDocument.Read(
                store.Read(TriggerDocument.TriggersFile), store.Read(TriggerDocument.CustomTextFile), catalog);
    }

    private static int Initialization(TriggerDocument document) =>
        document.Tree().Single(item => item.Kind == "category").Id;

    private static MapTriggers Reread(TriggerDocument document)
    {
        using var reader = new BinaryReader(new MemoryStream(document.TriggerBytes()!));
        return reader.ReadMapTriggers(document.Catalog.Data);
    }

    [Fact]
    public void A_new_trigger_gets_a_free_name_and_an_id_of_its_type()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var second = document.Add("trigger", Initialization(document));

        Assert.Equal(3, id >> 24);
        Assert.NotEqual(id, second);
        var read = Reread(document);
        Assert.Equal(["Untitled Trigger 001", "Untitled Trigger 002"], read.TriggerItems
            .OfType<TriggerDefinition>().Where(each => each.Id != 0x3000000).Select(each => each.Name));
        Assert.Equal(3, read.TriggerItemCounts[TriggerItemType.Gui]);
    }

    [Fact]
    public void A_new_trigger_has_an_entry_in_the_custom_text_file_a_comment_has_none()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var script = document.Add("script", Initialization(document));
        document.Add("comment", Initialization(document));
        document.SetCode(script, "call DoNothing()");

        using var reader = new BinaryReader(new MemoryStream(document.CustomTextBytes()!));
        var read = reader.ReadMapCustomTextTriggers();
        Assert.Equal(2, read.CustomTextTriggers.Count);
        Assert.Equal("call DoNothing()\0", read.CustomTextTriggers[1].Code);
        Assert.Equal("call DoNothing()", document.CodeOf(script));
    }

    [Fact]
    public void A_snapshot_gives_back_the_same_bytes()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var before = document.TriggerBytes()!;
        var snapshot = document.Snapshot();
        var id = document.Add("trigger", Initialization(document));
        document.AddFunction(id, null, 0, 0, "event", "TriggerRegisterTimerEventPeriodic");
        document.Rename(id, "Tick");
        var melee = document.Tree().Single(item => item.Name == "Melee Initialization").Id;
        document.AddFunction(melee, null, 0, 0, "action", "DoNothing");
        document.Delete(Initialization(document));

        document.Restore(snapshot);

        Assert.Equal(before, document.TriggerBytes());
    }

    [Fact]
    public void A_function_starts_with_the_games_defaults_and_reads_as_its_sentence()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        document.AddFunction(id, null, 0, 0, "event", "TriggerRegisterTimerEventPeriodic");
        document.AddFunction(id, null, 0, 0, "action", "CreateNUnitsAtLoc");

        var view = document.Trigger(id, new Names());

        Assert.Equal("Every 2.00 seconds of game time", string.Concat(view.Events[0].Sentence.Select(part => part.Text)));
        var action = view.Actions[0];
        Assert.Equal("Unit", action.Prefix);
        Assert.Equal(
            "Create 1 Footman for Player 1 (Red) at (Center of (Playable map area)) facing Default building facing (270) degrees",
            string.Concat(action.Sentence.Select(part => part.Text)));
        Assert.Equal(id, Reread(document).TriggerItems.OfType<TriggerDefinition>().Single(each => each.Id == id).Id);
    }

    [Fact]
    public void Every_function_of_the_game_can_be_added_and_written()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var wrong = new List<string>();
        foreach (var function in document.Catalog.Functions.Where(each => each.Kind != "call"))
        {
            try
            {
                document.AddFunction(id, null, 0, int.MaxValue, function.Kind, function.Name);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                wrong.Add($"{function.Name}: {exception.Message}");
            }
        }

        output.WriteLine(string.Join("\n", wrong));
        Assert.Empty(wrong);
        var read = Reread(document);
        Assert.Equal(
            document.Catalog.Functions.Count(each => each.Kind != "call"),
            read.TriggerItems.OfType<TriggerDefinition>().Single(each => each.Id == id).Functions.Count);
        Assert.NotEmpty(document.Trigger(id, new Names()).Actions);
    }

    [Fact]
    public void A_block_holds_its_functions_by_branch()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var ifThen = document.AddFunction(id, null, 0, 0, "action", "IfThenElseMultiple");
        document.AddFunction(id, ifThen, 1, 0, "action", "DoNothing");
        document.AddFunction(id, ifThen, 2, 0, "action", "ReturnAction");
        document.AddFunction(id, ifThen, 0, 0, "condition", "OperatorCompareBoolean");

        var blocks = document.Trigger(id, new Names()).Actions[0].Blocks;

        Assert.Equal(["If - Conditions", "Then - Actions", "Else - Actions"], blocks.Select(block => block.Title));
        Assert.Equal("OperatorCompareBoolean", Assert.Single(blocks[0].Functions).Name);
        Assert.Equal("DoNothing", Assert.Single(blocks[1].Functions).Name);
        Assert.Equal("ReturnAction", Assert.Single(blocks[2].Functions).Name);
        var inRead = Reread(document).TriggerItems.OfType<TriggerDefinition>().Single(each => each.Id == id).Functions[0];
        Assert.Equal(3, inRead.ChildFunctions.Count);
    }

    [Fact]
    public void Moving_a_function_keeps_its_kinds_order()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        document.AddFunction(id, null, 0, 0, "event", "MapInitializationEvent");
        document.AddFunction(id, null, 0, int.MaxValue, "action", "DoNothing");
        document.AddFunction(id, null, 0, int.MaxValue, "action", "ReturnAction");

        var view = document.Trigger(id, new Names());
        document.MoveFunction(id, view.Actions[1].Path, id, null, 0, 0);

        view = document.Trigger(id, new Names());
        Assert.Equal(["ReturnAction", "DoNothing"], view.Actions.Select(action => action.Name));
        Assert.Single(view.Events);
    }

    [Fact]
    public void A_function_moves_into_a_block_and_out_of_it()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var first = document.AddFunction(id, null, 0, int.MaxValue, "action", "DoNothing");
        var ifThen = document.AddFunction(id, null, 0, int.MaxValue, "action", "IfThenElseMultiple");
        document.AddFunction(id, ifThen, 2, 0, "action", "ReturnAction");

        document.MoveFunction(id, first, id, ifThen, 1, 0);

        var actions = document.Trigger(id, new Names()).Actions;
        var block = Assert.Single(actions).Blocks;
        Assert.Equal("DoNothing", Assert.Single(block[1].Functions).Name);
        Assert.Equal("ReturnAction", Assert.Single(block[2].Functions).Name);

        document.MoveFunction(id, block[1].Functions[0].Path, id, null, 0, int.MaxValue);

        actions = document.Trigger(id, new Names()).Actions;
        Assert.Equal(["IfThenElseMultiple", "DoNothing"], actions.Select(action => action.Name));
        Assert.Empty(actions[0].Blocks[1].Functions);
        var read = Reread(document).TriggerItems.OfType<TriggerDefinition>().Single(each => each.Id == id);
        Assert.Null(read.Functions[1].Branch);
    }

    [Fact]
    public void A_function_cannot_move_into_itself_or_a_block_of_another_kind()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var outer = document.AddFunction(id, null, 0, int.MaxValue, "action", "IfThenElseMultiple");
        var inner = document.AddFunction(id, outer, 1, 0, "action", "IfThenElseMultiple");
        var plain = document.AddFunction(id, null, 0, int.MaxValue, "action", "DoNothing");

        Assert.Throws<TriggerEditException>(() => document.MoveFunction(id, outer, id, inner, 1, 0));
        Assert.Throws<TriggerEditException>(() => document.MoveFunction(id, outer, id, outer, 1, 0));
        Assert.Throws<TriggerEditException>(() => document.MoveFunction(id, plain, id, outer, 0, 0));
        Assert.Equal(2, document.Trigger(id, new Names()).Actions.Count);
    }

    [Fact]
    public void A_function_moves_to_another_trigger()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var from = document.Add("trigger", category);
        var to = document.Add("trigger", category);
        var wait = document.AddFunction(from, null, 0, 0, "action", "TriggerSleepAction");
        document.AddFunction(to, null, 0, 0, "action", "DoNothing");

        document.MoveFunction(from, wait, to, null, 0, 0);

        Assert.Empty(document.Trigger(from, new Names()).Actions);
        Assert.Equal(["TriggerSleepAction", "DoNothing"], document.Trigger(to, new Names()).Actions.Select(action => action.Name));
    }

    [Fact]
    public void A_function_shows_its_title_for_its_card()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        document.AddFunction(id, null, 0, 0, "action", "CreateNUnitsAtLoc");

        Assert.Equal("Create Units Facing Angle", document.Trigger(id, new Names()).Actions[0].Title);
    }

    [Fact]
    public void A_category_gives_a_card_for_everything_directly_in_it()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var trigger = document.Add("trigger", category);
        var comment = document.Add("comment", category);
        var script = document.Add("script", category);
        var variable = document.AddVariable(new VariableInput("Count", "integer", false, 1, ""), category);
        var inner = document.Add("category", category);
        document.Add("trigger", inner);

        var cards = document.Cards(category, new Names());

        Assert.Equal(["trigger", "trigger", "comment", "script", "variable", "category"], cards.Select(card => card.Kind));
        Assert.NotNull(cards.Single(card => card.Id == trigger).Trigger);
        Assert.NotNull(cards.Single(card => card.Id == comment).Trigger);
        Assert.NotNull(cards.Single(card => card.Id == script).Trigger);
        Assert.Equal("Count", cards.Single(card => card.Id == variable).Variable!.Name);
        Assert.Equal(1, cards.Single(card => card.Id == inner).Count);
    }

    [Fact]
    public void A_cards_place_is_kept_and_undone_and_goes_with_the_item()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);
        Assert.Null(document.LayoutBytes());
        var before = document.Snapshot();

        document.SetPlaces([new CardMove(id, 40, 80)]);
        document.SetFolded(id, true);

        var card = document.Cards(category, new Names()).Single(each => each.Id == id);
        Assert.Equal(new CardPlace(40, 80, true), card.Place);
        var again = TriggerDocument.Read(document.TriggerBytes(), document.CustomTextBytes(), document.Catalog, null, document.LayoutBytes());
        Assert.Equal(new CardPlace(40, 80, true), again.Cards(category, new Names()).Single(each => each.Id == id).Place);

        document.Restore(before);
        Assert.Null(document.LayoutBytes());
        Assert.Null(document.Cards(category, new Names()).Single(each => each.Id == id).Place);

        document.SetPlaces([new CardMove(id, 40, 80)]);
        document.Delete(id);
        Assert.DoesNotContain("\"" + id + "\"", System.Text.Encoding.UTF8.GetString(document.LayoutBytes()!));
    }

    [Fact]
    public void A_layout_file_that_cannot_be_read_is_ignored()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(null, null, catalog, null, "not json"u8.ToArray());

        Assert.Null(document.Problem);
        Assert.Null(document.LayoutBytes());
    }

    [Fact]
    public void A_trigger_moved_to_the_map_itself_goes_in_a_category()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var root = document.Tree()[0].Id;
        var other = document.Add("category", root);
        var trigger = document.Add("trigger", other);

        document.Move(trigger, root, 0);

        var tree = document.Tree();
        Assert.NotEqual(root, tree.Single(item => item.Id == trigger).Parent);
        Assert.Equal("category", tree.Single(item => item.Id == tree.Single(each => each.Id == trigger).Parent).Kind);
    }

    [Fact]
    public void A_parameter_takes_each_kind_of_value()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var path = document.AddFunction(id, null, 0, 0, "action", "CreateNUnitsAtLoc");
        document.AddVariable(new VariableInput("Spawned", "integer", false, 1, ""), Initialization(document));

        document.SetParameter(id, path + "/p0", new ParamInput("variable", "Spawned"));
        document.SetParameter(id, path + "/p1", new ParamInput("value", "hfoo"));
        document.SetParameter(id, path + "/p2", new ParamInput("preset", "Player01"));
        document.SetParameter(id, path + "/p3", new ParamInput("function", "GetUnitLoc"));

        var parts = document.Trigger(id, new Names()).Actions[0].Sentence.Where(part => part.Parameter != null).ToList();
        Assert.Equal("Spawned", parts[0].Text);
        Assert.Equal("Footman", parts[1].Text);
        Assert.Equal("Player 2 (Blue)", parts[2].Text);
        Assert.Equal("(Position of (Triggering unit))", parts[3].Text);
        var inner = Assert.Single(parts[3].Parameter!.Function!.Sentence, part => part.Parameter != null);
        Assert.Equal("function", inner.Parameter!.Kind);

        document.SetParameter(id, inner.Parameter.Path, new ParamInput("variable", "gg_unit_hfoo_0001"));

        parts = document.Trigger(id, new Names()).Actions[0].Sentence.Where(part => part.Parameter != null).ToList();
        Assert.Equal("(Position of Footman 0001 <gen>)", parts[3].Text);
    }

    [Fact]
    public void An_array_variable_gets_an_index()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var path = document.AddFunction(id, null, 0, 0, "action", "TriggerSleepAction");
        document.AddVariable(new VariableInput("Times", "real", true, 4, ""), Initialization(document));

        document.SetParameter(id, path + "/p0", new ParamInput("variable", "Times"));
        document.SetParameter(id, path + "/p0/i", new ParamInput("value", "3"));

        var part = document.Trigger(id, new Names()).Actions[0].Sentence.Single(each => each.Parameter != null);
        Assert.Equal("Times[3]", part.Text);
    }

    [Fact]
    public void Set_variable_reads_its_value_as_the_variables_type()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        var path = document.AddFunction(id, null, 0, 0, "action", "SetVariable");
        document.AddVariable(new VariableInput("Delay", "real", false, 1, ""), Initialization(document));

        document.SetParameter(id, path + "/p0", new ParamInput("variable", "Delay"));
        document.SetParameter(id, path + "/p1", new ParamInput("value", "1.5"));

        var parts = document.Trigger(id, new Names()).Actions[0].Sentence.Where(part => part.Parameter != null).ToList();
        Assert.Equal("real", parts[1].Parameter!.Type);
        Assert.Equal("1.50", parts[1].Text);
    }

    [Fact]
    public void A_renamed_variable_or_trigger_is_followed_by_what_uses_it()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);
        var path = document.AddFunction(id, null, 0, 0, "action", "TriggerSleepAction");
        var variable = document.AddVariable(new VariableInput("Delay", "real", false, 1, ""), category);
        document.SetParameter(id, path + "/p0", new ParamInput("variable", "Delay"));
        var other = document.Add("trigger", category);
        var run = document.AddFunction(other, null, 0, 0, "action", "ConditionalTriggerExecute");
        document.SetParameter(other, run + "/p0", new ParamInput("variable", "gg_trg_Untitled_Trigger_001"));

        document.SetVariable(variable, new VariableInput("Pause", "real", false, 1, ""));
        document.Rename(id, "Wait Here");

        Assert.Equal("Pause", document.Trigger(id, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Text);
        Assert.Equal(
            "gg_trg_Wait_Here",
            document.Trigger(other, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Parameter!.Value);
    }

    [Fact]
    public void A_deleted_variable_leaves_its_parameters_without_a_value()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);
        var path = document.AddFunction(id, null, 0, 0, "action", "TriggerSleepAction");
        var variable = document.AddVariable(new VariableInput("Delay", "real", false, 1, ""), category);
        document.SetParameter(id, path + "/p0", new ParamInput("variable", "Delay"));

        document.Delete(variable);

        Assert.Equal("none", document.Trigger(id, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Parameter!.Kind);
        Assert.Empty(document.Variables());
        Assert.Empty(Reread(document).Variables);
    }

    [Fact]
    public void A_number_is_checked_and_another_kind_of_variable_clears_the_value_set()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);
        var wait = document.AddFunction(id, null, 0, 0, "action", "TriggerSleepAction");
        document.AddVariable(new VariableInput("Delay", "real", false, 1, ""), category);
        document.AddVariable(new VariableInput("Pause", "real", false, 1, ""), category);
        document.AddVariable(new VariableInput("Count", "integer", false, 1, ""), category);

        Assert.Throws<TriggerEditException>(() => document.SetParameter(id, wait + "/p0", new ParamInput("value", "soon")));

        var set = document.AddFunction(id, null, 0, 0, "action", "SetVariable");
        document.SetParameter(id, set + "/p0", new ParamInput("variable", "Delay"));
        document.SetParameter(id, set + "/p1", new ParamInput("value", "1.5"));
        document.SetParameter(id, set + "/p0", new ParamInput("variable", "Pause"));

        var parts = document.Trigger(id, new Names()).Actions[0].Sentence.Where(part => part.Parameter != null).ToList();
        Assert.Equal("value", parts[1].Parameter!.Kind);

        document.SetParameter(id, set + "/p0", new ParamInput("variable", "Count"));

        parts = document.Trigger(id, new Names()).Actions[0].Sentence.Where(part => part.Parameter != null).ToList();
        Assert.Equal("none", parts[1].Parameter!.Kind);
    }

    [Fact]
    public void Deleting_a_category_takes_every_trigger_in_it_even_one_that_names_another()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = document.Add("category", document.Tree()[0].Id);
        var first = document.Add("trigger", category);
        var second = document.Add("trigger", category);
        var run = document.AddFunction(second, null, 0, 0, "action", "ConditionalTriggerExecute");
        document.SetParameter(second, run + "/p0", new ParamInput("variable", "gg_trg_Untitled_Trigger_001"));

        document.Delete(category);

        Assert.DoesNotContain(document.Tree(), item => item.Id == first || item.Id == second);
        Assert.DoesNotContain(Reread(document).TriggerItems, item => item.Id == first || item.Id == second);
    }

    [Fact]
    public void Deleting_a_comment_clears_nothing_and_two_triggers_cannot_share_a_script_name()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var trigger = document.Add("trigger", category);
        document.Rename(trigger, "A B");
        Assert.Throws<TriggerEditException>(() => document.Rename(document.Add("trigger", category), "A_B"));
        var other = document.Add("trigger", category);
        var run = document.AddFunction(other, null, 0, 0, "action", "ConditionalTriggerExecute");
        document.SetParameter(other, run + "/p0", new ParamInput("variable", "gg_trg_A_B"));
        var comment = document.Add("comment", category);
        document.Rename(comment, "A B");

        document.Delete(comment);

        Assert.Equal("variable", document.Trigger(other, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Parameter!.Kind);
    }

    [Fact]
    public void Another_type_of_variable_clears_its_uses_and_an_array_gains_or_loses_its_index()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);
        var wait = document.AddFunction(id, null, 0, 0, "action", "TriggerSleepAction");
        var variable = document.AddVariable(new VariableInput("Delay", "real", false, 1, ""), category);
        document.SetParameter(id, wait + "/p0", new ParamInput("variable", "Delay"));

        document.SetVariable(variable, new VariableInput("Delay", "real", true, 4, ""));
        Assert.Equal("Delay[0]", document.Trigger(id, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Text);

        document.SetVariable(variable, new VariableInput("Delay", "real", false, 1, ""));
        Assert.Equal("Delay", document.Trigger(id, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Text);

        document.SetVariable(variable, new VariableInput("Delay", "integer", false, 1, ""));
        Assert.Equal("none", document.Trigger(id, new Names()).Actions[0].Sentence.Single(part => part.Parameter != null).Parameter!.Kind);
    }

    [Fact]
    public void A_variables_first_value_is_checked_by_its_type()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        Assert.Throws<TriggerEditException>(() => document.AddVariable(new VariableInput("A", "integer", false, 1, "abc"), category));
        Assert.Throws<TriggerEditException>(() => document.AddVariable(new VariableInput("B", "boolean", false, 1, "maybe"), category));
        Assert.Throws<TriggerEditException>(() => document.AddVariable(new VariableInput("C", "unit", false, 1, "someone"), category));
        document.AddVariable(new VariableInput("D", "integer", false, 1, "5"), category);
        document.AddVariable(new VariableInput("E", "boolean", false, 1, "true"), category);
        document.AddVariable(new VariableInput("F", "unitcode", false, 1, "hfoo"), category);
        document.AddVariable(new VariableInput("G", "string", false, 1, "any text"), category);
        document.AddVariable(new VariableInput("H", "player", false, 1, "Player00"), category);
        Assert.Equal(5, document.Variables().Count);
    }

    [Fact]
    public void Renaming_a_trigger_converted_to_text_renames_it_in_its_code()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open("Maps/FrozenThrone/Scenario/(6)BomberCommand.w3x");
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(
            store.Read(TriggerDocument.TriggersFile), store.Read(TriggerDocument.CustomTextFile), catalog);
        var custom = document.Tree().First(item => item.Kind == "custom");
        var before = TriggerJass.Identifier(custom.Name);
        Assert.Contains("InitTrig_" + before, document.CodeOf(custom.Id));

        document.Rename(custom.Id, "Renamed Here");

        Assert.Contains("InitTrig_Renamed_Here", document.CodeOf(custom.Id));
        Assert.DoesNotContain("InitTrig_" + before, document.CodeOf(custom.Id));
    }

    [Fact]
    public void The_end_of_a_custom_text_never_reaches_the_page_and_is_written_as_World_Editor_does()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.ForgottenHollow);
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var bytes = store.Read(TriggerDocument.CustomTextFile)!;
        var document = TriggerDocument.Read(store.Read(TriggerDocument.TriggersFile), bytes, catalog);
        var script = document.Tree().First(item => item.Kind == "script");
        var text = document.CodeOf(script.Id)!;
        Assert.False(text.EndsWith('\0'));

        document.SetCode(script.Id, text + "\0-- more");
        document.SetCode(script.Id, text);

        Assert.Equal(bytes, document.CustomTextBytes());
    }

    [Fact]
    public void A_copy_stays_what_it_was_whatever_happens_to_the_trigger()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);
        var wait = document.AddFunction(id, null, 0, 0, "action", "TriggerSleepAction");
        document.CopyFunction(id, wait);
        document.AddFunction(id, null, 0, 0, "event", "MapInitializationEvent");
        document.AddFunction(id, null, 0, 0, "action", "DoNothing");
        document.Delete(id);

        var other = document.Add("trigger", category);
        document.PasteFunction(other, null, 0, 0);

        Assert.Equal("TriggerSleepAction", Assert.Single(document.Trigger(other, new Names()).Actions).Name);
    }

    [Fact]
    public void A_trigger_tree_item_gives_the_name_a_parameter_holds()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var id = document.Add("trigger", Initialization(document));
        document.Rename(id, "Wave-1 Start");

        Assert.Equal("gg_trg_Wave_1_Start", document.Tree().Single(item => item.Id == id).Ref);
    }

    [Fact]
    public void Names_are_checked()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var category = Initialization(document);
        var id = document.Add("trigger", category);

        Assert.Throws<TriggerEditException>(() => document.Rename(id, "Melee Initialization"));
        Assert.Throws<TriggerEditException>(() => document.Rename(id, "   "));
        Assert.Throws<TriggerEditException>(() => document.AddVariable(new VariableInput("1st", "real", false, 1, ""), category));
        Assert.Throws<TriggerEditException>(() => document.AddVariable(new VariableInput("Ends_", "real", false, 1, ""), category));
    }

    [Fact]
    public void A_category_moves_with_what_is_in_it_and_not_into_itself()
    {
        var document = LostTemple();
        if (document == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var root = document.Tree()[0].Id;
        var outer = document.Add("category", root);
        var inner = document.Add("category", outer);
        var trigger = document.Add("trigger", inner);

        Assert.Throws<TriggerEditException>(() => document.Move(outer, inner, 0));
        document.Move(inner, root, 0);

        var tree = document.Tree();
        Assert.Equal(root, tree.Single(item => item.Id == inner).Parent);
        Assert.Equal(inner, tree.Single(item => item.Id == trigger).Parent);
        Assert.True(tree.ToList().FindIndex(item => item.Id == inner) < tree.ToList().FindIndex(item => item.Kind == "category" && item.Name == "Initialization"));
    }

    [Fact]
    public void A_trigger_added_to_the_map_itself_goes_in_a_category()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(null, null, catalog);

        document.Add("trigger", document.Tree()[0].Id);
        document.AddVariable(new VariableInput("Count", "integer", false, 1, ""), 0);

        var tree = document.Tree();
        Assert.Equal(["root", "category", "trigger", "variable"], tree.Select(item => item.Kind));
        Assert.Equal(tree[1].Id, tree[2].Parent);
        Assert.Equal(tree[1].Id, tree[3].Parent);
    }

    [Fact]
    public void A_map_with_no_trigger_file_gets_one_with_a_root()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(null, null, catalog);

        var category = document.Add("category", -1);
        document.Add("trigger", category);

        Assert.Equal(["root", "category", "trigger"], document.Tree().Select(item => item.Kind));
        Assert.Equal(1, Reread(document).TriggerItemCounts[TriggerItemType.Gui]);
        Assert.NotNull(document.CustomTextBytes());
    }
}
