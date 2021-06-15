using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnEntity : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; init; }

        [Field(1)]
        public Guid Uuid { get; init; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public EntityType Type { get; init; }

        [Field(3), DataFormat(typeof(double))]
        public VectorF Position { get; init; }

        [Field(4)]
        public Angle Pitch { get; init; }

        [Field(5)]
        public Angle Yaw { get; init; }

        [Field(6)]
        public int Data { get; init; }

        [Field(7)]
        public Velocity Velocity { get; init; }

        public int Id => 0x00;
    }
}
