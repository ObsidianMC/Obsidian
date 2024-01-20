namespace Obsidian.API.Events;

public class ContainerEventArgs : PlayerEventArgs
{
    public override string Name => "ContainerEvent";

    public required BaseContainer Container { get; init; }

    protected ContainerEventArgs(IPlayer player, IServer server) : base(player, server)
    {
    }
}
