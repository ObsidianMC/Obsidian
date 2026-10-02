using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Utilities.Collections;
using Obsidian.WorldData.Generators.Mojang;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public class Region : IRegion
{
    public const int CubicRegionSizeShift = 5;
    public const int CubicRegionSize = 1 << CubicRegionSizeShift;

    public int X { get; }
    public int Z { get; }

    public bool IsDirty { get; private set; } = true;

    public string RegionFolder { get; }

    public NbtCompression ChunkCompression { get; }

    public ConcurrentDictionary<int, IEntity> Entities { get; } = new();

    public int LoadedChunkCount => loadedChunks.Count(c => c.IsGenerated);

    private DenseCollection<IChunk> loadedChunks { get; } = new(CubicRegionSize, CubicRegionSize);

    private readonly RegionFile regionFile;

    private readonly ConcurrentDictionary<Vector, IBlockUpdate> blockUpdates = new();

    // Serializes filling empty chunk slots, so concurrent callers never end up with different instances of a chunk.
    private readonly SemaphoreSlim chunkSlotLock = new(1, 1);

    /// <summary>
    /// Locks a chunk against generation while it's serialized (the level's generator), or <c>null</c> for no locking.
    /// </summary>
    internal Func<int, int, ValueTask<IDisposable?>>? LockChunk { get; init; }

    // The dimension's build range, which decides the section count of loaded chunks.
    private readonly int minY;
    private readonly int height;

    internal Region(int x, int z, string worldFolderPath, NbtCompression chunkCompression = NbtCompression.ZLib,
        ILogger? logger = null, int minY = -64, int height = 384)
    {
        X = x;
        Z = z;
        this.minY = minY;
        this.height = height;
        RegionFolder = Path.Join(worldFolderPath, "regions");
        Directory.CreateDirectory(RegionFolder);
        var filePath = Path.Join(RegionFolder, $"r.{X}.{Z}.mca");

        logger?.LogInformation("Loading region file {RegionFile} with compression {Compression}", filePath, chunkCompression);
        regionFile = new RegionFile(filePath, chunkCompression, CubicRegionSize, logger);
        ChunkCompression = chunkCompression;
    }

    public void AddBlockUpdate(IBlockUpdate bu)
    {
        if (!blockUpdates.TryAdd(bu.Position, bu))
        {
            blockUpdates[bu.Position] = bu;
        }
    }

    public async Task<bool> InitAsync() => await regionFile.InitializeAsync();

    public async Task FlushAsync(CancellationToken cts = default)
    {
        foreach (Chunk c in loadedChunks.Cast<Chunk>())
            await SerializeChunkAsync(c);

        regionFile.Flush();
    }

    public async ValueTask<IChunk> GetChunkAsync(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is not null)
            return chunk;

        await chunkSlotLock.WaitAsync();
        try
        {
            chunk = loadedChunks[x, z] ?? await GetChunkFromFileAsync(x, z); // Still might be null but that's okay.
            loadedChunks[x, z] = chunk!;
            return chunk!;
        }
        finally
        {
            chunkSlotLock.Release();
        }
    }

    /// <summary>
    /// Gets the chunk at (<paramref name="x"/>, <paramref name="z"/>) in this region, loading it from disk or storing the
    /// one <paramref name="create"/> makes when there's none. Every caller gets the same instance.
    /// </summary>
    public async ValueTask<IChunk> GetOrAddChunkAsync(int x, int z, Func<IChunk> create)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is not null)
            return chunk;

        await chunkSlotLock.WaitAsync();
        try
        {
            chunk = loadedChunks[x, z] ?? await GetChunkFromFileAsync(x, z) ?? create();
            loadedChunks[x, z] = chunk;
            return chunk;
        }
        finally
        {
            chunkSlotLock.Release();
        }
    }

    public async Task UnloadChunk(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is null) { return; }
        await SerializeChunkAsync(chunk);
        loadedChunks[x, z] = null;
    }

    private async Task<Chunk?> GetChunkFromFileAsync(int x, int z)
    {
        var chunkBuffer = await regionFile.GetChunkBytesAsync(x, z);

        if (chunkBuffer is not Memory<byte> chunkData)
            return null;

        await using var bytesStream = new ReadOnlyStream(chunkData);
        var nbtReader = new NbtReader(bytesStream);

        return DeserializeChunk(nbtReader.ReadNextTag() as NbtCompound);
    }

    public IEnumerable<IChunk> GeneratedChunks()
    {
        foreach (var c in loadedChunks)
        {
            if (c is not null && c.IsGenerated)
            {
                yield return c;
            }
        }
    }

    public void SetChunk(IChunk chunk)
    {
        if (chunk is null) { return; } // I dunno... maybe we'll need to null out a chunk someday?
        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));
        loadedChunks[x, z] = chunk;
    }

    internal async Task SerializeChunkAsync(IChunk chunk)
    {
        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));

        await using MemoryStream strm = new();
        await using NbtWriterStream writer = new(strm, ChunkCompression, "");

        // Generation writes chunks (and their neighbors) under its locks, so the snapshot is taken under the chunk's lock.
        using (this.LockChunk is null ? null : await this.LockChunk(chunk.X, chunk.Z))
            SerializeChunk(writer, chunk);

        writer.EndCompound();

        await writer.TryFinishAsync();

        await regionFile.SetChunkAsync(x, z, strm.ToArray());
    }

    public async Task BeginTickAsync(CancellationToken cts = default)
    {
        await Parallel.ForEachAsync(Entities.Values, cts, async (entity, cts) => await entity.TickAsync());

        List<IBlockUpdate> neighborUpdates = [];
        List<IBlockUpdate> delayed = [];

        foreach (var pos in blockUpdates.Keys)
        {
            blockUpdates.Remove(pos, out var bu);
            if (bu.DelayCounter > 0)
            {
                bu.DelayCounter--;
                delayed.Add(bu);
            }
            else
            {
                bool updateNeighbor = await bu.Level.HandleBlockUpdateAsync(bu);
                if (updateNeighbor) { neighborUpdates.Add(bu); }
            }
        }
        delayed.ForEach(AddBlockUpdate);
        neighborUpdates.ForEach(async u => await u.Level.BlockUpdateNeighborsAsync(u));
    }

    #region NBT Ops
    private Chunk DeserializeChunk(NbtCompound chunkCompound)
    {
        int x = chunkCompound.GetInt("xPos");
        int z = chunkCompound.GetInt("zPos");

        var chunk = new Chunk(x, z, this.minY, this.height);

        // Chunks saved before build ranges were per dimension used the overworld's (min section -4) everywhere.
        var storedMinSection = chunkCompound.TryGetTag<NbtTag<int>>("yPos", out var yPos) ? yPos.Value : -4;
        var minSection = this.minY >> 4;

        foreach (var child in (NbtList)chunkCompound["sections"])
        {
            if (child is not NbtCompound sectionCompound)
                throw new InvalidOperationException("Nbt Tag is not a compound.");

            var secY = unchecked((sbyte)sectionCompound.GetByte("Y"));

            // Sections outside the dimension's build range (from an older, taller layout) are dropped.
            var sectionIndex = secY - minSection;
            if (sectionIndex < 0 || sectionIndex >= chunk.Sections.Length)
                continue;

            if (!sectionCompound.TryGetTag("block_states", out var statesTag))
                throw new UnreachableException("Unable to find block states from NBT.");

            var statesCompound = statesTag as NbtCompound;

            var section = chunk.Sections[sectionIndex];

            if (statesCompound!.TryGetTag("palette", out var palleteArrayTag))
            {
                var blockStatesPalette = palleteArrayTag as NbtList;

                foreach (var entry in blockStatesPalette!.Cast<NbtCompound>())
                {
                    var id = entry.GetInt("Id");
                    var block = BlocksRegistry.Get(id);
                    section.BlockStateContainer.Add(block);//TODO PROCESS ADDED PROPERTIES TO GET CORRECT BLOCK STATE
                }

                if (section.BlockStateContainer.Palette.Count == 1 && !section.BlockStateContainer.IsSingleValued)
                    throw new UnreachableException("Chunk palette has only one entry but chunk container is not single valued.");
            }

            if (statesCompound.TryGetTag("data", out var dataArrayTag))
            {
                var data = dataArrayTag as NbtArray<long>;
                section.BlockStateContainer.DataArray.storage = data!.GetArray();
            }

            // The storage was filled directly, so the section doesn't know whether it holds blocks yet.
            (section as ChunkSection)?.RecalculateEmpty();

            if (sectionCompound.TryGetTag<NbtCompound>("biomes", out var biomesCompound))
            {
                if (biomesCompound.TryGetTag<NbtList>("palette", out var biomesPalette))
                {
                    foreach (NbtTag<string> biome in biomesPalette!.Cast<NbtTag<string>>())
                    {
                        if (CodecRegistry.TryGetBiome(biome.Value, out var value))
                        {
                            section.BiomeContainer.Add(value);
                        }
                    }

                    if(section.BiomeContainer.Palette.Count == 1 && !section.BiomeContainer.IsSingleValued)
                        throw new UnreachableException("Biome palette has only one entry but biome container is not single valued.");
                }

                if (biomesCompound.TryGetTag<NbtArray<long>>("data", out var data))
                {
                    section.BiomeContainer.DataArray.storage = data!.GetArray();
                }
            }

            if (sectionCompound.TryGetTag("SkyLight", out var skyLightTag))
            {
                var array = (NbtArray<byte>)skyLightTag;

                section.SetLight(array.GetArray(), LightType.Sky);
            }

            if (sectionCompound.TryGetTag("BlockLight", out var blockLightTag))
            {
                var array = (NbtArray<byte>)blockLightTag;

                section.SetLight(array.GetArray(), LightType.Block);
            }
        }

        // Stored heights are relative to the stored min Y and packed for the stored height, so they're only kept when both
        // match; otherwise they're recomputed from the blocks.
        var heightmaps = (NbtCompound)chunkCompound["Heightmaps"];
        var expectedLength = chunk.Heightmaps[HeightmapType.MotionBlocking].data.storage.Length;
        var heightmapsMatch = storedMinSection == minSection
            && heightmaps.All(entry => ((NbtArray<long>)entry.Value).Count == expectedLength);

        if (heightmapsMatch)
        {
            foreach (var (name, heightmap) in heightmaps)
            {
                var heightmapType = (HeightmapType)Enum.Parse(typeof(HeightmapType), name.Replace("_", ""), true);
                chunk.Heightmaps[heightmapType].data.storage = ((NbtArray<long>)heightmap).GetArray();
            }
        }
        else
        {
            WorldgenHeightmaps.Update(chunk, this.minY, this.height);
            WorldgenHeightmaps.UpdateFinal(chunk, this.minY, this.height);
        }

        if (chunkCompound.TryGetTag<NbtList>("block_entities", out var blockEntities))
        {
            foreach (var blockEntityCompound in blockEntities.Cast<NbtCompound>())
            {
                if (!blockEntityCompound.TryGetTag<NbtTag<string>>("id", out var id))
                    continue;

                var position = new Vector(blockEntityCompound.GetInt("x"), blockEntityCompound.GetInt("y"), blockEntityCompound.GetInt("z"));
                if (position.Y < this.minY || position.Y >= this.minY + this.height)
                    continue;

                var data = new NbtCompound();
                foreach (var (name, tag) in blockEntityCompound)
                {
                    if (name is not ("id" or "x" or "y" or "z" or "keepPacked"))
                        data.Add(name, tag);
                }

                chunk.SetBlockEntity(position.X, position.Y, position.Z, new DataBlockEntity { Id = id.Value!, BlockPosition = position, Data = data });
            }
        }

        if (chunkCompound.TryGetTag<NbtArray<int>>("PostProcessing", out var postProcessing) && storedMinSection == minSection)
        {
            foreach (var packed in postProcessing.GetArray())
                chunk.PostProcessing.Add(new Vector((x << 4) + (packed & 15), this.minY + (packed >> 8), (z << 4) + ((packed >> 4) & 15)));
        }

        if (chunkCompound.TryGetTag<NbtList>("entities", out var entities))
        {
            foreach (var entityCompound in entities.Cast<NbtCompound>())
            {
                if (!entityCompound.TryGetTag<NbtTag<string>>("id", out var id) || !entityCompound.TryGetTag<NbtList>("Pos", out var pos))
                    continue;

                var yaw = 0f;
                var pitch = 0f;
                if (entityCompound.TryGetTag<NbtList>("Rotation", out var rotation))
                {
                    yaw = ((NbtTag<float>)rotation[0]).Value;
                    pitch = ((NbtTag<float>)rotation[1]).Value;
                }

                var data = new NbtCompound();
                foreach (var (name, tag) in entityCompound)
                {
                    if (name is not ("id" or "Pos" or "Rotation"))
                        data.Add(name, tag);
                }

                var position = new VectorF((float)((NbtTag<double>)pos[0]).Value, (float)((NbtTag<double>)pos[1]).Value,
                    (float)((NbtTag<double>)pos[2]).Value);
                chunk.PendingEntities.Add(new GeneratedEntity(id.Value!, position, yaw, pitch) { Data = data });
            }
        }

        chunk.SetChunkStatus((ChunkGenStage)(Enum.TryParse(typeof(ChunkGenStage), chunkCompound.GetString("Status"), out var status) ? status : ChunkGenStage.empty));

        return chunk;
    }

    private static void SerializeChunk(NbtWriterStream writer, IChunk chunk)
    {
        writer.WriteListStart("sections", NbtTagType.Compound, chunk.Sections.Length);

        foreach (var section in chunk.Sections)
        {
            if (section.YBase is null)
                throw new UnreachableException("Section Ybase should not be null");//THIS should never happen

            writer.WriteCompoundStart();

            writer.WriteCompoundStart("block_states");

            if (section.BlockStateContainer.Palette is IndirectBlockPalette indirect)
            {
                writer.WriteListStart("palette", NbtTagType.Compound, indirect.Count);

                Span<int> span = indirect.Values;
                for (int i = 0; i < indirect.Count; i++)
                {
                    var id = span[i];
                    var block = BlocksRegistry.Get(id);

                    writer.WriteCompoundStart();

                    writer.WriteString("Name", block.UnlocalizedName);
                    writer.WriteInt("Id", id);

                    writer.EndCompound();//TODO INCLUDE PROPERTIES
                }

                writer.EndList();

                writer.WriteArray("data", section.BlockStateContainer.DataArray.storage);
            }
            else if (section.BlockStateContainer.Palette is SingleValuePalette<IBlock> singleValueBlockPalette && singleValueBlockPalette.IsFull)
            {
                writer.WriteListStart("palette", NbtTagType.Compound, 1);

                var block = singleValueBlockPalette.GetValueFromIndex(0);

                writer.WriteCompoundStart();

                writer.WriteString("Name", block.UnlocalizedName);
                writer.WriteInt("Id", block.GetHashCode());

                writer.EndCompound();//TODO INCLUDE PROPERTIES

                writer.EndList();
            }

            writer.EndCompound();

            if (section.BiomeContainer.Palette.Count >= 1)
            {
                writer.WriteCompoundStart("biomes");

                if (section.BiomeContainer.Palette is BaseIndirectPalette<BiomeCodec> indirectBiomePalette)
                {
                    writer.WriteListStart("palette", NbtTagType.String, indirectBiomePalette.Count);

                    Span<int> span = indirectBiomePalette.Values;
                    for (int i = 0; i < indirectBiomePalette.Count; i++)
                    {
                        var biome = CodecRegistry.GetBiome(span[i]);
                        writer.WriteString(biome?.Name);
                    }

                    writer.EndList();

                    writer.WriteArray("data", section.BiomeContainer.DataArray.storage);
                }
                else if (section.BiomeContainer.Palette is SingleValuePalette<BiomeCodec> singleValueBiomePalette && singleValueBiomePalette.IsFull)
                {
                    writer.WriteListStart("palette", NbtTagType.String, 1);

                    var biome = singleValueBiomePalette.GetValueFromIndex(0);

                    writer.WriteString(biome.Name);

                    writer.EndList();
                }

                writer.EndCompound();
            }

            writer.WriteByte("Y", (byte)section.YBase);
            writer.WriteArray("SkyLight", section.SkyLightArray.ToArray());
            writer.WriteArray("BlockLight", section.BlockLightArray.ToArray());

            writer.EndCompound();

        }
        writer.EndList();

        // Only block entities kept as data are saved; container block entities aren't persisted yet.
        var blockEntities = chunk.GetBlockEntities().OfType<DataBlockEntity>().ToList();
        writer.WriteListStart("block_entities", NbtTagType.Compound, blockEntities.Count);
        foreach (var blockEntity in blockEntities)
        {
            writer.WriteCompoundStart();
            writer.WriteString("id", blockEntity.Id);
            writer.WriteInt("x", blockEntity.BlockPosition.X);
            writer.WriteInt("y", blockEntity.BlockPosition.Y);
            writer.WriteInt("z", blockEntity.BlockPosition.Z);
            writer.WriteBool("keepPacked", false);
            foreach (var (_, tag) in blockEntity.Data)
                writer.WriteTag(tag);
            writer.EndCompound();
        }
        writer.EndList();

        // Entities placed by world generation that haven't spawned yet, like a vanilla proto chunk's "entities".
        var pendingEntities = (chunk as Chunk)?.PendingEntities ?? [];
        writer.WriteListStart("entities", NbtTagType.Compound, pendingEntities.Count);
        foreach (var entity in pendingEntities)
        {
            writer.WriteCompoundStart();
            writer.WriteString("id", entity.Type);
            writer.WriteListStart("Pos", NbtTagType.Double, 3);
            writer.WriteDouble(entity.Position.X);
            writer.WriteDouble(entity.Position.Y);
            writer.WriteDouble(entity.Position.Z);
            writer.EndList();
            writer.WriteListStart("Rotation", NbtTagType.Float, 2);
            writer.WriteFloat(entity.Yaw);
            writer.WriteFloat(entity.Pitch);
            writer.EndList();
            foreach (var (_, tag) in entity.Data)
                writer.WriteTag(tag);
            writer.EndCompound();
        }
        writer.EndList();

        // Post-processing marks, each packed as (x | z << 4 | (y - min Y) << 8) within the chunk.
        if (chunk is Chunk generated)
        {
            writer.WriteArray("PostProcessing", generated.PostProcessing
                .Select(position => (position.X & 15) | (position.Z & 15) << 4 | (position.Y - chunk.MinY) << 8)
                .ToArray());
        }

        writer.WriteInt("xPos", chunk.X);
        writer.WriteInt("zPos", chunk.Z);
        writer.WriteInt("yPos", chunk.MinY >> 4);
        writer.WriteInt("DataVersion", 3337);
        writer.WriteString("Status", chunk.ChunkStatus.ToString());

        // Every heightmap the chunk still has. Chunks that aren't fully generated keep their world generation heightmaps,
        // which later generation steps read.
        writer.WriteCompoundStart("Heightmaps");
        foreach (var (type, heightmap) in chunk.Heightmaps)
            writer.WriteArray(HeightmapName(type), heightmap.data.storage);
        writer.EndCompound();
    }

    // Vanilla's heightmap names; loading strips the underscores to parse them back.
    private static string HeightmapName(HeightmapType type) => type switch
    {
        HeightmapType.WorldSurfaceWG => "WORLD_SURFACE_WG",
        HeightmapType.WorldSurface => "WORLD_SURFACE",
        HeightmapType.OceanFloorWG => "OCEAN_FLOOR_WG",
        HeightmapType.OceanFloor => "OCEAN_FLOOR",
        HeightmapType.MotionBlocking => "MOTION_BLOCKING",
        HeightmapType.MotionBlockingNoLeaves => "MOTION_BLOCKING_NO_LEAVES",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    #endregion NBT Ops

    public async ValueTask DisposeAsync() => await regionFile.DisposeAsync();
}
