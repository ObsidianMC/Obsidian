using Obsidian.ChunkData;
using Obsidian.Entities;
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

    // Like vanilla, entities of complete chunks are kept in their own region files (entities/r.x.z.mca).
    private readonly RegionFile entityRegionFile;

    // Reading the region files' headers; every file operation waits for it, since regions are loaded without waiting.
    private readonly Lazy<Task> initialization;

    private readonly ConcurrentDictionary<Vector, IBlockUpdate> blockUpdates = new();

    // Serializes filling empty chunk slots, so concurrent callers never end up with different instances of a chunk.
    private readonly SemaphoreSlim chunkSlotLock = new(1, 1);

    // Serializes saving chunks, from the snapshot through the writes, so overlapping saves of a chunk (an autosave and an
    // unload) are written in the order they were taken.
    private readonly SemaphoreSlim saveLock = new(1, 1);

    /// <summary>
    /// Locks a chunk against generation while it's serialized (the level's generator), or <c>null</c> for no locking.
    /// </summary>
    internal Func<int, int, ValueTask<IDisposable?>>? LockChunk { get; init; }

    /// <summary>
    /// Called with a complete chunk loaded from disk that has entities to spawn (the level queues them).
    /// </summary>
    internal Action<Chunk>? EntitiesLoaded { get; init; }

    /// <summary>
    /// Gives the structure starts to save in a chunk (vanilla's <c>structures.starts</c>), from the chunk's position and
    /// the starts it was loaded with; <c>null</c> means there are none. Called under the chunk's lock. Without it, the
    /// loaded starts are saved as they were.
    /// </summary>
    internal Func<int, int, NbtCompound?, NbtCompound?>? SaveStructureStarts { get; init; }

    /// <summary>
    /// The lock fluid ticks run under (the level's <see cref="Fluids.LevelFluids.TickLock"/>), held while a chunk is
    /// serialized so its blocks and fluid ticks are saved as of the same moment.
    /// </summary>
    internal Lock FluidTickLock { get; init; } = new();

    // The dimension's build range, which decides the section count of loaded chunks.
    private readonly int minY;
    private readonly int height;

    /// <param name="regionFolderName">The chunk region files' folder in <paramref name="worldFolderPath"/>: Obsidian's
    /// <c>regions</c>, or vanilla's <c>region</c>. Entities are in <c>entities</c> either way.</param>
    internal Region(int x, int z, string worldFolderPath, string regionFolderName = "regions",
        NbtCompression chunkCompression = NbtCompression.ZLib, int minY = -64, int height = 384)
    {
        X = x;
        Z = z;
        this.minY = minY;
        this.height = height;
        RegionFolder = Path.Join(worldFolderPath, regionFolderName);
        Directory.CreateDirectory(RegionFolder);
        var filePath = Path.Join(RegionFolder, $"r.{X}.{Z}.mca");

        regionFile = new RegionFile(filePath, chunkCompression, CubicRegionSize);
        ChunkCompression = chunkCompression;

        var entityFolder = Path.Join(worldFolderPath, "entities");
        Directory.CreateDirectory(entityFolder);
        this.entityRegionFile = new RegionFile(Path.Join(entityFolder, $"r.{X}.{Z}.mca"), chunkCompression, CubicRegionSize);

        this.initialization = new(() => Task.WhenAll(this.regionFile.InitializeAsync(), this.entityRegionFile.InitializeAsync()));
    }

    public void AddBlockUpdate(IBlockUpdate bu)
    {
        if (!blockUpdates.TryAdd(bu.Position, bu))
        {
            blockUpdates[bu.Position] = bu;
        }
    }

    public async Task<bool> InitAsync()
    {
        await this.initialization.Value;
        return true;
    }

    public async Task FlushAsync(CancellationToken cts = default)
    {
        await this.initialization.Value;

        foreach (Chunk c in loadedChunks.Cast<Chunk>())
            await SerializeChunkAsync(c);

        regionFile.Flush();
        this.entityRegionFile.Flush();
    }

    public async ValueTask<IChunk> GetChunkAsync(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is not null)
            return chunk;

        // A chunk the file doesn't have can't be loaded, so there's no read to serialize. The file only gains chunks that
        // were loaded, which are found above.
        await this.initialization.Value;
        if (!this.regionFile.HasChunk(x, z))
            return null!;

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

        await this.initialization.Value;
        await chunkSlotLock.WaitAsync();
        try
        {
            chunk = loadedChunks[x, z] ?? (this.regionFile.HasChunk(x, z) ? await GetChunkFromFileAsync(x, z) : null) ?? create();
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
        await SerializeChunkAsync(chunk, unloading: true);
        loadedChunks[x, z] = null;
    }

    private async Task<Chunk?> GetChunkFromFileAsync(int x, int z)
    {
        if (await ReadCompoundAsync(this.regionFile, x, z) is not NbtCompound chunkCompound)
            return null;

        var chunk = DeserializeChunk(chunkCompound);

        // Entities of complete chunks are in the entity region file; they spawn like generated ones.
        if (chunk.IsGenerated)
        {
            if (await ReadCompoundAsync(this.entityRegionFile, x, z) is NbtCompound entityChunk
                && entityChunk.TryGetTag<NbtList>("Entities", out var entities))
            {
                foreach (var entity in entities.OfType<NbtCompound>())
                {
                    if (EntityNbt.ToGeneratedEntity(entity) is GeneratedEntity pending)
                        chunk.PendingEntities.Add(pending);
                }
            }

            if (chunk.PendingEntities.Count > 0)
                this.EntitiesLoaded?.Invoke(chunk);
        }

        return chunk;
    }

    private async Task<NbtCompound?> ReadCompoundAsync(RegionFile file, int x, int z)
    {
        await this.initialization.Value;

        if (await file.GetChunkBytesAsync(x, z) is not Memory<byte> data)
            return null;

        await using var bytesStream = new ReadOnlyStream(data);
        return new NbtReader(bytesStream).ReadNextTag() as NbtCompound;
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

    /// <summary>
    /// Saves a chunk, and once it's complete, its entities to the entity region file.
    /// </summary>
    /// <param name="unloading">Whether the chunk is being unloaded: its saved entities are also taken out of the level.</param>
    internal async Task SerializeChunkAsync(IChunk chunk, bool unloading = false)
    {
        await this.initialization.Value;

        await this.saveLock.WaitAsync();
        try
        {
            await this.WriteChunkAsync(chunk, unloading);
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    private async Task WriteChunkAsync(IChunk chunk, bool unloading)
    {
        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));

        await using MemoryStream strm = new();
        await using NbtWriterStream writer = new(strm, ChunkCompression, "");

        // Generation writes chunks (and their neighbors) under its locks, so the snapshot is taken under the chunk's lock.
        // A fluid tick takes its tick from the chunk before it changes blocks, so the snapshot also waits out fluid ticks.
        // The entities are taken in the same snapshot, so they agree with the chunk's status.
        NbtList? entities = null;
        using (this.LockChunk is null ? null : await this.LockChunk(chunk.X, chunk.Z))
        {
            lock (this.FluidTickLock)
            {
                var loadedStarts = (chunk as Chunk)?.StructureStarts;
                var structureStarts = this.SaveStructureStarts is null ? loadedStarts : this.SaveStructureStarts(chunk.X, chunk.Z, loadedStarts);

                SerializeChunk(writer, chunk, structureStarts);

                if (chunk.IsGenerated && chunk is Chunk complete)
                    entities = this.CollectEntities(complete, unloading);
            }
        }

        writer.EndCompound();

        await writer.TryFinishAsync();

        await regionFile.SetChunkAsync(x, z, strm.ToArray());

        if (entities is not null)
            await this.WriteEntitiesAsync(chunk.X, chunk.Z, entities);
    }

    /// <summary>
    /// The entities to save with a complete chunk: those still waiting to spawn, and those of the level in the chunk. When
    /// unloading, the level's are taken out of it too, and the chunk's pending ones won't spawn anymore.
    /// </summary>
    /// <returns><c>null</c> once an unloading save took the entities, which a later save of the chunk mustn't overwrite.</returns>
    private NbtList? CollectEntities(Chunk chunk, bool unloading)
    {
        var entities = new NbtList(NbtTagType.Compound, "Entities");

        using (chunk.EntityLock.EnterScope())
        {
            if (chunk.EntitiesUnloaded)
                return null;

            foreach (var pending in chunk.PendingEntities)
                entities.Add(EntityNbt.ToNbt(pending));

            foreach (var entity in this.Entities.Values)
            {
                if (entity is not Entity levelEntity || ChunkOf(levelEntity.Position) != (chunk.X, chunk.Z))
                    continue;

                if (EntityNbt.Save(levelEntity) is not NbtCompound saved)
                    continue;

                entities.Add(saved);
                if (unloading)
                    this.Entities.TryRemove(levelEntity.EntityId, out _);
            }

            if (unloading)
                chunk.EntitiesUnloaded = true;
        }

        return entities;
    }

    /// <summary>
    /// Writes a chunk's entities like vanilla's <c>EntityStorage</c>: <c>DataVersion</c>, <c>Position</c> and
    /// <c>Entities</c>.
    /// </summary>
    private async Task WriteEntitiesAsync(int chunkX, int chunkZ, NbtList entities)
    {
        var (x, z) = (NumericsHelper.Modulo(chunkX, CubicRegionSize), NumericsHelper.Modulo(chunkZ, CubicRegionSize));

        // Vanilla deletes the entry of a chunk left without entities; region files can't delete entries, so a chunk that had
        // entities keeps an empty list, and one that never had any gets no entry.
        if (entities.Count == 0 && !this.entityRegionFile.HasChunk(x, z))
            return;

        await using MemoryStream strm = new();
        await using NbtWriterStream writer = new(strm, ChunkCompression, "");

        // Entities are saved in 1.21.11's format too (items with components).
        writer.WriteInt("DataVersion", VanillaLevelData.DataVersion);
        writer.WriteArray("Position", [chunkX, chunkZ]);
        writer.WriteTag(entities);
        writer.EndCompound();

        await writer.TryFinishAsync();

        await this.entityRegionFile.SetChunkAsync(x, z, strm.ToArray());
    }

    /// <summary>
    /// The chunk an entity at <paramref name="position"/> is in, which decides the region keeping it and the chunk saving it.
    /// </summary>
    /// <remarks>
    /// Unlike <c>VectorF.ToChunkCoord</c>, which truncates, this floors negative coordinates like vanilla.
    /// </remarks>
    internal static (int X, int Z) ChunkOf(VectorD position) => ((int)Math.Floor(position.X) >> 4, (int)Math.Floor(position.Z) >> 4);

    public async Task BeginTickAsync(CancellationToken cts = default)
    {
        await Parallel.ForEachAsync(Entities.Values, cts, async (entity, cts) => await entity.TickAsync());

        this.MoveEntitiesToTheirRegions();

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

    /// <summary>
    /// Hands entities that moved into another loaded region's chunks over to that region, which saves them with its chunks.
    /// </summary>
    private void MoveEntitiesToTheirRegions()
    {
        foreach (var entity in this.Entities.Values)
        {
            var (chunkX, chunkZ) = ChunkOf(entity.Position);
            if (chunkX >> CubicRegionSizeShift == this.X && chunkZ >> CubicRegionSizeShift == this.Z)
                continue;

            if (entity.Level is AbstractLevel level && level.GetRegionForChunk(chunkX, chunkZ) is Region target
                && target.Entities.TryAdd(entity.EntityId, entity))
                this.Entities.TryRemove(entity.EntityId, out _);
        }
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

            var section = chunk.Sections[sectionIndex];

            if (sectionCompound.TryGetTag<NbtCompound>("block_states", out var blockStates))
                ChunkSectionNbt.ReadBlockStates(blockStates, section.BlockStateContainer);

            // The storage was filled directly, so the section doesn't know whether it holds blocks yet.
            (section as ChunkSection)?.RecalculateEmpty();

            if (sectionCompound.TryGetTag<NbtCompound>("biomes", out var biomes))
                ChunkSectionNbt.ReadBiomes(biomes, section.BiomeContainer);

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
        var expectedLength = chunk.Heightmaps[HeightmapType.MotionBlocking].data.storage.Length;
        var heightmaps = chunkCompound.TryGetTag<NbtCompound>("Heightmaps", out var heightmapsTag) ? heightmapsTag : null;
        var heightmapsMatch = storedMinSection == minSection
            && heightmaps is not null
            && heightmaps.All(entry => entry.Value is NbtArray<long> array && array.Count == expectedLength);

        if (heightmapsMatch)
        {
            foreach (var (name, heightmap) in heightmaps!)
            {
                if (Enum.TryParse<HeightmapType>(name.Replace("_", ""), true, out var type) && chunk.Heightmaps.TryGetValue(type, out var target))
                    target.data.storage = ((NbtArray<long>)heightmap).GetArray();
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

                chunk.SetBlockEntity(position.X, position.Y, position.Z, BlockEntityNbt.Load(blockEntityCompound, id.Value!, position));
            }
        }

        if (storedMinSection == minSection)
            ReadPostProcessing(chunkCompound, chunk);

        if (chunkCompound.TryGetTag<NbtList>("fluid_ticks", out var fluidTicks))
            chunk.FluidTicks.Read(fluidTicks, x, z);

        // Entities of a chunk that isn't complete yet, like a vanilla proto chunk's "entities".
        if (chunkCompound.TryGetTag<NbtList>("entities", out var entities))
        {
            foreach (var entityCompound in entities.OfType<NbtCompound>())
            {
                if (EntityNbt.ToGeneratedEntity(entityCompound) is GeneratedEntity pending)
                    chunk.PendingEntities.Add(pending);
            }
        }

        if (chunkCompound.TryGetTag<NbtCompound>("structures", out var structures) && structures.TryGetTag<NbtCompound>("starts", out var starts))
            chunk.StructureStarts = starts;

        chunk.SetChunkStatus(ParseStatus(chunkCompound.TryGetTag<NbtTag<string>>("Status", out var status) ? status.Value : null));

        return chunk;
    }

    /// <summary>
    /// A saved chunk status: vanilla's namespaced ids (<c>minecraft:full</c>) or Obsidian's previous bare names (<c>full</c>).
    /// </summary>
    /// <remarks>
    /// A status that's missing or unknown counts as complete, so a chunk that has data is never generated again over it.
    /// </remarks>
    internal static ChunkGenStage ParseStatus(string? status)
    {
        const string Namespace = "minecraft:";

        var name = status is not null && status.StartsWith(Namespace, StringComparison.Ordinal) ? status[Namespace.Length..] : status;

        // Compared back to the name, since parsing also accepts numbers.
        return Enum.TryParse<ChunkGenStage>(name, out var stage) && stage.ToString() == name ? stage : ChunkGenStage.full;
    }

    /// <summary>
    /// Reads post-processing marks: vanilla's list per section of positions packed as <c>x | y &lt;&lt; 4 | z &lt;&lt; 8</c>
    /// within the section, or Obsidian's array packed as <c>x | z &lt;&lt; 4 | (y - min Y) &lt;&lt; 8</c> within
    /// the chunk.
    /// </summary>
    private void ReadPostProcessing(NbtCompound chunkCompound, Chunk chunk)
    {
        var (blockX, blockZ) = (chunk.X << 4, chunk.Z << 4);

        if (chunkCompound.TryGetTag<NbtArray<int>>("PostProcessing", out var ownShape))
        {
            foreach (var packed in ownShape.GetArray())
                chunk.PostProcessing.Add(new Vector(blockX + (packed & 15), this.minY + (packed >> 8), blockZ + ((packed >> 4) & 15)));

            return;
        }

        if (!chunkCompound.TryGetTag<NbtList>("PostProcessing", out var sections))
            return;

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (sections[sectionIndex] is not NbtList positions)
                continue;

            var sectionY = this.minY + (sectionIndex << 4);
            foreach (var packed in positions.OfType<NbtTag<short>>().Select(position => position.Value))
                chunk.PostProcessing.Add(new Vector(blockX + (packed & 15), sectionY + ((packed >> 4) & 15), blockZ + ((packed >> 8) & 15)));
        }
    }

    private static void SerializeChunk(NbtWriterStream writer, IChunk chunk, NbtCompound? structureStarts)
    {
        writer.WriteListStart("sections", NbtTagType.Compound, chunk.Sections.Length);

        foreach (var section in chunk.Sections)
        {
            if (section.YBase is null)
                throw new UnreachableException("Section Ybase should not be null");//THIS should never happen

            writer.WriteCompoundStart();

            // The containers are packed under their locks: live writes (fluid ticks, players) may grow a palette meanwhile.
            ChunkSectionNbt.WriteBlockStates(writer, section.BlockStateContainer);
            ChunkSectionNbt.WriteBiomes(writer, section.BiomeContainer);

            writer.WriteByte("Y", (byte)section.YBase);
            writer.WriteArray("SkyLight", section.SkyLightArray.ToArray());
            writer.WriteArray("BlockLight", section.BlockLightArray.ToArray());

            writer.EndCompound();

        }
        writer.EndList();

        // Data block entities and containers; other block entities aren't saved yet.
        var blockEntities = chunk.GetBlockEntities().Select(blockEntity => BlockEntityNbt.Save(blockEntity, chunk)).OfType<NbtCompound>().ToList();
        writer.WriteListStart("block_entities", NbtTagType.Compound, blockEntities.Count);
        foreach (var blockEntity in blockEntities)
            writer.WriteListTag(blockEntity);
        writer.EndList();

        // Entities placed by world generation in a chunk that isn't complete yet, like a vanilla proto chunk's "entities".
        // Complete chunks keep their entities in the entity region file.
        var pendingEntities = chunk is Chunk { IsGenerated: false } proto ? proto.PendingEntities : [];
        writer.WriteListStart("entities", NbtTagType.Compound, pendingEntities.Count);
        foreach (var entity in pendingEntities)
            writer.WriteListTag(EntityNbt.ToNbt(entity));
        writer.EndList();

        // Vanilla's structures.starts. References aren't saved: they follow from the starts, which are recomputed from the
        // seed, so only the starts' placement state needs saving.
        if (structureStarts is { Count: > 0 })
        {
            writer.WriteCompoundStart("structures");
            writer.WriteCompoundStart("starts");
            foreach (var (_, start) in structureStarts)
                writer.WriteTag(start);
            writer.EndCompound();
            writer.EndCompound();
        }

        if (chunk is Chunk generated)
        {
            // Post-processing marks in Obsidian's own shape: vanilla's (a list of short lists per section) can't be written
            // while Obsidian.Nbt's writer gets lists inside lists wrong. Vanilla ignores this field, and complete chunks
            // rarely have marks. Both shapes are read.
            if (generated.PostProcessing.Count > 0)
            {
                writer.WriteArray("PostProcessing", generated.PostProcessing
                    .Select(position => (position.X & 15) | (position.Z & 15) << 4 | (position.Y - chunk.MinY) << 8)
                    .ToArray());
            }

            // Scheduled fluid ticks, like vanilla's "fluid_ticks".
            generated.FluidTicks.Write(writer);
        }

        writer.WriteInt("xPos", chunk.X);
        writer.WriteInt("zPos", chunk.Z);
        writer.WriteInt("yPos", chunk.MinY >> 4);

        // Chunks are saved in 1.21.11's format, so vanilla reads them without upgrading them.
        writer.WriteInt("DataVersion", VanillaLevelData.DataVersion);
        writer.WriteString("Status", $"minecraft:{chunk.ChunkStatus}");

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

    public async ValueTask DisposeAsync()
    {
        await regionFile.DisposeAsync();
        await this.entityRegionFile.DisposeAsync();
    }
}
