using System.Text.Json;

namespace ObjectEditor.App;

/// <summary>The commands the page can call. Each task adds its own here.</summary>
public static class Commands
{
    /// <summary>Runs one command by name.</summary>
    /// <param name="command">The command's name.</param>
    /// <param name="args">The command's arguments, as the page sent them.</param>
    /// <returns>The command's result.</returns>
    /// <exception cref="UnknownCommandException">There is no command of that name.</exception>
    public static Task<object?> Run(string command, JsonElement args) =>
        command switch
        {
            "ping" => Task.FromResult<object?>("pong"),
            _ => throw new UnknownCommandException(command),
        };
}
