using Obsidian.Net;

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkIndirectPalette : ChunkPalette
    {
        public List<int> Palette { get; set; } = new List<int>();

        public ChunkIndirectPalette(byte bitsPerBlock, List<int> palette)
        {
            this.BitsPerBlock = bitsPerBlock;
            this.Palette = palette;
        }
        public override byte BitsPerBlock { get; set; }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(this.Palette.Count);

                foreach (int item in this.Palette)
                {
                    await stream.WriteVarIntAsync(item);
                }

                return stream.ToArray();
            }
        }
    }
}
