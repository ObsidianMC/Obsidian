using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelChunkWithLightPacket(IChunk chunk)
{
    public IChunk Chunk { get; } = chunk;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(Chunk.X);
        writer.WriteInt(Chunk.Z);

        //Chunk.CalculateHeightmap();

        writer.WriteVarInt(1);
        foreach (var (type, heightmap) in Chunk.Heightmaps)
            if (type == HeightmapType.MotionBlocking)
            {
                var dataArray = heightmap.GetDataArray();

                writer.WriteVarInt(type.GetHashCode());
                writer.WriteVarInt(dataArray.Length);

                foreach(var height in dataArray)
                    writer.WriteLong(height);

                break;
            }

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


        // Block entities kept as data; clients create the others from the block states when they're placed.
        var blockEntities = Chunk.GetBlockEntities().OfType<DataBlockEntity>()
            .Where(blockEntity => BlockPhysics.BlockEntityTypeId(blockEntity.Id) >= 0)
            .ToList();

        writer.WriteVarInt(blockEntities.Count);
        foreach (var blockEntity in blockEntities)
        {
            var position = blockEntity.BlockPosition;
            writer.WriteByte((sbyte)((position.X & 15) << 4 | (position.Z & 15)));
            writer.WriteShort((short)position.Y);
            writer.WriteVarInt(BlockPhysics.BlockEntityTypeId(blockEntity.Id));
            WriteNbt(writer, blockEntity.GetClientData());
        }

        // Lighting
        Chunk.WriteLightMaskTo(writer, LightType.Sky);
        Chunk.WriteLightMaskTo(writer, LightType.Block);

        Chunk.WriteEmptyLightMaskTo(writer, LightType.Sky);
        Chunk.WriteEmptyLightMaskTo(writer, LightType.Block);

        Chunk.WriteLightTo(writer, LightType.Sky);
        Chunk.WriteLightTo(writer, LightType.Block);
    }

    /// <summary>
    /// Writes a compound as network NBT (an unnamed root), or an end tag for <c>null</c>.
    /// </summary>
    private static void WriteNbt(INetStreamWriter writer, NbtCompound? compound)
    {
        if (compound is null)
        {
            writer.WriteByte((sbyte)NbtTagType.End);
            return;
        }

        using var nbtWriter = new RawNbtWriter(true);
        foreach (var (_, tag) in compound)
            nbtWriter.WriteTag(tag);

        nbtWriter.EndCompound();
        nbtWriter.TryFinish();

        writer.WriteByteArray(nbtWriter.Data.ToArray());
    }
}
