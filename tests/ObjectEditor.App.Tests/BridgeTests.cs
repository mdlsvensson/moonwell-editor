using System.Text.Json;
using ObjectEditor.App;

namespace ObjectEditor.App.Tests;

public sealed class BridgeTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // Commands that never touch the settings or the game data: only ping and the unknown-command error are used.
    private static Commands PingOnly() =>
        new(
            new Settings(Path.Combine(Path.GetTempPath(), "ObjectEditor.App.Tests", "bridge")),
            () => null,
            _ => throw new InvalidOperationException());

    [Fact]
    public async Task Ping_round_trips()
    {
        var bridge = new Bridge(PingOnly().Run);

        var reply = Parse(await bridge.Handle("""{"id":7,"command":"ping","args":{}}"""));

        Assert.Equal(7, reply.GetProperty("id").GetInt32());
        Assert.True(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("pong", reply.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Unknown_command_is_an_error()
    {
        var bridge = new Bridge(PingOnly().Run);

        var reply = Parse(await bridge.Handle("""{"id":1,"command":"nope","args":{}}"""));

        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("unknownCommand", reply.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Command_exception_becomes_error_reply()
    {
        var bridge = new Bridge((command, args) =>
            command == "boom"
                ? throw new InvalidOperationException("it broke")
                : Task.FromResult<object?>("fine"));

        var failed = Parse(await bridge.Handle("""{"id":1,"command":"boom","args":{}}"""));
        var next = Parse(await bridge.Handle("""{"id":2,"command":"ok","args":{}}"""));

        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.Equal("it broke", failed.GetProperty("error").GetProperty("message").GetString());
        Assert.True(next.GetProperty("ok").GetBoolean());
        Assert.Equal("fine", next.GetProperty("result").GetString());
    }

    [Fact]
    public void Event_carries_name_and_payload()
    {
        var message = Parse(Bridge.Event("mapOpened", new { MapName = "arena" }));

        Assert.Equal("mapOpened", message.GetProperty("event").GetString());
        Assert.Equal("arena", message.GetProperty("payload").GetProperty("mapName").GetString());
    }
}
