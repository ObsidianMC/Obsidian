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

    public bool Cancel { get; set; }

    public IncomingChatMessageEventArgs(IPlayer player, string message, string format) : base(player)
    {
        this.Message = message;
        this.Format = format;
    }
}
