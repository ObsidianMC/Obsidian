namespace Obsidian.API.Events
{
    public class PlayerTeleportEventArgs : PlayerEventArgs
    {
        public VectorF OldPosition { get; }
        public VectorF NewPosition { get; }
        public PlayerTeleportEventArgs(IPlayer player, VectorF oldPosition, VectorF newPosition) : base(player)
        {
            OldPosition = oldPosition;
            NewPosition = newPosition;
        }
    }
}
