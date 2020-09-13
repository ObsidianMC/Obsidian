using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play.Client
{
    public class SpawnPlayer : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2, true)]
        public Position Position { get; set; }

        [Field(3)]
        public Angle Yaw { get; set; }

        [Field(4)]
        public Angle Pitch { get; set; }

        public SpawnPlayer() : base(0x05) { }
    }
}