using Obsidian.Nbt;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelChunkWithLightPacket(IChunk chunk)
{
    public IChunk Chunk { get; } = chunk;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(Chunk.X);
        writer.WriteInt(Chunk.Z);

        //Chunk.CalculateHeightmap();
        var heightmapBuffer = new NetworkBuffer();

        using var nbtWriter = new RawNbtWriter(true);

        foreach (var (type, heightmap) in Chunk.Heightmaps)
            if (type == HeightmapType.MotionBlocking)
                nbtWriter.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray()));

        nbtWriter.EndCompound();
        nbtWriter.TryFinish();

        heightmapBuffer.Write(nbtWriter.Data);

        writer.Write(heightmapBuffer);

        var sectionBuffer = new NetworkBuffer();

        foreach (var section in Chunk.Sections)
        {
            if (!section.BlockStateContainer.IsEmpty)
            {
                section.BlockStateContainer.WriteTo(sectionBuffer);
                section.BiomeContainer.WriteTo(sectionBuffer);
            }
        }

        writer.WriteVarInt((int)sectionBuffer.Size);
        writer.Write(sectionBuffer);


        // Num block entities
        writer.WriteVarInt(0);

        // Lighting
        Chunk.WriteLightMaskTo(writer, LightType.Sky);
        Chunk.WriteLightMaskTo(writer, LightType.Block);

        Chunk.WriteEmptyLightMaskTo(writer, LightType.Sky);
        Chunk.WriteEmptyLightMaskTo(writer, LightType.Block);

        Chunk.WriteLightTo(writer, LightType.Sky);
        Chunk.WriteLightTo(writer, LightType.Block);
    }
}
