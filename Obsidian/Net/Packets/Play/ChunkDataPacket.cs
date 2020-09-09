using Obsidian.Nbt;
using Obsidian.World;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class ChunkDataPacket : Packet
    {
        public Chunk Chunk { get; set; }

        public int changedSectionFilter = 0b1111111111111111;

        public ChunkDataPacket(Chunk chunk) : base(0x22) => this.Chunk = chunk;

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            var sections = this.Chunk.Sections;
            var biomes = this.Chunk.Biomes;
            var blockEntities = this.Chunk.BlockEntities;
            //var heightmaps = this.Chunk.Heightmaps;

            bool fullChunk = true; // changedSectionFilter == 0b1111111111111111;

            await stream.WriteIntAsync(this.Chunk.X);
            await stream.WriteIntAsync(this.Chunk.Z);

            await stream.WriteBooleanAsync(fullChunk);

            int mask = 0;

            await using var dataStream = new MinecraftStream();

            var chunkSectionY = 0;
            foreach (var section in sections)
            {
                if (section == null)
                    throw new InvalidOperationException();

                if (fullChunk || (mask & (1 << chunkSectionY)) != 0)
                {
                    mask |= 1 << chunkSectionY;

                    await section.WriteToAsync(dataStream);
                }
                chunkSectionY++;
            }

            if (chunkSectionY != 16)
                throw new InvalidOperationException();

            await stream.WriteVarIntAsync(mask);

            /*Writing heightmap

            var nbtStream = new MemoryStream();
            var writer = new NbtWriter(nbtStream, "");

            foreach (var (name, map) in heightmaps)
            {
                writer.WriteLongArray(name, map.GetDataArray().Cast<long>().ToArray());
            }
            
            writer.EndCompound();
            writer.Finish();

            nbtStream.Position = 0;
            var reader = new NbtReader(nbtStream);

            var tag = reader.ReadAsTag();

            Console.WriteLine(tag.ToString());

            nbtStream.Position = 0;

            await nbtStream.CopyToAsync(dataStream);*/

            if (fullChunk)
            {
                if (biomes.Count != 16 * 16)
                    throw new InvalidOperationException();

                foreach (int biomeId in biomes)
                    await dataStream.WriteIntAsync(biomeId);
            }

            await stream.WriteVarIntAsync((int)dataStream.Length);

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);

            await stream.WriteVarIntAsync(blockEntities.Count);

            foreach (var entity in blockEntities)
                await stream.WriteNbtAsync(entity);
        }

        private int NeededBits(int value) => BitOperations.LeadingZeroCount((uint)value);
    }
}