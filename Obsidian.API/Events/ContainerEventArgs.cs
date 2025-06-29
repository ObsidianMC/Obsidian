namespace Obsidian.API.Events;

public class ContainerEventArgs : PlayerEventArgs
{
    public BaseContainer Container { get; init; }

    protected ContainerEventArgs(IPlayer player, IServer server, BaseContainer container) : base(player, server)
    {
        this.Container = container ?? throw new ArgumentNullException(nameof(container), "Container cannot be null.");
    }
}
