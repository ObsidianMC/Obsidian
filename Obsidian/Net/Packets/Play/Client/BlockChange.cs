using Obsidian.API;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public class BlockChange : Packet
    {
        [Field(0)]
        public Position Location { get; private set; }

        [Field(1, Type = DataType.VarInt)]
        public int BlockId { get; private set; }

        public BlockChange() : base(0x0B) { }

        public BlockChange(Position loc, int block) : base(0x0B)
        {
            Location = loc;
            BlockId = block;
        }
    }
}