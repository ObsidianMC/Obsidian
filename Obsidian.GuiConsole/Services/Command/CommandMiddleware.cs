using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;

namespace Obsidian.GuiConsole.Services.Command;

public class CommandMiddleware(IServer server,ILogger<CommandMiddleware> logger)
{
    public async Task ExecuteCommandFromConsoleAsync(string commandText)
    {
        var context = new CommandContext(
            commandText,
            new ConsoleCommandSender(logger),null,
            server
        );

        try
        {
            await server.CommandHandler.ProcessCommand(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute command: {CommandText}", commandText);
        }
    }

}
