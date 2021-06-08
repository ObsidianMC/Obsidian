using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityTeleport : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1), VectorFormat(typeof(double))]
        public VectorF Position { get; set; }

        [Field(2)]
        public Angle Yaw { get; set; }

        [Field(3)]
        public Angle Pitch { get; set; }

        [Field(4)]
        public bool OnGround { get; set; }

        public int Id => 0x56;
    }
}
