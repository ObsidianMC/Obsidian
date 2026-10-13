namespace Obsidian.API.Events;

/// <summary>
/// Represents the base class for classes that contain minecraft event data.
/// </summary>
public abstract class BaseMinecraftEventArgs : AsyncEventArgs
{
    private readonly IServer server;

    /// <summary>
    /// Server this event took place in.
    /// </summary>
    /// <remarks>
    /// A plugin's handler gets its own <see cref="IServer"/>, whose scoreboards, boss bars, recipes and server
    /// players are the plugin's, so that unloading it removes them. The arguments themselves are shared by every
    /// handler: what one sets, like cancelling the event, the next sees.
    /// </remarks>
    public IServer Server => ServerScope.Resolve(this.server);

    /// <summary>
    /// Constructs a new instance of the <see cref="BaseMinecraftEventArgs"/> class.
    /// </summary>
    /// <param name="server">The server that's handling this event.</param>
    internal BaseMinecraftEventArgs(IServer server)
    {
        this.server = server;
    }
}
