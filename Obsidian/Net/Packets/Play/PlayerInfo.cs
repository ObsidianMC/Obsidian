using Obsidian.PlayerData.Info;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerInfo : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int Action { get; }

        [Field(1, Type = DataType.Array)]
        public List<PlayerInfoAction> Actions { get; }

        public PlayerInfo() : base(0x30) { }

        public PlayerInfo(int action, List<PlayerInfoAction> actions) : base(0x30)
        {
            this.Action = action;
            this.Actions = actions;
        }
    }
}