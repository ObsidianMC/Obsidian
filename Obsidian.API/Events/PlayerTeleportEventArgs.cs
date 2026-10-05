namespace Obsidian.API.Events;

public class PlayerTeleportEventArgs : PlayerEventArgs
{
    public VectorD OldPosition { get; }
    public VectorD NewPosition { get; }

    public PlayerTeleportEventArgs(IPlayer player, IServer server, VectorD oldPosition, VectorD newPosition) : base(player, server)
    {
        OldPosition = oldPosition;
        NewPosition = newPosition;
    }
}
