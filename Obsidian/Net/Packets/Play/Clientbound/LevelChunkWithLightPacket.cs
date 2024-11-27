using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelChunkWithLightPacket(Chunk chunk)
{
    public Chunk Chunk { get; } = chunk;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(Chunk.X);
        writer.WriteInt(Chunk.Z);

        //Chunk.CalculateHeightmap();
        using (var heightmapStream = new MinecraftStream())
        {
            var nbtWriter = new NbtWriter(heightmapStream, true);
            foreach (var (type, heightmap) in Chunk.Heightmaps)
                if (type == ChunkData.HeightmapType.MotionBlocking)
                    nbtWriter.WriteTag(new NbtArray<long>(type.ToString().ToSnakeCase().ToUpper(), heightmap.GetDataArray()));

            nbtWriter.EndCompound();
            nbtWriter.TryFinish();

            heightmapStream.Position = 0;
            heightmapStream.CopyTo((MinecraftStream)writer);
        }

        using (var sectionStream = new MinecraftStream())
        {
            foreach (var section in Chunk.Sections)
            {
                if (section is { BlockStateContainer.IsEmpty: false })
                {
                    section.BlockStateContainer.WriteTo(sectionStream);
                    section.BiomeContainer.WriteTo(sectionStream);
                }
            }

            sectionStream.Position = 0;

            writer.WriteVarInt((int)sectionStream.Length);
            sectionStream.CopyTo((MinecraftStream)writer);
        }

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
