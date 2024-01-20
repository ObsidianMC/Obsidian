namespace Obsidian.API.Events;

public class PlayerTeleportEventArgs : PlayerEventArgs
{
    public override string Name => "PlayerTeleport";

    public VectorF OldPosition { get; }
    public VectorF NewPosition { get; }

    public PlayerTeleportEventArgs(IPlayer player, IServer server, VectorF oldPosition, VectorF newPosition) : base(player, server)
    {
        OldPosition = oldPosition;
        NewPosition = newPosition;
    }
}
