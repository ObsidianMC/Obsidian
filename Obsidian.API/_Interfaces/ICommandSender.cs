namespace Obsidian.API;

/// <summary>
/// Whoever sent a command: a player, the console, the remote console or a plugin.
/// </summary>
public interface ICommandSender
{
    /// <summary>
    /// Gets the kind of sender.
    /// </summary>
    public CommandIssuers Issuer { get; }
    /// <summary>
    /// Gets the player who sent the command, or <see langword="null"/> if no player did.
    /// </summary>
    public IPlayer? Player { get; }
    /// <summary>
    /// Sends <paramref name="message"/> to the sender, as command feedback.
    /// </summary>
    /// <param name="message">The message to send.</param>
    public Task SendMessageAsync(ChatMessage message);

    /// <summary>
    /// Sends <paramref name="message"/> to the sender as a chat message from the player with UUID
    /// <paramref name="sender"/>.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="sender">The UUID of the message's sender.</param>
    public Task SendMessageAsync(ChatMessage message, Guid sender);
}
