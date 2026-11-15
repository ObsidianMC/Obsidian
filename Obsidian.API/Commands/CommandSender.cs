namespace Obsidian.API.Commands;
/// <summary>
/// A basic <see cref="ICommandSender"/> for a player, the console or another issuer.
/// </summary>
/// <param name="issuer">The kind of sender.</param>
/// <param name="player">The player who sent the command, or <see langword="null"/> when no player did.</param>
public class CommandSender(CommandIssuers issuer, IPlayer? player) : ICommandSender
{
    /// <inheritdoc />
    public IPlayer? Player { get; } = player;
    /// <inheritdoc />
    public CommandIssuers Issuer { get; } = issuer;

    /// <summary>
    /// Sends <paramref name="message"/> to <see cref="Player"/> when the sender is a client; for other issuers the
    /// message is discarded.
    /// </summary>
    /// <param name="message">The message to send.</param>
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
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="sender">The UUID of the message's sender.</param>
    /// <exception cref="NotImplementedException">Always.</exception>
    public Task SendMessageAsync(ChatMessage message, Guid sender) => throw new NotImplementedException();
}
