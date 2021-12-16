using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChunkDataPacket : IClientboundPacket
{
    public Chunk Chunk { get; }

    public int Id => 0x22;

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

        foreach (var section in Chunk.Sections)
        {
            if (section != null && !section.BlockStateContainer.IsEmpty)
            {
                dataStream.WriteShort(section.BlockCount);
                section.BlockStateContainer.WriteTo(dataStream);
                section.BiomeContainer.WriteTo(dataStream);
            }
        }

        Chunk.CalculateHeightmap();
        var writer = new NbtWriter(stream, string.Empty);
        foreach (var (type, heightmap) in Chunk.Heightmaps)
            if(type == ChunkData.HeightmapType.MotionBlocking)
                writer.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray().Cast<long>()));

        writer.EndCompound();

        dataStream.Position = 0;
        stream.WriteVarInt((int)dataStream.Length);
        dataStream.CopyTo(stream);

        // Num block entities
        stream.WriteVarInt(0);

        // Trust edges
        stream.WriteBoolean(true);

        // Lighting
        long skyLightMask = 0L;
        long blockLightMask = 0L;
        long emptySkyLightMask = long.MaxValue;
        long emptyBlockLightMask = long.MaxValue;

        int skyLightArrayCount = 0;
        int blockLightArrayCount = 0;

        // sky light bitset
        stream.WriteVarInt(0);
        //stream.WriteLong(skyLightMask);
        
        // block light bitset
        stream.WriteVarInt(0);
        //stream.WriteLong(blockLightMask);

        // empty sky light bitset
        stream.WriteVarInt(1);
        stream.WriteLong(emptySkyLightMask);

        // empty block light bitset
        stream.WriteVarInt(1);
        stream.WriteLong(emptyBlockLightMask);

        // sky light arrays
        stream.WriteVarInt(skyLightArrayCount);

        // block light arrays
        stream.WriteVarInt(blockLightArrayCount);


        minecraftStream.Lock.Wait();
        minecraftStream.WriteVarInt(Id.GetVarIntLength() + (int)stream.Length);
        minecraftStream.WriteVarInt(Id);
        stream.Position = 0;
        stream.CopyTo(minecraftStream);
        minecraftStream.Lock.Release();
    }
}
