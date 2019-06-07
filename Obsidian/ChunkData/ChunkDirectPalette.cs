using Obsidian.Net;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkDirectPalette : ChunkPalette
    {
        public override byte BitsPerBlock { get; set; } = 14;

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                return stream.ToArray();
            }
        }
    }
}
