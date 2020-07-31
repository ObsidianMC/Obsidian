using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class BlockChange : Packet
    {
        [PacketOrder(0)]
        public Position Location { get; private set; }

        [PacketOrder(1)]
        public int BlockId { get; private set; }

        public BlockChange() : base(0x0B) { }

        public BlockChange(Position loc, int block) : base(0x0B, System.Array.Empty<byte>())
        {
            Location = loc;
            BlockId = block;
        }
    }
}