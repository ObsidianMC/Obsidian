using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SpawnEntity : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public EntityType Type { get; set; }

        [Field(3), Absolute]
        public VectorF Position { get; set; }

        [Field(4)]
        public Angle Pitch { get; set; }

        [Field(5)]
        public Angle Yaw { get; set; }

        [Field(6)]
        public int Data { get; set; }

        [Field(7)]
        public Velocity Velocity { get; set; }

        public int Id => 0x00;
    }
}
