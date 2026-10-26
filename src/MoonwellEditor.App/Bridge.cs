using System.Text.Json;

namespace MoonwellEditor.App;

/// <summary>A command the bridge does not know. The bridge answers it with the error code <c>unknownCommand</c>.</summary>
public sealed class UnknownCommandException(string command)
    : Exception($"Unknown command '{command}'.")
{
    /// <summary>The command that was asked for.</summary>
    public string Command { get; } = command;
}

/// <summary>
/// A command that failed for a reason the page can act on. The bridge answers it with the error <see cref="Code"/>
/// and the message, so the page can tell it from a bug (for example <c>gameDataLoading</c>).
/// </summary>
public sealed class CommandException(string code, string message) : Exception(message)
{
    /// <summary>The error code the page sees.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// The page's way to ask the app for things. A request is <c>{"id","command","args"}</c>; the reply is
/// <c>{"id","ok":true,"result"}</c> or <c>{"id","ok":false,"error":{"code","message"}}</c>. It knows nothing about
/// WebView2: the window passes it the strings, so it can be tested without a window.
/// </summary>
public sealed class Bridge
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Func<string, JsonElement, Task<object?>> _run;

    /// <summary>Makes a bridge that runs every command through <paramref name="run"/>.</summary>
    /// <param name="run">Takes the command name and its arguments and gives the result, which is serialised as camelCase JSON.</param>
    public Bridge(Func<string, JsonElement, Task<object?>> run)
    {
        _run = run;
    }

    /// <summary>Answers one request. It never throws: every failure becomes an error reply.</summary>
    /// <param name="requestJson">The request, as the page sent it.</param>
    /// <returns>The reply, as JSON text.</returns>
    public async Task<string> Handle(string requestJson)
    {
        JsonElement id = default;
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CommandException("badRequest", "A request is a JSON object.");
            }

            if (root.TryGetProperty("id", out var idProperty))
            {
                id = idProperty.Clone();
            }

            if (!root.TryGetProperty("command", out var commandProperty)
                || commandProperty.ValueKind != JsonValueKind.String)
            {
                throw new CommandException("badRequest", "A request needs its command's name as text.");
            }

            var command = commandProperty.GetString()!;
            var args = root.TryGetProperty("args", out var argsProperty) ? argsProperty.Clone() : default;
            var result = await _run(command, args);
            return Reply(id, true, "result", result);
        }
        catch (UnknownCommandException exception)
        {
            return Reply(id, false, "error", new { code = "unknownCommand", message = exception.Message });
        }
        catch (CommandException exception)
        {
            return Reply(id, false, "error", new { code = exception.Code, message = exception.Message });
        }
        catch (JsonException exception)
        {
            return Reply(id, false, "error", new { code = "badRequest", message = exception.Message });
        }
        catch (Exception exception)
        {
            return Reply(id, false, "error", new { code = "failed", message = exception.Message });
        }
    }

    /// <summary>Makes a message the app sends to the page on its own: <c>{"event","payload"}</c>.</summary>
    /// <param name="name">The event's name.</param>
    /// <param name="payload">What the event carries, serialised as camelCase JSON.</param>
    /// <returns>The message, as JSON text.</returns>
    public static string Event(string name, object payload) =>
        JsonSerializer.Serialize(new { @event = name, payload }, Json);

    private static string Reply(JsonElement id, bool ok, string key, object? body)
    {
        var reply = new Dictionary<string, object?>
        {
            ["id"] = id.ValueKind == JsonValueKind.Undefined ? null : id,
            ["ok"] = ok,
            [key] = body,
        };
        return JsonSerializer.Serialize(reply, Json);
    }
}
