using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;

namespace Obsidian.GuiConsole.Services.Command;

/// <summary>
/// Command middleware to process commands from GUI console
/// </summary>
/// <param name="server">Server Instance</param>
/// <param name="logger">log</param>
public class CommandMiddleware(IServer server,ILogger<CommandMiddleware> logger)
{
    public async Task ExecuteCommandFromConsoleAsync(string commandText)
    {
        await server.CommandHandler.ProcessCommand(new CommandContext(
            "/"+commandText,
            new ConsoleCommandSender(logger),null,
            server
        ));
    }

}
