using fNbt;
using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ChunkDataPacket : Packet
    {
        public int ChunkX { get; set; }
        public int ChunkZ { get; set; }
        
        public List<ChunkSection> Data { get; }
        public List<int> Biomes { get; }
        public List<NbtTag> BlockEntities { get; }

        public int changedSectionFilter = 0b1111111111111111;
        public ChunkDataPacket() : base(0x22, new byte[0]) { }

        public ChunkDataPacket(int chunkX, int chunkZ) : base(0x22, new byte[0])
        {
            this.ChunkX = chunkX;
            this.ChunkZ = chunkZ;

            this.Data = new List<ChunkSection>();
            this.Biomes = new List<int>(16 * 16);
            this.BlockEntities = new List<NbtTag>(); 
        }

        public override async Task<byte[]> SerializeAsync()
        {
            bool fullChunk = true; // changedSectionFilter == 0b1111111111111111;

            using (var stream = new MinecraftStream())
            {
                await stream.WriteIntAsync(this.ChunkX);
                await stream.WriteIntAsync(this.ChunkZ);

                await stream.WriteBooleanAsync(fullChunk);

                int availableSections = 0;

                byte[] data;
                using (var dataStream = new MinecraftStream())
                {
                    var chunkSectionY = 0;
                    foreach (ChunkSection section in Data)
                    {
                        if (section == null)
                            throw new InvalidOperationException();

                        if (fullChunk || (changedSectionFilter & (1 << chunkSectionY)) != 0) {

                            availableSections |= 1 << chunkSectionY;

                            await dataStream.WriteAsync(await section.ToArrayAsync());

                        }
                        chunkSectionY++;
                    }
                    if(chunkSectionY != 16)
                        throw new InvalidOperationException();

                    if (fullChunk)
                    {
                        if(Biomes.Count != 16 * 16)
                            throw new InvalidOperationException();

                        foreach (int biomeId in Biomes)
                        {
                            await dataStream.WriteIntAsync(biomeId);
                        }
                    }

                    data = dataStream.ToArray();
                }
                await stream.WriteVarIntAsync(availableSections);

                await stream.WriteVarIntAsync(data.Length);
                await stream.WriteAsync(data);

                await stream.WriteVarIntAsync(BlockEntities.Count);
                foreach (NbtTag entity in BlockEntities)
                {
                    await stream.WriteNbtAsync(entity);
                }

                return stream.ToArray();
            }
        }
    }
}
