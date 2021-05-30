using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityPosition : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public short DeltaX { get; set; }

        [Field(2)]
        public short DeltaY { get; set; }

        [Field(3)]
        public short DeltaZ { get; set; }

        [Field(4)]
        public bool OnGround { get; set; }

        public int Id => 0x27;
    }
}
