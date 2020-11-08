using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.API.Events
{
    public class PlayerTeleportEventArgs : PlayerEventArgs
    {
        public Position OldPosition { get; }
        public Position NewPosition { get; }
        public PlayerTeleportEventArgs(IPlayer player, Position oldPosition, Position newPosition) : base(player)
        {
            OldPosition = oldPosition;
            NewPosition = newPosition;
        }
    }
}
