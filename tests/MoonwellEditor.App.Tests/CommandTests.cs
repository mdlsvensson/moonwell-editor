using System.Text.Json;
using MoonwellEditor.App;
using Xunit.Abstractions;

namespace MoonwellEditor.App.Tests;

/// <summary>
/// The game data folder named by <c>MOONWELL_EDITOR_GAME_DATA</c>, loaded once for the test class. The cache lives
/// under the system temp folder and is kept between runs, so only the first run builds it.
/// </summary>
public sealed class GameDataFixture
{
    /// <summary>Loads the game data, or sets <see cref="Reason"/> when it cannot.</summary>
    public GameDataFixture()
    {
        var folder = Environment.GetEnvironmentVariable("MOONWELL_EDITOR_GAME_DATA");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Reason = "MOONWELL_EDITOR_GAME_DATA is not set to an existing game data folder (war3.w3mod); skipped";
            return;
        }

        Folder = folder;
        var cache = Path.Combine(Path.GetTempPath(), "MoonwellEditor.App.Tests", "gamedata");
        Data = MoonwellEditor.GameData.GameData.Load(folder, cache);
    }

    /// <summary>Why the tests cannot run, or null when <see cref="Data"/> is loaded.</summary>
    public string? Reason { get; }

    /// <summary>The game data folder, or null.</summary>
    public string? Folder { get; }

    /// <summary>The game data, or null when the tests cannot run.</summary>
    public MoonwellEditor.GameData.GameData? Data { get; }
}

public sealed class CommandTests(GameDataFixture fixture, ITestOutputHelper output)
    : IClassFixture<GameDataFixture>, IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "MoonwellEditor.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<SessionState> _events = [];
    private int _nextId = 1;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task Open_tree_object_set_undo_save()
    {
        if (Skipped(out var commands))
        {
            return;
        }

        var map = CopyFixture("we-3.0");
        var bridge = new Bridge(commands.Run);

        // No map yet; then the folder dialog (no path given) picks the map.
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "tree", new { kind = "unit" })));
        var opened = Result(await Ask(bridge, "openMap", new { }));
        Assert.Equal("map", opened.GetProperty("name").GetString());
        Assert.Equal(map, opened.GetProperty("path").GetString());
        Assert.False(opened.GetProperty("archive").GetBoolean());
        Assert.Equal(0, opened.GetProperty("problems").GetArrayLength());
        Assert.Equal(1, opened.GetProperty("counts").GetProperty("unit").GetInt32());
        Assert.Equal(1, opened.GetProperty("counts").GetProperty("upgrade").GetInt32());
        Assert.Equal(map, new Settings(Path.Combine(_root, "settings")).RecentMaps[0]);
        Assert.Equal(new SessionState(false, 0), _events[^1]);

        var tree = Result(await Ask(bridge, "tree", new { kind = "unit" }));
        var custom = tree.EnumerateArray().First();
        Assert.Equal("h000", custom.GetProperty("id").GetString());
        Assert.Equal("hpea", custom.GetProperty("baseId").GetString());
        Assert.Equal("Custom", custom.GetProperty("path")[0].GetString());

        var footman = Result(await Ask(bridge, "object", new { kind = "unit", id = "hfoo" }));
        Assert.Equal("unit", footman.GetProperty("kind").GetString());
        var life = Field(footman, "uhpm", 0).GetProperty("value").GetString();

        var set = Result(await SetLife(bridge, "500"));
        Assert.Equal("500", Field(set, "uhpm", 0).GetProperty("value").GetString());
        Assert.True(Field(set, "uhpm", 0).GetProperty("changed").GetBoolean());
        Assert.Equal(new SessionState(true, 1), _events[^1]);

        Assert.True(Result(await Ask(bridge, "undo", new { })).GetBoolean());
        Assert.Equal(new SessionState(false, 0), _events[^1]);
        var undone = Result(await Ask(bridge, "object", new { kind = "unit", id = "hfoo" }));
        Assert.Equal(life, Field(undone, "uhpm", 0).GetProperty("value").GetString());
        Assert.False(Result(await Ask(bridge, "undo", new { })).GetBoolean());

        Assert.True(Result(await Ask(bridge, "redo", new { })).GetBoolean());
        Assert.Equal(new SessionState(true, 1), _events[^1]);

        var report = Result(await Ask(bridge, "save", new { }));
        Assert.Equal("war3map.w3u", Assert.Single(report.GetProperty("written").EnumerateArray()).GetString());
        Assert.StartsWith(map + ".backup", report.GetProperty("backup").GetString());
        Assert.Equal(new SessionState(false, 0), _events[^1]);

        // A new object (at the next free id, which nextId gives without making anything), a copy, the copy deleted.
        var nextId = Result(await Ask(bridge, "nextId", new { kind = "unit", baseId = "hfoo" })).GetString();
        Assert.Equal("h001", nextId);
        Assert.False(commands.Dirty);
        var created = Result(await Ask(bridge, "createObject", new { kind = "unit", baseId = "hfoo" })).GetString();
        Assert.Equal(nextId, created);
        Assert.True(commands.Dirty);
        Assert.Equal("h002", Result(await Ask(bridge, "nextId", new { kind = "unit", baseId = "hfoo" })).GetString());
        var copy = new { kind = "unit", fromId = created, id = "h00X" };
        var copied = Result(await Ask(bridge, "copyObject", copy)).GetString();
        Assert.Equal("h00X", copied);
        Assert.Equal(new SessionState(true, 2), _events[^1]);
        Assert.True(Ok(await Ask(bridge, "deleteObject", new { kind = "unit", id = "h00X" })));
        Assert.Equal(new SessionState(true, 1), _events[^1]);

        // Reload drops the edits that were not saved (once the page says to), and keeps the saved one.
        Assert.Equal("unsaved", ErrorCode(await Ask(bridge, "reload", new { })));
        var reloaded = Result(await Ask(bridge, "reload", new { discard = true }));
        Assert.Equal(1, reloaded.GetProperty("counts").GetProperty("unit").GetInt32());
        Assert.Equal(new SessionState(false, 0), _events[^1]);
        var saved = Result(await Ask(bridge, "object", new { kind = "unit", id = "hfoo" }));
        Assert.Equal("500", Field(saved, "uhpm", 0).GetProperty("value").GetString());

        var users = Result(await Ask(bridge, "usedBy", new { kind = "unit", id = "hfoo" }));
        Assert.Contains(users.EnumerateArray(), user => user.GetProperty("id").GetString() == "hbar");

        var hits = Result(await Ask(bridge, "search", new { query = "hfoo", limit = 5 }));
        Assert.Equal("hfoo", hits[0].GetProperty("id").GetString());
        Assert.Equal("unit", hits[0].GetProperty("kind").GetString());

        var choices = Result(await Ask(bridge, "choices", new { }));
        Assert.True(choices.TryGetProperty("attackType", out var attackType));
        Assert.True(attackType.GetProperty("entries").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Edit_error_has_code()
    {
        if (Skipped(out var commands))
        {
            return;
        }

        // War3Net cannot read this cut-short war3map.w3a: abilities are read only (their skin file still shows them).
        var map = CopyFixture("moonwell-mymap");
        var abilities = Path.Combine(map, "war3map.w3a");
        File.WriteAllBytes(abilities, File.ReadAllBytes(abilities)[..5]);
        var bridge = new Bridge(commands.Run);
        var opened = Result(await Ask(bridge, "openMap", new { path = map }));
        Assert.NotEqual(0, opened.GetProperty("problems").GetArrayLength());
        var eventsBefore = _events.Count;

        var notNumber = await SetLife(bridge, "lots");
        Assert.Equal("notNumber", ErrorCode(notNumber));
        Assert.Contains("lots", notNumber.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("noObject", ErrorCode(await Ask(bridge, "object", new { kind = "unit", id = "zzzz" })));
        var noField = new { kind = "unit", id = "hfoo", field = "zzzz", level = 0, value = "1" };
        Assert.Equal("noField", ErrorCode(await Ask(bridge, "setField", noField)));
        Assert.Equal("noObject", ErrorCode(await Ask(bridge, "deleteObject", new { kind = "unit", id = "hfoo" })));
        var badId = new { kind = "unit", baseId = "hfoo", id = "bad id" };
        Assert.Equal("idInvalid", ErrorCode(await Ask(bridge, "createObject", badId)));
        Assert.Equal("noObject", ErrorCode(await Ask(bridge, "nextId", new { kind = "unit", baseId = "zzzz" })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "nextId", new { kind = "unit" })));
        var takenId = new { kind = "unit", baseId = "hfoo", id = "hfoo" };
        Assert.Equal("idTaken", ErrorCode(await Ask(bridge, "createObject", takenId)));

        // A read-only kind still shows its objects; editing them is refused.
        var ability = Result(await Ask(bridge, "tree", new { kind = "ability" })).EnumerateArray().First();
        var id = ability.GetProperty("id").GetString();
        var view = Result(await Ask(bridge, "object", new { kind = "ability", id }));
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("readOnly").ValueKind);
        var field = view.GetProperty("fields")[0];
        Assert.Equal("readOnly", ErrorCode(await Ask(bridge, "resetField", new
        {
            kind = "ability",
            id,
            field = field.GetProperty("id").GetString(),
            level = field.GetProperty("level").GetInt32(),
        })));

        // Wrong or missing arguments are the page's mistake.
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "tree", new { kind = "dragon" })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "object", new { kind = "unit" })));
        var textLevel = new { kind = "unit", id = "hfoo", field = "uhpm", level = "one", value = "1" };
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "setField", textLevel)));
        Assert.Equal(eventsBefore, _events.Count);   // nothing was edited

        // The map changed on disk since it was opened: save refuses.
        Assert.True(Ok(await SetLife(bridge, "500")));
        var units = Path.Combine(map, "war3map.w3u");
        File.SetLastWriteTimeUtc(units, DateTime.UtcNow.AddMinutes(1));   // newer than every file as read
        Assert.Equal("changedOnDisk", ErrorCode(await Ask(bridge, "save", new { })));
    }

    [Fact]
    public async Task Table_flow()
    {
        if (Skipped(out var commands))
        {
            return;
        }

        var map = CopyFixture("we-3.0");
        var bridge = new Bridge(commands.Run);
        Assert.True(Ok(await Ask(bridge, "openMap", new { path = map })));

        var fields = Result(await Ask(bridge, "fields", new { kind = "unit" }));
        var life = fields.EnumerateArray().Single(field => field.GetProperty("id").GetString() == "uhpm");
        Assert.False(string.IsNullOrEmpty(life.GetProperty("label").GetString()));
        Assert.Equal("int", life.GetProperty("type").GetString());
        Assert.False(life.GetProperty("perLevel").GetBoolean());
        Assert.Equal(JsonValueKind.Null, life.GetProperty("choice").ValueKind);
        Assert.Equal(JsonValueKind.Null, life.GetProperty("listOf").ValueKind);
        var order = fields.EnumerateArray()
            .Select(field => (field.GetProperty("category").GetString(), field.GetProperty("sort").GetString()))
            .ToList();
        var sorted = order
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .ThenBy(pair => pair.Item2, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(sorted, order);

        var columns = new[] { new { field = "uhpm", level = 0 }, new { field = "umvs", level = 0 } };
        var ids = new[] { "hfoo", "h000", "zzzz" };
        var rows = Result(await Ask(bridge, "table", new { kind = "unit", ids, columns }));
        Assert.Equal(2, rows.GetArrayLength());   // an unknown id is left out
        Assert.Equal("hfoo", rows[0].GetProperty("id").GetString());
        Assert.Equal("h000", rows[1].GetProperty("id").GetString());
        Assert.Equal("hpea", rows[1].GetProperty("baseId").GetString());
        Assert.Equal(2, rows[0].GetProperty("cells").GetArrayLength());
        var footmanLife = rows[0].GetProperty("cells")[0];
        Assert.True(footmanLife.GetProperty("applies").GetBoolean());
        Assert.False(footmanLife.GetProperty("changed").GetBoolean());
        var lifeBefore = footmanLife.GetProperty("value").GetString();
        var speedBefore = rows[0].GetProperty("cells")[1].GetProperty("value").GetString();
        var eventsBefore = _events.Count;

        var edits = new[]
        {
            new { id = "hfoo", field = "uhpm", level = 0, value = (string?)"500" },
            new { id = "h000", field = "umvs", level = 0, value = (string?)"250" },
        };
        var done = Result(await Ask(bridge, "setCells", new { kind = "unit", edits }));
        Assert.Equal(2, done.GetProperty("changed").GetInt32());
        Assert.False(done.TryGetProperty("view", out _));
        Assert.Equal(eventsBefore + 1, _events.Count);
        Assert.Equal(new SessionState(true, 2), _events[^1]);

        var after = Result(await Ask(bridge, "table", new { kind = "unit", ids, columns }));
        Assert.Equal("500", after[0].GetProperty("cells")[0].GetProperty("value").GetString());
        Assert.True(after[0].GetProperty("cells")[0].GetProperty("changed").GetBoolean());
        Assert.Equal("250", after[1].GetProperty("cells")[1].GetProperty("value").GetString());

        // The same values again change nothing, so nothing is sent.
        var again = Result(await Ask(bridge, "setCells", new { kind = "unit", edits }));
        Assert.Equal(0, again.GetProperty("changed").GetInt32());
        Assert.Equal(eventsBefore + 1, _events.Count);

        // One undo takes both cells back.
        Assert.True(Result(await Ask(bridge, "undo", new { })).GetBoolean());
        Assert.Equal(new SessionState(false, 0), _events[^1]);
        var undone = Result(await Ask(bridge, "table", new { kind = "unit", ids, columns }));
        Assert.Equal(lifeBefore, undone[0].GetProperty("cells")[0].GetProperty("value").GetString());
        Assert.Equal(speedBefore, undone[0].GetProperty("cells")[1].GetProperty("value").GetString());
        Assert.False(undone[1].GetProperty("cells")[1].GetProperty("changed").GetBoolean());

        // A null value resets the cell.
        var set = new[] { new { id = "hfoo", field = "uhpm", level = 0, value = (string?)"500" } };
        var reset = new[] { new { id = "hfoo", field = "uhpm", level = 0, value = (string?)null } };
        var setDone = Result(await Ask(bridge, "setCells", new { kind = "unit", edits = set }));
        Assert.Equal(1, setDone.GetProperty("changed").GetInt32());
        var resetDone = Result(await Ask(bridge, "setCells", new { kind = "unit", edits = reset }));
        Assert.Equal(1, resetDone.GetProperty("changed").GetInt32());
        // Set and reset: nothing differs from the map on disk, so nothing is unsaved.
        Assert.Equal(new SessionState(false, 0), _events[^1]);
    }

    [Fact]
    public async Task A_bad_cell_gives_its_code_and_changes_nothing()
    {
        if (Skipped(out var commands))
        {
            return;
        }

        var map = CopyFixture("we-3.0");
        var bridge = new Bridge(commands.Run);
        var columns = new[] { new { field = "uhpm", level = 0 } };
        var hfoo = new[] { "hfoo" };
        var noEdits = Array.Empty<object>();
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "table", new { kind = "unit", ids = hfoo, columns })));
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = noEdits })));
        Assert.True(Ok(await Ask(bridge, "openMap", new { path = map })));
        var eventsBefore = _events.Count;

        var edits = new[]
        {
            new { id = "hfoo", field = "uhpm", level = 0, value = (string?)"500" },
            new { id = "h000", field = "uhpm", level = 0, value = (string?)"lots" },
        };
        var refused = await Ask(bridge, "setCells", new { kind = "unit", edits });
        Assert.Equal("notNumber", ErrorCode(refused));
        Assert.Contains("h000", refused.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(eventsBefore, _events.Count);
        Assert.False(commands.Dirty);
        var rows = Result(await Ask(bridge, "table", new { kind = "unit", ids = hfoo, columns }));
        Assert.False(rows[0].GetProperty("cells")[0].GetProperty("changed").GetBoolean());

        var noField = new[] { new { id = "hfoo", field = "zzzz", level = 0, value = (string?)"1" } };
        Assert.Equal("noField", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = noField })));
        var noObject = new[] { new { id = "zzzz", field = "uhpm", level = 0, value = (string?)"1" } };
        Assert.Equal("noObject", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = noObject })));
        var badBool = new[] { new { id = "hfoo", field = "ubdg", level = 0, value = (string?)"7" } };
        Assert.Equal("badValue", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = badBool })));
        var badChoice = new[] { new { id = "hfoo", field = "udty", level = 0, value = (string?)"Bogus" } };
        Assert.Equal("badValue", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = badChoice })));
        var noColumn = new[] { new { field = "zzzz", level = 0 } };
        Assert.Equal("noField", ErrorCode(await Ask(bridge, "table", new { kind = "unit", ids = hfoo, columns = noColumn })));

        // Wrong shapes are the page's mistake.
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit" })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = "all" })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = hfoo })));
        var textLevel = new[] { new { id = "hfoo", field = "uhpm", level = "one", value = (string?)"1" } };
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = textLevel })));
        var numberValue = new[] { new { id = "hfoo", field = "uhpm", level = 0, value = 5 } };
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "setCells", new { kind = "unit", edits = numberValue })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "table", new { kind = "unit", columns })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "table", new { kind = "unit", ids = new[] { 1 }, columns })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "table", new { kind = "unit", ids = hfoo })));
        var noLevel = new[] { new { field = "uhpm" } };
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "table", new { kind = "unit", ids = hfoo, columns = noLevel })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "fields", new { kind = "dragon" })));
        Assert.Equal(eventsBefore, _events.Count);
    }

    [Fact]
    public async Task A_map_with_unsaved_edits_is_dropped_only_with_discard()
    {
        var asked = 0;
        if (Skipped(out var commands, () =>
            {
                asked++;
                return Path.Combine(_root, "map");
            }))
        {
            return;
        }

        var map = CopyFixture("we-3.0");
        var bridge = new Bridge(commands.Run);
        Assert.True(Ok(await Ask(bridge, "openMap", new { path = map })));
        Assert.True(Ok(await SetLife(bridge, "500")));

        // A page that was reloaded does not know the edits: opening or reloading is refused, the edits stay.
        Assert.Equal("unsaved", ErrorCode(await Ask(bridge, "openMap", new { path = map })));
        Assert.Equal("unsaved", ErrorCode(await Ask(bridge, "openMap", new { })));
        Assert.Equal(0, asked);   // no folder dialog for a map that could not be opened
        Assert.Equal("unsaved", ErrorCode(await Ask(bridge, "openMap", new { path = map, discard = false })));
        Assert.Equal("unsaved", ErrorCode(await Ask(bridge, "reload", new { })));
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "reload", new { discard = "yes" })));
        Assert.True(commands.Dirty);
        var kept = Result(await Ask(bridge, "object", new { kind = "unit", id = "hfoo" }));
        Assert.Equal("500", Field(kept, "uhpm", 0).GetProperty("value").GetString());

        // The user chose Discard in the page's dialog.
        Assert.True(Ok(await Ask(bridge, "reload", new { discard = true })));
        Assert.False(commands.Dirty);
        Assert.True(Ok(await SetLife(bridge, "600")));
        Assert.True(Ok(await Ask(bridge, "openMap", new { path = map, discard = true })));
        Assert.False(commands.Dirty);

        // With nothing unsaved, no discard is needed.
        Assert.True(Ok(await Ask(bridge, "reload", new { })));
        Assert.True(Ok(await Ask(bridge, "openMap", new { })));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Current_map_gives_the_open_map_and_where_it_stands()
    {
        if (Skipped(out var commands))
        {
            return;
        }

        var map = CopyFixture("we-3.0");
        var bridge = new Bridge(commands.Run);
        Assert.Equal(JsonValueKind.Null, Result(await Ask(bridge, "currentMap", new { })).ValueKind);

        Assert.True(Ok(await Ask(bridge, "openMap", new { path = map })));
        var events = _events.Count;
        var current = Result(await Ask(bridge, "currentMap", new { }));
        Assert.Equal("map", current.GetProperty("map").GetProperty("name").GetString());
        Assert.Equal(map, current.GetProperty("map").GetProperty("path").GetString());
        Assert.Equal(1, current.GetProperty("map").GetProperty("counts").GetProperty("unit").GetInt32());
        Assert.False(current.GetProperty("session").GetProperty("dirty").GetBoolean());

        // A page loaded again sees the edits that are not saved.
        Assert.True(Ok(await SetLife(bridge, "500")));
        current = Result(await Ask(bridge, "currentMap", new { }));
        Assert.True(current.GetProperty("session").GetProperty("dirty").GetBoolean());
        Assert.Equal(1, current.GetProperty("session").GetProperty("changedObjects").GetInt32());
        Assert.Equal(events + 1, _events.Count);   // asking changes nothing, so no sessionChanged
    }

    [Fact]
    public async Task Map_commands_need_the_game_data_and_a_map()
    {
        var settings = new Settings(Path.Combine(_root, "settings"));
        var commands = new Commands(
            settings, _ => throw new InvalidOperationException("no dialog"), _ => throw new InvalidOperationException());
        var bridge = new Bridge(commands.Run);

        Assert.Equal("gameDataMissing", ErrorCode(await Ask(bridge, "openMap", new { path = _root })));
        Assert.Equal("gameDataMissing", ErrorCode(await Ask(bridge, "choices", new { })));
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "save", new { })));
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "undo", new { })));
    }

    [Fact]
    public async Task Cancelling_the_map_dialog_opens_nothing()
    {
        if (Skipped(out var commands, () => null))
        {
            return;
        }

        var bridge = new Bridge(commands.Run);

        var reply = await Ask(bridge, "openMap", new { });

        Assert.Equal(JsonValueKind.Null, Result(reply).ValueKind);
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "tree", new { kind = "unit" })));
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Open_edit_and_save_an_archive()
    {
        var archive = Path.Combine(_root, "maps", "Sample.w3x");
        string? asked = null;
        if (Skipped(out var commands, askArchive: text =>
            {
                asked = text;
                return archive;
            }))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "moonwell-sample", "Sample.w3x"), archive);
        var bridge = new Bridge(commands.Run);

        // A path to a file opens it as an archive; the header shows its file name.
        var opened = Result(await Ask(bridge, "openMap", new { path = archive }));
        Assert.Equal("Sample.w3x", opened.GetProperty("name").GetString());
        Assert.Equal(archive, opened.GetProperty("path").GetString());
        Assert.True(opened.GetProperty("archive").GetBoolean());
        Assert.Equal(0, opened.GetProperty("problems").GetArrayLength());
        Assert.Equal(archive, new Settings(Path.Combine(_root, "settings")).RecentMaps[0]);
        Assert.Null(asked);

        Assert.True(Ok(await SetLife(bridge, "500")));
        var report = Result(await Ask(bridge, "save", new { }));
        Assert.Equal("war3map.w3u", Assert.Single(report.GetProperty("written").EnumerateArray()).GetString());
        Assert.StartsWith(archive + ".backup", report.GetProperty("backup").GetString());
        Assert.Equal("", report.GetProperty("note").GetString());
        var current = Result(await Ask(bridge, "currentMap", new { }));
        Assert.True(current.GetProperty("map").GetProperty("archive").GetBoolean());

        // A save that fails says why, and the edits stay unsaved.
        Assert.True(Ok(await SetLife(bridge, "600")));
        using (new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var refused = await Ask(bridge, "save", new { });
            Assert.Equal("mapFile", ErrorCode(refused));
            Assert.Contains("World Editor", refused.GetProperty("error").GetProperty("message").GetString());
        }

        Assert.True(commands.Dirty);
        Assert.True(Ok(await Ask(bridge, "save", new { })));

        // With no path, archive asks for a file in the Open File dialog, else for a folder.
        var picked = Result(await Ask(bridge, "openMap", new { archive = true }));
        Assert.NotNull(asked);
        Assert.Equal(archive, picked.GetProperty("path").GetString());
        var folder = CopyFixture("we-3.0");
        var fromFolderDialog = Result(await Ask(bridge, "openMap", new { archive = false }));
        Assert.Equal(folder, fromFolderDialog.GetProperty("path").GetString());
        Assert.False(fromFolderDialog.GetProperty("archive").GetBoolean());
        Assert.Equal("badRequest", ErrorCode(await Ask(bridge, "openMap", new { archive = "yes" })));
    }

    [Fact]
    public async Task Unreadable_archive_fails_with_the_reason()
    {
        if (Skipped(out var commands))
        {
            return;
        }

        Directory.CreateDirectory(_root);
        var noise = Path.Combine(_root, "noise.w3x");
        var random = new byte[1000];
        new Random(1).NextBytes(random);
        File.WriteAllBytes(noise, random);
        var bridge = new Bridge(commands.Run);

        var reply = await Ask(bridge, "openMap", new { path = noise });

        Assert.Equal("failed", ErrorCode(reply));
        var message = reply.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains(noise, message);
        Assert.Contains("cannot be opened", message);
        Assert.Equal("noMap", ErrorCode(await Ask(bridge, "tree", new { kind = "unit" })));

        var missing = await Ask(bridge, "openMap", new { path = Path.Combine(_root, "missing.w3x") });
        Assert.Equal("failed", ErrorCode(missing));
        Assert.Contains("missing.w3x", missing.GetProperty("error").GetProperty("message").GetString());
    }

    private bool Skipped(out Commands commands, Func<string?>? ask = null, Func<string, string?>? askArchive = null)
    {
        if (fixture.Data == null)
        {
            output.WriteLine(fixture.Reason);
            commands = null!;
            return true;
        }

        var settings = new Settings(Path.Combine(_root, "settings")) { GameDataFolder = fixture.Folder };
        var data = fixture.Data;
        commands = new Commands(
            settings,
            _ => ask == null ? Path.Combine(_root, "map") : ask(),
            _ => data,
            askArchive: askArchive);
        commands.SessionChanged += state => _events.Add(state);
        commands.StartLoading().Wait();
        return false;
    }

    /// <summary>Copies a fixture map into <c>map</c> under the test's temp folder, and gives its full path.</summary>
    private string CopyFixture(string fixtureName)
    {
        var folder = Path.Combine(_root, "map");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", fixtureName)))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }

        return Path.GetFullPath(folder);
    }

    private async Task<JsonElement> Ask(Bridge bridge, string command, object args)
    {
        var request = JsonSerializer.Serialize(new { id = _nextId++, command, args });
        return JsonDocument.Parse(await bridge.Handle(request)).RootElement;
    }

    // Sets the footman's hit points.
    private Task<JsonElement> SetLife(Bridge bridge, string value) =>
        Ask(bridge, "setField", new { kind = "unit", id = "hfoo", field = "uhpm", level = 0, value });

    private static bool Ok(JsonElement reply) => reply.GetProperty("ok").GetBoolean();

    private static JsonElement Result(JsonElement reply)
    {
        Assert.True(Ok(reply), reply.ToString());
        return reply.GetProperty("result");
    }

    private static string? ErrorCode(JsonElement reply)
    {
        Assert.False(Ok(reply), reply.ToString());
        return reply.GetProperty("error").GetProperty("code").GetString();
    }

    private static JsonElement Field(JsonElement view, string id, int level) =>
        view.GetProperty("fields").EnumerateArray()
            .Single(field =>
                field.GetProperty("id").GetString() == id && field.GetProperty("level").GetInt32() == level);
}
