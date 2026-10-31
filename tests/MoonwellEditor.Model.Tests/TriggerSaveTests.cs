using System.Text;
using Moonwell.GameData;
using MoonwellEditor.Files;
using MoonwellEditor.Triggers;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class TriggerSaveTests(GameDataFixture game, ITestOutputHelper output) : IDisposable
{
    private const string WarChasers = "Maps/Scenario/(4)WarChasers.w3m";
    private const string ForgottenHollow = "Maps/ForsakenKingdom/Scenario/(1)ForgottenHollow.w3x";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MoonwellEditor.TriggerSaveTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    // A copy of a map of the install, with the install's trigger catalog; null without an install.
    private (string Path, TriggerCatalog Catalog)? Copy(string path)
    {
        if (game.Data == null || GameInstall.Find() is not { } folder)
        {
            output.WriteLine(game.Reason);
            return null;
        }

        using var files = GameFiles.Open(folder);
        Directory.CreateDirectory(_folder);
        var target = Path.Combine(_folder, Path.GetFileName(path));
        File.WriteAllBytes(target, files.Read(path, "sd")!);
        return (target, TriggerCatalog.Read(name => files.Read(name, "sd")));
    }

    private static int Category(TriggerDocument document) =>
        document.Tree().First(item => item.Kind == "category").Id;

    [Fact]
    public void An_edit_is_unsaved_until_it_is_undone()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var session = Session.Open(MapStore.Open(path), game.Data!);
        var document = session.Triggers(catalog);

        session.EditTriggers(catalog, each => each.Add("trigger", Category(each)));

        Assert.True(session.Dirty);
        Assert.True(session.Undo());
        Assert.False(session.Dirty);
        Assert.Single(document.Tree(), item => item.Kind == "root");
        Assert.DoesNotContain(document.Tree(), item => item.Name == "Untitled Trigger 001");
        Assert.True(session.Redo());
        Assert.Contains(document.Tree(), item => item.Name == "Untitled Trigger 001");
    }

    [Fact]
    public void A_refused_edit_changes_nothing_and_is_no_undo_step()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var session = Session.Open(MapStore.Open(path), game.Data!);
        var before = session.Triggers(catalog).TriggerBytes();

        Assert.Throws<TriggerEditException>(() => session.EditTriggers(catalog, each =>
        {
            var id = each.Add("trigger", Category(each));
            each.Rename(id, "   ");
            return id;
        }));

        Assert.Equal(before, session.Triggers(catalog).TriggerBytes());
        Assert.False(session.Dirty);
        Assert.False(session.Undo());
    }

    [Fact]
    public void A_save_writes_the_trigger_files_and_the_script_and_nothing_else()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var before = MapStore.Open(path);
        var names = new[] { "war3map.w3u", "war3map.doo", "war3mapUnits.doo", "war3map.w3e", "war3map.w3i", "war3map.wts" };
        var kept = names.ToDictionary(name => name, before.Read);
        var session = Session.Open(before, game.Data!);
        var id = session.EditTriggers(catalog, each =>
        {
            var made = each.Add("trigger", Category(each));
            each.AddFunction(made, null, 0, 0, "event", "TriggerRegisterTimerEventPeriodic");
            each.AddFunction(made, null, 0, 0, "action", "CreateNUnitsAtLoc");
            return made;
        });

        var report = session.Save();

        Assert.Equal(["war3map.wtg", "war3map.wct", "war3map.j"], report.Written);
        Assert.Equal("", report.Note);
        Assert.False(session.Dirty);
        var after = MapStore.Open(path);
        foreach (var name in names)
        {
            Assert.Equal(kept[name], after.Read(name));
        }

        var script = Encoding.UTF8.GetString(after.Read("war3map.j")!);
        Assert.Contains("gg_trg_Untitled_Trigger_001", script);
        Assert.Contains("call TriggerRegisterTimerEventPeriodic( gg_trg_Untitled_Trigger_001, 2 )", script);
        Assert.Contains("call CreateNUnitsAtLoc( 1, 'hfoo', Player(0), GetRectCenter(GetPlayableMapRect()), bj_UNIT_FACING )", script);
        var again = TriggerDocument.Read(after.Read("war3map.wtg"), after.Read("war3map.wct"), catalog);
        Assert.Null(again.Problem);
        Assert.Contains(again.Tree(), item => item.Id == id);
    }

    [Fact]
    public void A_map_whose_triggers_were_not_edited_saves_no_trigger_file()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var session = Session.Open(MapStore.Open(path), game.Data!);
        session.Triggers(catalog);

        Assert.Empty(session.Save().Written);
    }

    [Fact]
    public void A_lua_map_keeps_its_script_and_the_save_says_so()
    {
        if (Copy(ForgottenHollow) is not var (path, catalog))
        {
            return;
        }

        var session = Session.Open(MapStore.Open(path), game.Data!);
        session.EditTriggers(catalog, each => each.Add("trigger", Category(each)));

        var report = session.Save();

        Assert.Equal(["war3map.wtg", "war3map.wct"], report.Written);
        Assert.Contains("Lua", report.Note);
    }

    [Fact]
    public void A_reload_drops_the_edits()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var session = Session.Open(MapStore.Open(path), game.Data!);
        session.EditTriggers(catalog, each => each.Add("trigger", Category(each)));

        session.Reload();

        Assert.False(session.Dirty);
        Assert.DoesNotContain(session.Triggers(catalog).Tree(), item => item.Name == "Untitled Trigger 001");
    }
}
