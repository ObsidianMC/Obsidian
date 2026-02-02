using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;

namespace Obsidian.GuiConsole.Services.Command;

public class CommandMiddleware(IServer server,ILogger<CommandMiddleware> logger)
{
    public async Task ExecuteCommandFromConsoleAsync(string commandText)
    {
        var commandContext = new CommandContext("/"+commandText, new ConsoleCommandSender(logger), null,server);
        await server.CommandHandler.ProcessCommand(commandContext);
    }
}
