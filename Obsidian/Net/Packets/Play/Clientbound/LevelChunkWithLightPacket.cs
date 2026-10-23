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

    // The sections are written here first, since their size goes ahead of them. Reused by the thread's chunk packets:
    // Serialize copies it into the packet before returning, and nothing it calls serializes another chunk packet.
    [ThreadStatic]
    private static NetworkBuffer? sectionScratch;

    // A scratch grown past this by an unusually varied chunk is dropped rather than kept for the life of the thread.
    private const int MaxRetainedSectionScratch = 1024 * 1024;

    // Room for the packet's fixed fields and a block entity's update data on top of what's known exactly, so that
    // the body rarely has to grow (and copy itself) after it's sized.
    private const int FixedFieldsSize = 64;
    private const int BlockEntitySize = 256;

    public override void Serialize(INetStreamWriter writer)
    {
        var sections = sectionScratch ??= new NetworkBuffer();
        sections.Clear();

        foreach (var section in Chunk.Sections)
        {
            if (!section.BlockStateContainer.IsEmpty)
            {
                section.BlockStateContainer.WriteTo(sections);
                section.BiomeContainer.WriteTo(sections);
            }
        }

        var heights = Chunk.Heightmaps.TryGetValue(HeightmapType.MotionBlocking, out var motionBlocking)
            ? motionBlocking.GetDataArray()
            : null;

        // Like vanilla's, every block entity of a type clients know, with its update data. A plugin's block entity
        // sends what it gave last (see BlockEntity.ClientData), so that its code doesn't run on this thread.
        var blockEntities = Chunk.GetBlockEntities()
            .OfType<API.BlockEntities.BlockEntity>()
            .Select(blockEntity => (BlockEntity: blockEntity, Type: BlockEntityTypesRegistry.NetworkId(blockEntity.Id)))
            .Where(entry => entry.Type >= 0)
            .ToList();

        // Sized once for the whole packet, rather than doubling its way there from a few bytes.
        if (writer is NetworkBuffer body)
        {
            body.EnsureWritable(FixedFieldsSize
                + sizeof(long) * (heights?.Length ?? 0)
                + sections.Size
                + BlockEntitySize * blockEntities.Count
                + this.light.MaxLightSize());
        }

        writer.WriteInt(Chunk.X);
        writer.WriteInt(Chunk.Z);

        writer.WriteVarInt(1);
        if (heights is not null)
        {
            writer.WriteVarInt(HeightmapType.MotionBlocking.GetHashCode());
            writer.WriteVarInt(heights.Length);

            foreach (var height in heights)
                writer.WriteLong(height);
        }

        writer.WriteVarInt(sections.Size);
        writer.Write(sections);

        if (sections.Capacity > MaxRetainedSectionScratch)
            sectionScratch = null;

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
        nbtWriter.WriteEntries(compound);

        nbtWriter.EndCompound();
        nbtWriter.TryFinish();

        writer.WriteByteArray(nbtWriter.Data);
    }
}
