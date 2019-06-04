using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection : ISerializable
    {
        public byte BitsPerBlock;
        public ChunkPalette Palette;
        public long[] Data;
        public byte[] BlockLight;
        public byte[] SkyLight;

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteUnsignedByteAsync(BitsPerBlock);



                await stream.WriteVarIntAsync(Data.Length);

                foreach (long item in Data)
                {
                    await stream.WriteLongAsync(item);
                }

                await stream.WriteAsync(BlockLight);

                if (SkyLight != null)
                {
                    await stream.WriteAsync(SkyLight);
                }
                return stream.ToArray();
            }
        }
    }
}
