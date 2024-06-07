using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChunkDataAndUpdateLightPacket : IClientboundPacket
{
    public Chunk Chunk { get; }

    public int Id => 0x27;

    public ChunkDataAndUpdateLightPacket(Chunk chunk)
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
        var writer = new NbtWriter(stream, true);
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

        // Lighting

        // Sky Light Mask
        Chunk.WriteLightMaskTo(stream, LightType.Sky);

        // Block Light Mask
        Chunk.WriteLightMaskTo(stream, LightType.Block);

        // Empty Sky Light Mask
        Chunk.WriteEmptyLightMaskTo(stream, LightType.Sky);

        // Empty Block Light Mask
        Chunk.WriteEmptyLightMaskTo(stream, LightType.Block);

        // sky light arrays
        Chunk.WriteLightTo(stream, LightType.Sky);

        // block light arrays
        Chunk.WriteLightTo(stream, LightType.Block);

        minecraftStream.Lock.Wait();
        minecraftStream.WriteVarInt(Id.GetVarIntLength() + (int)stream.Length);
        minecraftStream.WriteVarInt(Id);
        stream.Position = 0;
        stream.CopyTo(minecraftStream);
        minecraftStream.Lock.Release();
    }
}
