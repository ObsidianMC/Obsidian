using Obsidian.Nbt;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelChunkWithLightPacket(IChunk chunk)
{
    public IChunk Chunk { get; } = chunk;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(Chunk.X);
        writer.WriteInt(Chunk.Z);

        //Heightmap writing we're only sending motion blocking for now.
        writer.WriteVarInt(1);
        var heightmap = Chunk.Heightmaps[HeightmapType.MotionBlocking];
        var heightmapStorage = heightmap.GetDataArray();

        writer.WriteVarInt(HeightmapType.MotionBlocking.GetHashCode());
        writer.WriteLengthPrefixedArray(writer.WriteLong, heightmapStorage);

        var sectionBuffer = new NetworkBuffer();

        foreach (var section in Chunk.Sections)
        {
            if (!section.BlockStateContainer.IsEmpty)
            {
                section.BlockStateContainer.WriteTo(sectionBuffer);
                section.BiomeContainer.WriteTo(sectionBuffer);
            }
        }

        writer.WriteVarInt(sectionBuffer.Size);
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
