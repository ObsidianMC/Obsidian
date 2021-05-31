using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SpawnPlayer : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2), Absolute]
        public VectorF Position { get; set; }

        [Field(3)]
        public Angle Yaw { get; set; }

        [Field(4)]
        public Angle Pitch { get; set; }

        public int Id => 0x04;
    }
}