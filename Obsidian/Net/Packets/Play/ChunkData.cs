using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ChunkData : Packet
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public bool FullChunk { get; set; } = false;
        public int BitMask { get; set; } = 0;

        public ChunkData() : base(0x22, new byte[0])
        {
        }

        public override Task<byte[]> ToArrayAsync() => throw new NotImplementedException();
        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
