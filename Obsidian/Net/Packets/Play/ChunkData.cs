using fNbt;

using Obsidian.ChunkData;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ChunkData : Packet
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public bool FullChunk { get; set; } = false;
        public int BitMask { get; set; } = 0;

        public List<ChunkSection> Data { get; set; } = new List<ChunkSection>();
        public List<int> Biomes { get; set; } = new List<int>();
        public List<NbtTag> BlockEntities { get; set; } = new List<NbtTag>();

        public ChunkData() : base(0x22, new byte[0])
        {
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteIntAsync(this.ChunkX);
                await stream.WriteIntAsync(this.ChunkY);

                await stream.WriteBooleanAsync(this.FullChunk);
                await stream.WriteVarIntAsync(this.BitMask);

                byte[] data;
                using (var dataStream = new MinecraftStream())
                {
                    foreach (ChunkSection section in Data)
                    {
                        await dataStream.WriteAsync(await section.ToArrayAsync());
                    }

                    if (this.FullChunk)
                    {
                        foreach (int biomeId in Biomes)
                        {
                            await dataStream.WriteIntAsync(biomeId);
                        }
                    }

                    data = dataStream.ToArray();
                }

                await stream.WriteVarIntAsync(data.Length);
                await stream.WriteAsync(data);

                await stream.WriteVarIntAsync(BlockEntities.Count);
                foreach (NbtTag entity in BlockEntities)
                {
                    throw new NotImplementedException("MinecraftStream.WriteNbtAsync(...) implementation is missing. Can't serialize this packet with block entities present.");
                }

                return stream.ToArray();
            }
        }

        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
