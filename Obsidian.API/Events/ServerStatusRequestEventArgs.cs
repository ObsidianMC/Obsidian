namespace Obsidian.API.Events;

public class ServerStatusRequestEventArgs : BaseMinecraftEventArgs
{
    public override string Name => "ServerStatusRequest";

    public IServerStatus Status { get; }

    internal ServerStatusRequestEventArgs(IServer server, IServerStatus status) : base(server)
    {
        this.Status = status;
    }
}
