using System.Text.Json;
using ObjectEditor.App;
using Xunit.Abstractions;

namespace ObjectEditor.App.Tests;

/// <summary>
/// The game data folder named by <c>OBJECT_EDITOR_GAME_DATA</c>, loaded once for the test class. The cache lives
/// under the system temp folder and is kept between runs, so only the first run builds it.
/// </summary>
public sealed class GameDataFixture
{
    /// <summary>Loads the game data, or sets <see cref="Reason"/> when it cannot.</summary>
    public GameDataFixture()
    {
        var folder = Environment.GetEnvironmentVariable("OBJECT_EDITOR_GAME_DATA");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Reason = "OBJECT_EDITOR_GAME_DATA is not set to an existing game data folder (war3.w3mod); skipped";
            return;
        }

        Folder = folder;
        var cache = Path.Combine(Path.GetTempPath(), "ObjectEditor.App.Tests", "gamedata");
        Data = ObjectEditor.GameData.GameData.Load(folder, cache);
    }

    /// <summary>Why the tests cannot run, or null when <see cref="Data"/> is loaded.</summary>
    public string? Reason { get; }

    /// <summary>The game data folder, or null.</summary>
    public string? Folder { get; }

    /// <summary>The game data, or null when the tests cannot run.</summary>
    public ObjectEditor.GameData.GameData? Data { get; }
}

public sealed class CommandTests(GameDataFixture fixture, ITestOutputHelper output)
    : IClassFixture<GameDataFixture>, IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ObjectEditor.App.Tests", Guid.NewGuid().ToString("N"));
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

        // Reload drops the edits that were not saved, and keeps the saved one.
        var reloaded = Result(await Ask(bridge, "reload", new { }));
        Assert.Equal(1, reloaded.GetProperty("counts").GetProperty("unit").GetInt32());
        Assert.Equal(new SessionState(false, 0), _events[^1]);
        var saved = Result(await Ask(bridge, "object", new { kind = "unit", id = "hfoo" }));
        Assert.Equal("500", Field(saved, "uhpm", 0).GetProperty("value").GetString());

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

        // War3Net reads this war3map.w3a but would drop its last 4 bytes on writing it back: abilities are read only.
        var map = CopyFixture("moonwell-mymap");
        var abilities = Path.Combine(map, "war3map.w3a");
        File.WriteAllBytes(abilities, [.. File.ReadAllBytes(abilities), 0, 0, 0, 0]);
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

    private bool Skipped(out Commands commands, Func<string?>? ask = null)
    {
        if (fixture.Data == null)
        {
            output.WriteLine(fixture.Reason);
            commands = null!;
            return true;
        }

        var settings = new Settings(Path.Combine(_root, "settings")) { GameDataFolder = fixture.Folder };
        var data = fixture.Data;
        commands = new Commands(settings, _ => ask == null ? Path.Combine(_root, "map") : ask(), _ => data);
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
