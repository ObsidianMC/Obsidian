namespace Obsidian.API.Events;

public sealed class PlayerTeleportEventArgs : BaseEventArgs, IPlayerEvent
{
    internal PlayerTeleportEventArgs(IPlayer player, VectorF oldPosition, VectorF newPosition)
    {
        Player = player;
        OldPosition = oldPosition;
        NewPosition = newPosition;
    }

    public VectorF OldPosition { get; }
    public VectorF NewPosition { get; }
    public IPlayer Player { get; }
}
