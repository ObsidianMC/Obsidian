namespace Obsidian.API.Events;

/// <summary>
/// Represents the base class for classes that contain minecraft event data.
/// </summary>
public abstract class BaseMinecraftEventArgs : AsyncEventArgs
{
    /// <summary>
    /// Server this event took place in.
    /// </summary>
    public IServer Server { get; }

    public static EventType EventType { get; }

    /// <summary>
    /// Constructs a new instance of the <see cref="BaseMinecraftEventArgs"/> class.
    /// </summary>
    /// <param name="server">The server that's handling this event.</param>
    internal BaseMinecraftEventArgs(IServer server)
    {
        Server = server;
    }
}
