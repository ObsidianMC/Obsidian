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

        //Chunk.CalculateHeightmap();
        Chunk.CalculateSkyLight();
        var writer = new NbtWriter(stream, string.Empty);
        foreach (var (type, heightmap) in Chunk.Heightmaps)
            if (type == ChunkData.HeightmapType.MotionBlocking)
                writer.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray().Cast<long>()));

        writer.EndCompound();
        writer.TryFinish();

        foreach (var section in Chunk.Sections)
        {
            if (section is { BlockStateContainer.IsEmpty: false })
            {
                section.BlockStateContainer.WriteTo(dataStream);
                section.BiomeContainer.WriteTo(dataStream);
            }
        }

        dataStream.Position = 0;
        stream.WriteVarInt((int)dataStream.Length);
        dataStream.CopyTo(stream);

        // Num block entities
        stream.WriteVarInt(0);

        // Trust edges
        stream.WriteBoolean(true);

        // Lighting

        // Sky Light Mask
        Chunk.WriteSkyLightMaskTo(stream);

        // Block Light Mask
        stream.WriteVarInt(0);

        // Empty Sky Light Mask
        Chunk.WriteEmptySkyLightMaskTo(stream);

        // Empty Block Light Mask
        stream.WriteVarInt(1);
        stream.WriteLong(long.MaxValue);

        // sky light arrays
        Chunk.WriteSkyLightTo(stream);

        // block light arrays
        stream.WriteVarInt(0);


        minecraftStream.Lock.Wait();
        //minecraftStream.WriteVarInt(Id.GetVarIntLength() + (int)stream.Length);
        minecraftStream.WriteVarInt(Id);
        stream.Position = 0;
        stream.CopyTo(minecraftStream);
        minecraftStream.Lock.Release();
    }
}
