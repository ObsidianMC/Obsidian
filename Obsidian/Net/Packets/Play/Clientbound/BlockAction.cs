using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class BlockAction : IClientboundPacket
    {
        [Field(0)]
        public Vector Position { get; set; }

        [Field(1)]
        public byte ActionId { get; set; }

        [Field(2)]
        public byte ActionParam { get; set; }

        [Field(3), VarLength]
        public int BlockType { get; set; }

        public int Id => 0x0A;
    }
}
