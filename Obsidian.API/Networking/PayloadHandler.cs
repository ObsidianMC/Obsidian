namespace Obsidian.API.Networking;

/// <summary>
/// Handles a payload a client sent on a registered channel.
/// </summary>
public delegate ValueTask PayloadHandler<TPayload>(TPayload payload, PayloadContext context);

/// <summary>
/// Where a received payload came from.
/// </summary>
public sealed class PayloadContext
{
    /// <summary>
    /// The connection the payload arrived on.
    /// </summary>
    public required IClient Client { get; init; }

    /// <summary>
    /// The player the connection logged in as.
    /// </summary>
    public IPlayer? Player { get; init; }

    public required IServer Server { get; init; }

    /// <summary>
    /// The phase the connection was in when the payload arrived.
    /// </summary>
    public required PayloadPhase Phase { get; init; }
}
