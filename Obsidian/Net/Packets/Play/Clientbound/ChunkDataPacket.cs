using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChunkDataPacket : IClientboundPacket
{
    public Chunk Chunk { get; }

    public int Id => 0x22;

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

        int chunkSectionY = 0;

        //Probably best to make it into a class and support resizing but for now it works atleast
        var bits = new long[1];
        foreach (var section in Chunk.Sections)
        {
            if (section != null && !section.BlockStateContainer.IsEmpty)
            {
                //get index
                var index = chunkSectionY >> 6;

                //Set the bit
                bits[index] |= 1L << chunkSectionY;

                section.BlockStateContainer.WriteTo(dataStream);
                section.BiomeContainer.WriteTo(dataStream);
            }

            chunkSectionY++;
        }

        Chunk.CalculateHeightmap();
        var writer = new NbtWriter(stream, string.Empty);
        foreach (var (type, heightmap) in Chunk.Heightmaps)
            writer.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray().Cast<long>()));

        writer.EndCompound();

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
