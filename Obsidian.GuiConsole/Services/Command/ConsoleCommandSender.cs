using Microsoft.Extensions.Logging;
using Obsidian.API;
using System.Text.RegularExpressions;

namespace Obsidian.GuiConsole.Services.Command;

public class ConsoleCommandSender(ILogger<CommandMiddleware> logger): ICommandSender
{
    public CommandIssuers Issuer { get; } = CommandIssuers.Console;
    public IPlayer? Player { get; } = null;

    public Task SendMessageAsync(ChatMessage message)
    {
        List<string?> messageParts = new();
        messageParts.Add(message.Text);
        foreach (var extra in message.GetExtras())
            messageParts.Add(extra.Text);
        //log
        foreach (var messagePart in messageParts)
        {
            if (string.IsNullOrEmpty(messagePart) || messagePart == "\n")
            {
                continue;
            }
            //remove color codes §
            var clearMessage = Regex
                .Replace(messagePart, "§[0-9a-fk-or]", string.Empty, RegexOptions.IgnoreCase);
            //log to console
            logger.LogInformation("[Console] {MessagePart}", clearMessage);
        }
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(ChatMessage message, Guid sender) => throw new NotImplementedException();
}
