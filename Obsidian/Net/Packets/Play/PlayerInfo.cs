using Obsidian.PlayerData.Info;
using Obsidian.Util;
using System.Collections.Generic;

namespace Obsidian.Net.Packets
{
    public class PlayerInfo : Packet
    {
        [Variable]
        public int Action { get; }

        [Variable]
        public int ActionCount => Actions.Count;

        [Variable]
        public List<PlayerInfoAction> Actions { get; }

        public PlayerInfo(int action, List<PlayerInfoAction> actions) : base(0x30)
        {
            this.Action = action;
            this.Actions = actions;
        }
    }
}