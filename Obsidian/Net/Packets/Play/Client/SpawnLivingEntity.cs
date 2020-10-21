using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play.Client
{
    public class SpawnLivingEntity : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public EntityType Type { get; set; }

        [Field(3, true)]
        public Position Position { get; set; }

        [Field(4)]
        public Angle Yaw { get; set; }

        [Field(5)]
        public Angle Pitch { get; set; }

        [Field(6)]
        public Angle HeadPitch { get; set; }

        [Field(7)]
        public Velocity Velocity { get; set; }

        public SpawnLivingEntity() : base(0x02) { }

    }
}