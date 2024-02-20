using Microsoft.Extensions.Logging;

namespace Obsidian.Commands.Framework.Entities;

public class CommandSender(CommandIssuers issuer, IPlayer? player, ILogger logger) : ICommandSender
{
    private readonly ILogger logger = logger;

    public IPlayer? Player { get; } = player;
    public CommandIssuers Issuer { get; } = issuer;

    public async Task SendMessageAsync(ChatMessage message)
    {
        if (Issuer == CommandIssuers.Client)
        {
            await Player!.SendMessageAsync(message);

            //TODO make sure to send secure message if there's a sender
            return;
        }

        var messageString = message.Text;
        foreach (var extra in message.GetExtras())
            messageString += extra.Text;

        this.logger.LogInformation("{message}", messageString);
    }

    public Task SendMessageAsync(ChatMessage message, Guid sender) => throw new NotImplementedException();
}
