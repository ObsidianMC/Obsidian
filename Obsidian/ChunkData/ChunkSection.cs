using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection : ISerializable
    {
        private IBlockStatePalette palette;

        private int BitCount
        {
            get { return 0; }
            set
            {
                // TODO add linear palette
            }
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                //await stream.WriteByteAsync(bitCount);

                return stream.ToArray();
            }
        }
    }

}
