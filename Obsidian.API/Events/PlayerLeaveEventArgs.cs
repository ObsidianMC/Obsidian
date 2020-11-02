namespace Obsidian.API.Events
{
    public class PlayerLeaveEventArgs : PlayerEventArgs
    {
        public PlayerLeaveEventArgs(IPlayer player) : base(player) { }
    }
}
