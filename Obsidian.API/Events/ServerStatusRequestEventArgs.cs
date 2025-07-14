namespace Obsidian.API.Events;

public class ServerStatusRequestEventArgs : BaseMinecraftEventArgs
{
    public ServerStatus Status { get; }

    internal ServerStatusRequestEventArgs(IServer server, ServerStatus status) : base(server)
    {
        this.Status = status;
    }
}
