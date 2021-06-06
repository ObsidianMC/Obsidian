using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class BlockChange : IClientboundPacket
    {
        [Field(0)]
        public Vector Position { get; private set; }

        [Field(1), VarLength]
        public int BlockId { get; private set; }

        public int Id => 0x0B;

        public BlockChange(Vector position, int block)
        {
            Position = position;
            BlockId = block;
        }
    }
}