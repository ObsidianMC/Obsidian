namespace Obsidian.API.Events;

public class IncomingChatMessageEventArgs : PlayerEventArgs, ICancellable
{
    /// <summary>
    /// The message that was sent.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// The message format.
    /// </summary>
    public string Format { get; set; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IncomingChatMessageEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player which sent the message.</param>
    /// <param name="message">The message which was sent.</param>
    /// <param name="format">Any formatting appied to the message.</param>
    public IncomingChatMessageEventArgs(IPlayer player, string message, string format) : base(player)
    {
        this.Message = message;
        this.Format = format;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
