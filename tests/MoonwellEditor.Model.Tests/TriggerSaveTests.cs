using System.Text;
using Moonwell.GameData;
using MoonwellEditor.Files;
using MoonwellEditor.Triggers;
using War3Net.Build.Extensions;
using War3Net.Build.Import;
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

    private static List<string> ImportedPaths(byte[]? imports)
    {
        if (imports == null)
        {
            return [];
        }

        using var reader = new BinaryReader(new MemoryStream(imports));
        return [.. reader.ReadImportedFiles().Files.Select(file => file.FullPath)];
    }

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
    public void A_moved_card_saves_its_place_in_the_map_and_lists_the_file_in_the_imports()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var before = MapStore.Open(path);
        var imported = ImportedPaths(before.Read(ImportedFiles.MapFileName));
        var session = Session.Open(before, game.Data!);
        var id = session.Triggers(catalog).Tree().First(item => item.Kind == "trigger").Id;

        session.EditTriggers(catalog, each =>
        {
            each.SetPlaces([new CardMove(id, 120, 60)]);
            return 0;
        });

        Assert.True(session.Dirty);
        var report = session.Save();
        Assert.Equal([TriggerDocument.LayoutFile, ImportedFiles.MapFileName], report.Written);
        Assert.False(session.Dirty);
        var after = MapStore.Open(path);
        Assert.NotNull(after.Read(TriggerDocument.LayoutFile));
        Assert.Equal([.. imported, TriggerDocument.LayoutFile], ImportedPaths(after.Read(ImportedFiles.MapFileName)));

        var again = Session.Open(after, game.Data!);
        var card = again.Triggers(catalog).Cards(again.Triggers(catalog).Tree().First(item => item.Id == id).Parent, again).Single(each => each.Id == id);
        Assert.Equal(new CardPlace(120, 60, false), card.Place);
    }

    [Fact]
    public void A_card_moved_again_writes_only_the_layout_file()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var session = Session.Open(MapStore.Open(path), game.Data!);
        var id = session.Triggers(catalog).Tree().First(item => item.Kind == "trigger").Id;
        session.EditTriggers(catalog, each =>
        {
            each.SetPlaces([new CardMove(id, 120, 60)]);
            return 0;
        });
        session.Save();

        session.EditTriggers(catalog, each =>
        {
            each.SetPlaces([new CardMove(id, 200, 80)]);
            return 0;
        });

        Assert.Equal([TriggerDocument.LayoutFile], session.Save().Written);
    }

    [Fact]
    public void A_later_session_lists_the_layout_file_only_once()
    {
        if (Copy(WarChasers) is not var (path, catalog))
        {
            return;
        }

        var first = Session.Open(MapStore.Open(path), game.Data!);
        var id = first.Triggers(catalog).Tree().First(item => item.Kind == "trigger").Id;
        first.EditTriggers(catalog, each =>
        {
            each.SetPlaces([new CardMove(id, 120, 60)]);
            return 0;
        });
        first.Save();

        var second = Session.Open(MapStore.Open(path), game.Data!);
        second.EditTriggers(catalog, each =>
        {
            each.SetPlaces([new CardMove(id, 200, 80)]);
            return 0;
        });

        Assert.Equal([TriggerDocument.LayoutFile], second.Save().Written);
        var paths = ImportedPaths(MapStore.Open(path).Read(ImportedFiles.MapFileName));
        Assert.Single(paths, each => each == TriggerDocument.LayoutFile);
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
