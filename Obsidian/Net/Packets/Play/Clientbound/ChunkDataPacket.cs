using Obsidian.Nbt;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System.Linq;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class ChunkDataPacket : IClientboundPacket
    {
        public Chunk Chunk { get; }

        public int Id => 0x20;

        public int changedSectionFilter = 65535; // 0b1111111111111111;

        public ChunkDataPacket(Chunk chunk)
        {
            Chunk = chunk;
        }

        public void Serialize(MinecraftStream minecraftStream)
        {
            using var stream = new MinecraftStream();
            using var dataStream = new MinecraftStream();

            stream.WriteInt(Chunk.X);
            stream.WriteInt(Chunk.Z);

            stream.WriteBoolean(true); // full chunk

            int chunkSectionY = 0, mask = 0;
            foreach (var section in Chunk.Sections)
            {
                if ((changedSectionFilter & 1 << chunkSectionY) != 0)
                {
                    mask |= 1 << chunkSectionY;
                    section.WriteTo(dataStream);
                }

                chunkSectionY++;
            }

            stream.WriteVarInt(mask);

            Chunk.CalculateHeightmap();
            var writer = new NbtWriter(stream, string.Empty);
            foreach (var (type, heightmap) in Chunk.Heightmaps)
                writer.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray().Cast<long>()));

            writer.EndCompound();

            Chunk.BiomeContainer.WriteTo(stream);

            dataStream.Position = 0;
            stream.WriteVarInt((int)dataStream.Length);
            dataStream.CopyTo(stream);

            stream.WriteVarInt(0);

            minecraftStream.Lock.Wait();
            minecraftStream.WriteVarInt(Id.GetVarIntLength() + (int)stream.Length);
            minecraftStream.WriteVarInt(Id);
            stream.Position = 0;
            stream.CopyTo(minecraftStream);
            minecraftStream.Lock.Release();
        }
    }
}
