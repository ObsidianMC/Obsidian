using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelChunkWithLightPacket(Chunk chunk)
{
    public Chunk Chunk { get; } = chunk;

    public override void Serialize(INetStreamWriter writer)
    {
        using var stream = new MinecraftStream();
        using var dataStream = new MinecraftStream();

        stream.WriteInt(Chunk.X);
        stream.WriteInt(Chunk.Z);

        //Chunk.CalculateHeightmap();
        var nbtWriter = new NbtWriter(stream, true);
        foreach (var (type, heightmap) in Chunk.Heightmaps)
            if (type == ChunkData.HeightmapType.MotionBlocking)
                nbtWriter.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray()));

        nbtWriter.EndCompound();
        nbtWriter.TryFinish();

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
        Chunk.WriteLightMaskTo(stream, LightType.Sky);
        Chunk.WriteLightMaskTo(stream, LightType.Block);

        Chunk.WriteEmptyLightMaskTo(stream, LightType.Sky);
        Chunk.WriteEmptyLightMaskTo(stream, LightType.Block);

        Chunk.WriteLightTo(stream, LightType.Sky);
        Chunk.WriteLightTo(stream, LightType.Block);

        //PRobably make something so we don't cast :sweat_smile:
        stream.CopyTo((MinecraftStream)writer);
    }
}
