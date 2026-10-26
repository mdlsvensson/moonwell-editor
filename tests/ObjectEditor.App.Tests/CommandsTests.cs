using System.Text.Json;
using ObjectEditor.App;

namespace ObjectEditor.App.Tests;

public sealed class CommandsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ObjectEditor.App.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static async Task<JsonElement> Ask(Commands commands, string command)
    {
        var bridge = new Bridge(commands.Run);
        return Parse(await bridge.Handle("{\"id\":1,\"command\":\"" + command + "\",\"args\":{}}"));
    }

    private Commands Make(
        Func<string, ObjectEditor.GameData.GameData> load,
        Func<string?>? ask = null,
        string? savedFolder = null)
    {
        var settings = new Settings(_folder) { GameDataFolder = savedFolder };
        return new Commands(settings, ask ?? (() => null), load);
    }

    [Fact]
    public async Task Command_exception_keeps_its_code()
    {
        var bridge = new Bridge((command, args) => throw new CommandException("gameDataMissing", "no folder"));

        var reply = Parse(await bridge.Handle("""{"id":1,"command":"x","args":{}}"""));

        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("gameDataMissing", reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("no folder", reply.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Game_data_with_no_setting_is_missing()
    {
        var commands = Make(_ => throw new InvalidOperationException("must not load"));
        await commands.StartLoading();

        var reply = await Ask(commands, "gameData");

        var result = reply.GetProperty("result");
        Assert.Equal(JsonValueKind.Null, result.GetProperty("source").ValueKind);
        Assert.False(result.GetProperty("loaded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        Assert.Equal("gameDataMissing", Assert.Throws<CommandException>(() => commands.NeedGameData()).Code);
    }

    [Fact]
    public async Task Game_data_is_loading_until_the_background_load_ends()
    {
        var release = new ManualResetEventSlim();
        var commands = Make(_ =>
        {
            release.Wait();
            throw new InvalidOperationException("stop here");
        }, savedFolder: _folder);

        var loading = commands.StartLoading();
        var reply = await Ask(commands, "gameData");

        var result = reply.GetProperty("result");
        Assert.Equal(_folder, result.GetProperty("source").GetString());
        Assert.False(result.GetProperty("loaded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        Assert.Equal("gameDataLoading", Assert.Throws<CommandException>(() => commands.NeedGameData()).Code);

        release.Set();
        await loading;
    }

    [Fact]
    public async Task A_failed_load_is_reported_in_the_state()
    {
        var commands = Make(_ => throw new DirectoryNotFoundException("no such folder"), savedFolder: _folder);

        await commands.StartLoading();
        var reply = await Ask(commands, "gameData");

        var result = reply.GetProperty("result");
        Assert.False(result.GetProperty("loaded").GetBoolean());
        Assert.Equal("no such folder", result.GetProperty("error").GetString());
        Assert.Equal("gameDataFailed", Assert.Throws<CommandException>(() => commands.NeedGameData()).Code);
    }

    [Fact]
    public async Task Choosing_a_folder_that_fails_keeps_the_old_setting()
    {
        var commands = Make(_ => throw new DirectoryNotFoundException("bad"), () => @"C:\bad", savedFolder: @"C:\old");
        new Settings(_folder) { GameDataFolder = @"C:\old" }.Save();

        await Ask(commands, "chooseGameData");
        await commands.Loading;

        Assert.Equal(@"C:\old", new Settings(_folder).GameDataFolder);
    }

    [Fact]
    public async Task Cancelling_the_dialog_changes_nothing()
    {
        var commands = Make(_ => throw new InvalidOperationException("must not load"), () => null);

        var reply = await Ask(commands, "chooseGameData");

        Assert.True(reply.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").GetProperty("source").ValueKind);
        Assert.Null(new Settings(_folder).GameDataFolder);
    }

    [Fact]
    public async Task Choosing_a_folder_loads_it_and_saves_the_setting()
    {
        var folder = Environment.GetEnvironmentVariable("OBJECT_EDITOR_GAME_DATA");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Console.WriteLine("OBJECT_EDITOR_GAME_DATA is not set to an existing game data folder; skipped");
            return;
        }

        var cache = Path.Combine(_folder, "cache");
        var commands = Make(path => ObjectEditor.GameData.GameData.Load(path, cache), () => folder);

        var reply = await Ask(commands, "chooseGameData");
        await commands.Loading;
        var after = await Ask(commands, "gameData");

        Assert.Equal(folder, reply.GetProperty("result").GetProperty("source").GetString());
        Assert.True(after.GetProperty("result").GetProperty("loaded").GetBoolean());
        Assert.NotNull(commands.NeedGameData());
        Assert.Equal(folder, new Settings(_folder).GameDataFolder);
    }

    [Fact]
    public async Task Ping_still_works()
    {
        var commands = Make(_ => throw new InvalidOperationException());

        var reply = await Ask(commands, "ping");

        Assert.Equal("pong", reply.GetProperty("result").GetString());
    }
}
