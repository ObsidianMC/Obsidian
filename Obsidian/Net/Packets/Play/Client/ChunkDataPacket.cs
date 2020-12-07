using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Util.Extensions;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class ChunkDataPacket : IPacket
    {
        public Chunk Chunk { get; set; }

        public int Id => 0x20;

        public int changedSectionFilter = 65535;//0b1111111111111111;

        public ChunkDataPacket(Chunk chunk) : base() => this.Chunk = chunk;

        public async Task WriteAsync(MinecraftStream stream)
        {
            var sections = this.Chunk.Sections;
            var blockEntities = this.Chunk.BlockEntities;

            bool fullChunk = true;

            await stream.WriteIntAsync(this.Chunk.X);
            await stream.WriteIntAsync(this.Chunk.Z);

            await stream.WriteBooleanAsync(fullChunk);

            await using var dataStream = new MinecraftStream();

            int chunkSectionY = 0;
            int mask = 0;

            //Read sections to calculate mask and also write sections to a seperate stream
            foreach (var section in sections)
            {
                if (section == null)
                    throw new InvalidOperationException();

                if (fullChunk || (changedSectionFilter & 1 << chunkSectionY) != 0)
                {
                    mask |= 1 << chunkSectionY;
                    await section.WriteToAsync(dataStream);
                }

                chunkSectionY++;
            }

            if (chunkSectionY != 16)
                throw new InvalidOperationException();

            await stream.WriteVarIntAsync(mask);

            this.Chunk.CalculateHeightmap();
            var heightmaps = this.Chunk.Heightmaps;

            var writer = new NbtWriter(stream, "");
            foreach (var (type, heightmap) in heightmaps)
                writer.WriteLongArray(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray().Cast<long>().ToArray());

            writer.EndCompound();
            writer.Finish();

            if (fullChunk)
                await this.Chunk.BiomeContainer.WriteToAsync(stream);

            dataStream.Position = 0;
            await stream.WriteVarIntAsync((int)dataStream.Length);

            await dataStream.CopyToAsync(stream);

            await stream.WriteVarIntAsync(0);

            //foreach (var entity in blockEntities)
             //   await stream.WriteNbtAsync(entity);
        }

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;

        public void Serialize(MinecraftStream stream)
        {
            using var dataStream = new MinecraftStream();
            dataStream.WriteVarInt(Id);
            WriteAsync(dataStream).GetAwaiter().GetResult();

            stream.Lock.Wait();
            stream.WriteVarInt((int)dataStream.Length);
            dataStream.CopyTo(stream);
            stream.Lock.Release();
        }
    }
}