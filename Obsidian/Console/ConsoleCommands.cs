using Microsoft.Extensions.Logging;
using Obsidian.API.Commands;

namespace Obsidian.Console;

public sealed class ConsoleCommands(ILogger<ConsoleCommands> logger) : CommandModuleBase
{
    [Command("say")]
    [CommandInfo("Writes a message to the server log.", "/say <message>")]
    public void Say([Remaining] string message)
    {
        this.CommandContext.Server.BroadcastMessage(message);
        logger.LogInformation("[{Sender}] {Message}", this.CommandContext.Player?.Username ?? "Console", message);
    }
}
