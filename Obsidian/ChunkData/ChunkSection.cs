using Obsidian.Net;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection : ISerializable
    {
        public byte BitsPerBlock => ChunkPalette == null ? (byte)0 : ChunkPalette.BitsPerBlock;

        public ChunkPalette ChunkPalette;

        public ChunkSection(byte bpb)
        {
            this.ChunkPalette = this.GetPalette(bpb);
        }

        /// <summary>
        /// Calculates how many bit places are required from an array
        /// </summary>
        /// https://stackoverflow.com/questions/7150035/calculating-bits-required-to-store-decimal-number#7150113
        public int GetBits(int length) => (int)(Math.Log(length) / Math.Log(2));

        public long[] Data;
        public byte[] BlockLight;
        public byte[] SkyLight;

        public ChunkPalette GetPalette(byte bitsPerBlock)
        {
            if (bitsPerBlock <= 4 || bitsPerBlock <= 8)
            {
                return new ChunkIndirectPalette(bitsPerBlock);
            }
            else
            {
                return new ChunkDirectPalette();
            }
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteUnsignedByteAsync(BitsPerBlock);

                await stream.WriteAsync(await ChunkPalette.ToArrayAsync());

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
