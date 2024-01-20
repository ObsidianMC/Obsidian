namespace Obsidian.API.Events;

public class PlayerEventArgs : BaseMinecraftEventArgs
{
    public override string Name => "PlayerEvent";

    /// <summary>
    /// The player involved in this event.
    /// </summary>
    public IPlayer Player { get; }

    protected PlayerEventArgs(IPlayer player, IServer server) : base(server)
    {
        Player = player;
    }
}
