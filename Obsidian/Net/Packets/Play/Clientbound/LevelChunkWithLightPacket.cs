using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.ChunkData;
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

    // The heightmaps vanilla sends clients (those whose Heightmap.Types usage is CLIENT). Vanilla sends them as a
    // hash map, in no particular order, so this order is Obsidian's.
    private static readonly HeightmapType[] ClientHeightmaps =
        [HeightmapType.WorldSurface, HeightmapType.MotionBlocking, HeightmapType.MotionBlockingNoLeaves];

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

        // Clients read every section of the level's height, like vanilla writes them.
        foreach (var section in Chunk.Sections)
        {
            if (section.BlockStateContainer.IsEmpty)
            {
                WriteUnsetSection(sections, section.BiomeContainer);
                continue;
            }

            section.BlockStateContainer.WriteTo(sections);
            section.BiomeContainer.WriteTo(sections);
        }

        // Like vanilla's, the heightmaps clients use, those the chunk has.
        var heightmaps = new List<(HeightmapType Type, long[] Data)>(ClientHeightmaps.Length);
        foreach (var type in ClientHeightmaps)
        {
            if (Chunk.Heightmaps.TryGetValue(type, out var heightmap))
                heightmaps.Add((type, heightmap.GetDataArray()));
        }

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
                + heightmaps.Sum(heightmap => sizeof(long) * heightmap.Data.Length)
                + sections.Size
                + BlockEntitySize * blockEntities.Count
                + this.light.MaxLightSize());
        }

        writer.WriteInt(Chunk.X);
        writer.WriteInt(Chunk.Z);

        writer.WriteVarInt(heightmaps.Count);
        foreach (var (type, data) in heightmaps)
        {
            // Vanilla's Heightmap.Types ids, which HeightmapType's values are.
            writer.WriteVarInt((int)type);
            writer.WriteVarInt(data.Length);

            foreach (var height in data)
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
    /// Writes a section no block was set in as air, like vanilla's empty sections, in its biomes, or vanilla's default
    /// biome, plains, where none were set either.
    /// </summary>
    private static void WriteUnsetSection(NetworkBuffer writer, DataContainer<BiomeCodec> biomes)
    {
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteByte(0);
        writer.WriteVarInt(BlocksRegistry.Air.GetHashCode());

        if (biomes.Palette is SingleValuePalette<BiomeCodec> { IsFull: false })
        {
            writer.WriteByte(0);
            writer.WriteVarInt(CodecRegistry.Biomes.Plains.Id);
            return;
        }

        biomes.WriteTo(writer);
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
