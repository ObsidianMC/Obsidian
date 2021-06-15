using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnPlayer : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; init; }

        [Field(1)]
        public Guid Uuid { get; init; }

        [Field(2), DataFormat(typeof(double))]
        public VectorF Position { get; init; }

        [Field(3)]
        public Angle Yaw { get; init; }

        [Field(4)]
        public Angle Pitch { get; init; }

        public int Id => 0x04;
    }
}
