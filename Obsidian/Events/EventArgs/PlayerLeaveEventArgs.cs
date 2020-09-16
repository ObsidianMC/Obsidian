using Obsidian.Entities;

namespace Obsidian.Events.EventArgs
{
    public class PlayerLeaveEventArgs : PlayerEventArgs
    {
        internal PlayerLeaveEventArgs(Player player) : base(player) { }
    }
}
