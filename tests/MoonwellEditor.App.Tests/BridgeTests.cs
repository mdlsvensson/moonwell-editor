using System.Text.Json;
using MoonwellEditor.App;

namespace MoonwellEditor.App.Tests;

public sealed class BridgeTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // Commands that never touch the settings or the game data: only ping and the unknown-command error are used.
    private static Commands PingOnly() =>
        new(
            new Settings(Path.Combine(Path.GetTempPath(), "MoonwellEditor.App.Tests", "bridge")),
            _ => null,
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
    public async Task Missing_command_is_a_bad_request()
    {
        var bridge = new Bridge(PingOnly().Run);

        var reply = Parse(await bridge.Handle("""{"id":3,"args":{}}"""));
        var notText = Parse(await bridge.Handle("""{"id":4,"command":5}"""));

        Assert.Equal(3, reply.GetProperty("id").GetInt32());
        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("badRequest", reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(4, notText.GetProperty("id").GetInt32());
        Assert.Equal("badRequest", notText.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"ping\"")]
    [InlineData("42")]
    [InlineData("not json")]
    public async Task A_request_that_is_not_an_object_is_a_bad_request_with_no_id(string request)
    {
        var bridge = new Bridge(PingOnly().Run);

        var reply = Parse(await bridge.Handle(request));

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("id").ValueKind);
        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("badRequest", reply.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_string_id_comes_back_as_it_was_sent()
    {
        var bridge = new Bridge(PingOnly().Run);

        var reply = Parse(await bridge.Handle("""{"id":"abc","command":"ping"}"""));

        Assert.Equal("abc", reply.GetProperty("id").GetString());
        Assert.True(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("pong", reply.GetProperty("result").GetString());
    }

    [Fact]
    public void Event_carries_name_and_payload()
    {
        var message = Parse(Bridge.Event("mapOpened", new { MapName = "arena" }));

        Assert.Equal("mapOpened", message.GetProperty("event").GetString());
        Assert.Equal("arena", message.GetProperty("payload").GetProperty("mapName").GetString());
    }
}
