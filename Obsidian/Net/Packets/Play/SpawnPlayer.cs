using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnPlayer : Packet
    {
        [PacketOrder(0)]
        public int EntityId { get; set; }

        [PacketOrder(1)]
        public Guid Uuid { get; set; }

        [PacketOrder(2)]
        public Transform Tranform { get; set; }

        [PacketOrder(3)]
        public Player Player { get; set; }

        public SpawnPlayer() : base(0x05) { }
    }
}