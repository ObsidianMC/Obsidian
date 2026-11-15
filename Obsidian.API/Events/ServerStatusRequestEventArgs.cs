namespace Obsidian.API.Events;

/// <summary>
/// A client asking for the server's status in the server list, raised before the server sends <see cref="Status"/>
/// back.
/// </summary>
public class ServerStatusRequestEventArgs : BaseMinecraftEventArgs
{
    /// <summary>
    /// Gets the status the server will send, which handlers can change.
    /// </summary>
    public ServerStatus Status { get; }

    internal ServerStatusRequestEventArgs(IServer server, ServerStatus status) : base(server)
    {
        this.Status = status;
    }
}
