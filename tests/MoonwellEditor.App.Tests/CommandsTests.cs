using System.Text.Json;
using MoonwellEditor.App;

namespace MoonwellEditor.App.Tests;

public sealed class CommandsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MoonwellEditor.App.Tests", Guid.NewGuid().ToString("N"));

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
        Func<string, MoonwellEditor.GameData.GameData> load,
        Func<string?>? ask = null,
        string? savedFolder = null)
    {
        var settings = new Settings(_folder) { GameFolder = savedFolder };
        return new Commands(settings, _ => ask == null ? null : ask(), load);
    }

    [Fact]
    public async Task Without_a_chosen_folder_the_install_found_is_loaded()
    {
        string? loaded = null;
        var commands = new Commands(
            new Settings(_folder),
            _ => null,
            folder =>
            {
                loaded = folder;
                throw new IOException("stop here");
            },
            findGame: () => @"C:\Games\Warcraft III");

        await commands.StartLoading();

        Assert.Equal(@"C:\Games\Warcraft III", loaded);
        Assert.Null(new Settings(_folder).GameFolder);
    }

    [Fact]
    public async Task A_chosen_folder_comes_before_the_install_found()
    {
        string? loaded = null;
        var commands = new Commands(
            new Settings(_folder) { GameFolder = @"C:\Chosen" },
            _ => null,
            folder =>
            {
                loaded = folder;
                throw new IOException("stop here");
            },
            findGame: () => @"C:\Games\Warcraft III");

        await commands.StartLoading();

        Assert.Equal(@"C:\Chosen", loaded);
    }

    [Fact]
    public async Task Without_a_chosen_folder_or_an_install_the_page_asks_for_one()
    {
        var commands = new Commands(
            new Settings(_folder),
            _ => null,
            _ => throw new InvalidOperationException("must not load"),
            findGame: () => null);

        await commands.StartLoading();
        var state = await Ask(commands, "gameData");

        Assert.Equal(JsonValueKind.Null, state.GetProperty("result").GetProperty("source").ValueKind);
        Assert.Equal("gameDataMissing", Assert.Throws<CommandException>(() => commands.NeedGameData()).Code);
    }

    [Fact]
    public async Task Choosing_the_install_loads_it_with_its_game_version()
    {
        var install = Moonwell.GameData.GameInstall.Find();
        if (install == null)
        {
            Console.WriteLine("No Warcraft III install found; skipped");
            return;
        }

        var cache = Path.Combine(_folder, "cache");
        var commands = Make(path => MoonwellEditor.GameData.GameData.Load(path, cache), () => install);

        await Ask(commands, "chooseGameData");
        await commands.Loading;
        var after = (await Ask(commands, "gameData")).GetProperty("result");

        Assert.True(after.GetProperty("loaded").GetBoolean());
        Assert.Equal(Moonwell.GameData.GameInstall.Build(install).Version, after.GetProperty("game").GetString());
        Assert.Equal(install, new Settings(_folder).GameFolder);
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
    public async Task The_startup_map_is_given_once_then_cleared()
    {
        var settings = new Settings(_folder);
        var commands = new Commands(
            settings,
            _ => null,
            _ => throw new InvalidOperationException("must not load"),
            @"C:\Maps\Arena");

        var first = await Ask(commands, "startup");
        var second = await Ask(commands, "startup");

        Assert.Equal(@"C:\Maps\Arena", first.GetProperty("result").GetProperty("map").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("result").GetProperty("map").ValueKind);
    }

    [Fact]
    public async Task With_no_startup_map_startup_gives_none()
    {
        var commands = Make(_ => throw new InvalidOperationException("must not load"));

        var reply = await Ask(commands, "startup");

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").GetProperty("map").ValueKind);
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
        new Settings(_folder) { GameFolder = @"C:\old" }.Save();

        await Ask(commands, "chooseGameData");
        await commands.Loading;

        Assert.Equal(@"C:\old", new Settings(_folder).GameFolder);
    }

    [Fact]
    public async Task Cancelling_the_dialog_changes_nothing()
    {
        var commands = Make(_ => throw new InvalidOperationException("must not load"), () => null);

        var reply = await Ask(commands, "chooseGameData");

        Assert.True(reply.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").GetProperty("source").ValueKind);
        Assert.Null(new Settings(_folder).GameFolder);
    }

    [Fact]
    public async Task A_newer_state_has_a_higher_version()
    {
        var release = new ManualResetEventSlim();
        var commands = Make(_ =>
        {
            release.Wait();
            throw new InvalidOperationException("stop here");
        }, savedFolder: _folder);
        GameDataState? fromEvent = null;
        commands.GameDataChanged += state => fromEvent = state;

        var loading = commands.StartLoading();
        var whileLoading = (await Ask(commands, "gameData")).GetProperty("result").GetProperty("version").GetInt32();
        release.Set();
        await loading;
        var after = (await Ask(commands, "gameData")).GetProperty("result").GetProperty("version").GetInt32();

        Assert.NotNull(fromEvent);
        Assert.True(fromEvent.Version > whileLoading);
        Assert.Equal(fromEvent.Version, after);
    }

    [Fact]
    public async Task Ping_still_works()
    {
        var commands = Make(_ => throw new InvalidOperationException());

        var reply = await Ask(commands, "ping");

        Assert.Equal("pong", reply.GetProperty("result").GetString());
    }

    [Fact]
    public async Task The_page_answers_a_close_through_two_commands()
    {
        var commands = Make(_ => throw new InvalidOperationException());
        var heard = new List<string>();
        commands.CloseAsking += () => heard.Add("asking");
        commands.CloseApproved += () => heard.Add("approved");

        var asking = await Ask(commands, "closeAsking");
        var approved = await Ask(commands, "closeWindow");

        Assert.True(asking.GetProperty("ok").GetBoolean());
        Assert.True(approved.GetProperty("ok").GetBoolean());
        Assert.Equal(["asking", "approved"], heard);
    }

    [Fact]
    public async Task The_title_bar_drives_the_window_through_commands()
    {
        var commands = Make(_ => throw new InvalidOperationException());
        var heard = new List<string>();
        commands.MinimizeAsked += () => heard.Add("minimize");
        commands.MaximizeAsked += () => heard.Add("maximize");
        commands.CloseClicked += () => heard.Add("close");
        commands.FrameColorsChanged += (surface, border) => heard.Add($"{surface} {border}");
        commands.UiScaleChanged += percent => heard.Add($"{percent}%");
        commands.Maximized = () => true;
        var bridge = new Bridge(commands.Run);

        await Ask(commands, "minimizeWindow");
        await Ask(commands, "maximizeWindow");
        await Ask(commands, "closeClicked");
        var state = await Ask(commands, "windowState");
        var colors = Parse(await bridge.Handle(
            "{\"id\":1,\"command\":\"frameColors\",\"args\":{\"surface\":\"#1f1f28\",\"border\":\"#363646\"}}"));
        var scaled = Parse(await bridge.Handle("{\"id\":1,\"command\":\"uiScale\",\"args\":{\"percent\":120}}"));
        var badScale = Parse(await bridge.Handle("{\"id\":1,\"command\":\"uiScale\",\"args\":{\"percent\":133}}"));
        var bad = Parse(await bridge.Handle(
            "{\"id\":1,\"command\":\"frameColors\",\"args\":{\"surface\":\"red\",\"border\":\"#363646\"}}"));

        Assert.Equal(["minimize", "maximize", "close", "#1f1f28 #363646", "120%"], heard);
        Assert.True(scaled.GetProperty("ok").GetBoolean());
        Assert.False(badScale.GetProperty("ok").GetBoolean());
        Assert.True(state.GetProperty("result").GetProperty("maximized").GetBoolean());
        Assert.True(colors.GetProperty("ok").GetBoolean());
        Assert.False(bad.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void With_no_map_nothing_is_unsaved()
    {
        var commands = Make(_ => throw new InvalidOperationException());

        Assert.False(commands.Dirty);
    }
}
