using Obsidian.BlockData;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class BlockChange : Packet
    {
        public Position Location { get; private set; }
        public int BlockId { get; private set; }

        public BlockChange(Position loc, int block) : base(0x0B, new byte[0])
        {
            Location = loc;
            BlockId = block;
        }

        public BlockChange(byte[] data) : base(0x0B, data) { }

        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                Location = await stream.ReadPositionAsync();
                BlockId = await stream.ReadVarIntAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WritePositionAsync(Location);
                await stream.WriteVarIntAsync(BlockId);
                return stream.ToArray();
            }
        }
    }
}
