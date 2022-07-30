namespace Obsidian.API.Events;

public sealed class IncomingChatMessageEventArgs : BaseEventArgs, IPlayerEvent, ICancellable
{
    internal IncomingChatMessageEventArgs(string message, string format, IPlayer player)
    {
        Message = message;
        Format = format;
        Player = player;
    }

    /// <summary>
    /// The message that was sent.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The message format.
    /// </summary>
    public string Format { get; }

    public IPlayer Player { get; }
    public bool Cancelled { get; set; }
}
