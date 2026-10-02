using Obsidian.API.Commands;

namespace Obsidian.Console;

public sealed class ConsoleCommands : CommandModuleBase
{
    [Command("say")]
    [CommandInfo("Writes a message to the server log.", "/say <message>")]
    [IssuerScope(CommandIssuers.Console)]
    public void Say([Remaining] string message)
    {
        // Broadcasting logs the message too.
        this.CommandContext.Server.BroadcastMessage(message);
    }
}
