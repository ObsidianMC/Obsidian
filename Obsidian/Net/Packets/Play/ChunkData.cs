using fNbt;
using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class ChunkDataPacket : Packet
    {
        public int ChunkX { get; set; }
        public int ChunkZ { get; set; }

        public bool FullChunk = true;

        public List<ChunkSection> Data { get; }
        public List<int> Biomes { get; }
        public List<NbtTag> BlockEntities { get; }

        public const int changedSectionFilter = 0b1111111111111111;

        public ChunkDataPacket() : base(0x22, Array.Empty<byte>()) { }

        public ChunkDataPacket(int chunkX, int chunkZ) : base(0x22, Array.Empty<byte>())
        {
            this.ChunkX = chunkX;
            this.ChunkZ = chunkZ;

            this.Data = new List<ChunkSection>();
            this.Biomes = new List<int>(16 * 16);
            this.BlockEntities = new List<NbtTag>();
        }

        internal async Task WriteToAsync(MinecraftStream stream)
        {
            bool fullChunk = true; // changedSectionFilter == 0b1111111111111111;

            await stream.WriteIntAsync(this.ChunkX);
            await stream.WriteIntAsync(this.ChunkZ);

            await stream.WriteBooleanAsync(fullChunk);

            int avaliableSections = 0;

            await using var dataStream = new MinecraftStream();

            var chunkSectionY = 0;
            foreach (var section in this.Data)
            {
                if (section == null)
                    throw new InvalidOperationException();

                if (fullChunk || (changedSectionFilter & (1 << chunkSectionY)) != 0)
                {
                    avaliableSections |= 1 << chunkSectionY;

                    await section.CopyToAsync(dataStream);
                }
                chunkSectionY++;
            }

            if (chunkSectionY != 16)
                throw new InvalidOperationException();

            if (fullChunk)
            {
                if (Biomes.Count != 16 * 16)
                    throw new InvalidOperationException();

                foreach (int biomeId in Biomes)
                {
                    await dataStream.WriteIntAsync(biomeId);
                }
            }
            await stream.WriteVarIntAsync(avaliableSections);

            var data = dataStream.ToArray();

            await stream.WriteVarIntAsync(data.Length);
            await stream.WriteAsync(data);

            await stream.WriteVarIntAsync(BlockEntities.Count);

            foreach (var entity in BlockEntities)
                await stream.WriteNbtAsync(entity);
        }
    }
}