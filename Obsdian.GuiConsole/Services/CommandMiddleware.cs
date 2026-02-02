using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.Commands.Framework;

namespace Obsidian.GuiConsole.Services;

public class CommandMiddleware(IServer server)
{
    public async Task ExecuteCommandFromConsoleAsync(string commandText)
    {
        var commandContext = new CommandContext("/"+commandText, new CommandSender(CommandIssuers.Console,null), null,server);
        await server.CommandHandler.ProcessCommand(commandContext);
    }
}
