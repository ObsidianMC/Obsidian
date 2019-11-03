using Obsidian.BlockData;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class BlockChange : Packet
    {
        public Position Location { get; private set; }
        public int BlockId { get; private set; }

        public BlockChange(Position loc, int block) : base(0x0B, System.Array.Empty<byte>())
        {
            Location = loc;
            BlockId = block;
        }

        public BlockChange(byte[] data) : base(0x0B, data)
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WritePositionAsync(Location);
            await stream.WriteVarIntAsync(BlockId);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            Location = await stream.ReadPositionAsync();
            BlockId = await stream.ReadVarIntAsync();
        }
    }
}