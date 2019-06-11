using System;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkDirectPalette : ChunkPalette
    {
        public override byte BitsPerBlock { get; set; } = 14;

        public override Task<byte[]> ToArrayAsync() => throw new NotImplementedException();
    }
}
