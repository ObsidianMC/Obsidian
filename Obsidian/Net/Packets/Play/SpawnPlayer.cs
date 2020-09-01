using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnPlayer : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2)]
        public Transform Transform { get; set; }

        [Field(3)]
        public byte EOF = 255;


        public Player Player { get; set; }

        public SpawnPlayer() : base(0x05) { }
    }
}