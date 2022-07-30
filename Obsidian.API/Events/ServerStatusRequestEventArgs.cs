namespace Obsidian.API.Events;

public sealed class ServerStatusRequestEventArgs : BaseEventArgs, IServerEvent, ICancellable
{
    internal ServerStatusRequestEventArgs(IServer server, IServerStatus status)
    {
        Server = server;
        Status = status;
    }

    public IServerStatus Status { get; }
    public IServer Server { get; }
    public bool Cancelled { get; set; }
}
