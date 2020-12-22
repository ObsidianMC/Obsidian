using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.API.Events
{
    public class PlayerTeleportEventArgs : PlayerEventArgs
    {
        public PositionF OldPosition { get; }
        public PositionF NewPosition { get; }
        public PlayerTeleportEventArgs(IPlayer player, PositionF oldPosition, PositionF newPosition) : base(player)
        {
            OldPosition = oldPosition;
            NewPosition = newPosition;
        }
    }
}
