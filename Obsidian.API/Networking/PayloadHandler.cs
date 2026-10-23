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
    private readonly IServer server = null!;

    /// <summary>
    /// The connection the payload arrived on.
    /// </summary>
    public required IClient Client { get; init; }

    /// <summary>
    /// The player the connection logged in as.
    /// </summary>
    public IPlayer? Player { get; init; }

    /// <summary>
    /// The server. A plugin's handler gets the plugin's own <see cref="IServer"/>, whose additions are the plugin's
    /// (see <see cref="Events.BaseMinecraftEventArgs.Server"/>).
    /// </summary>
    public required IServer Server
    {
        get => ServerScope.Resolve(this.server);
        init => this.server = value;
    }

    /// <summary>
    /// The phase the connection was in when the payload arrived.
    /// </summary>
    public required PayloadPhase Phase { get; init; }
}
