using Obsidian.Nbt;
using Obsidian.WorldData;
using Obsidian.WorldData.Lighting;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelChunkWithLightPacket(IChunk chunk)
{
    public IChunk Chunk { get; } = chunk;

    // Captured when the packet is made, before it enters the send queue, which serializes it later.
    private readonly LightUpdatePacket light = chunk is Chunk { RuntimeLight: RuntimeLightEngine engine }
        ? engine.SnapshotLight(chunk)
        : new LightUpdatePacket(chunk);

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


        // Like vanilla's, every block entity of a type clients know, with its update data. A plugin's block entity
        // sends what it gave last (see BlockEntity.ClientData), so that its code doesn't run on this thread.
        var blockEntities = Chunk.GetBlockEntities()
            .OfType<API.BlockEntities.BlockEntity>()
            .Select(blockEntity => (BlockEntity: blockEntity, Type: BlockEntityTypesRegistry.NetworkId(blockEntity.Id)))
            .Where(entry => entry.Type >= 0)
            .ToList();

        writer.WriteVarInt(blockEntities.Count);
        foreach (var (blockEntity, type) in blockEntities)
        {
            var position = blockEntity.BlockPosition;
            writer.WriteByte((sbyte)((position.X & 15) << 4 | (position.Z & 15)));
            writer.WriteShort((short)position.Y);
            writer.WriteVarInt(type);
            WriteNbt(writer, blockEntity.SavesOnAnyThread ? blockEntity.GetUpdateData() : blockEntity.ClientData);
        }

        // Lighting
        this.light.WriteLight(writer);
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
