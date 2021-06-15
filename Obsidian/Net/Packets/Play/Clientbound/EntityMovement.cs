using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class EntityMovement : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        public int Id => 0x2A;
    }
}