using Obsidian.API.Registry.Codecs.Biomes;
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

    internal void TickInhabitedTime(AbstractLevel level)
    {
        foreach (var chunk in loadedChunks.OfType<Chunk>())
        {
            if (level.IsMobTicking(new VectorF(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8)))
                chunk.InhabitedTime++;
        }
    }

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
    /// Called with a complete chunk loaded from disk for entity and egg registration.
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

    internal Region(int x, int z, string worldFolderPath, NbtCompression chunkCompression = NbtCompression.ZLib,
        int minY = -64, int height = 384)
    {
        X = x;
        Z = z;
        this.minY = minY;
        this.height = height;
        RegionFolder = Path.Join(worldFolderPath, "regions");
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

    internal IChunk? GetLoadedChunk(int x, int z) => loadedChunks[x, z] is { IsGenerated: true } chunk ? chunk : null;

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
                    if (entity.TryGetTagValue<string>("id", out var id) && EntityNbt.TryParseType(id, out _) &&
                        EntityNbt.ToGeneratedEntity(entity) is GeneratedEntity pending)
                        chunk.PendingEntities.Add(pending);
                    else
                        chunk.UnspawnableEntities.Add(entity);
                }
            }

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
        await this.initialization.Value;

        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));

        await using MemoryStream strm = new();
        NbtList? entities = null;
        await using (NbtWriterStream writer = new(strm, ChunkCompression, ""))
        {
            // Snapshot blocks, fluid ticks and entities together; take the generator's lock before the fluid lock.
            using (this.LockChunk is null ? null : await this.LockChunk(chunk.X, chunk.Z))
            lock (this.FluidTickLock)
            {
                var loadedStarts = (chunk as Chunk)?.StructureStarts;
                var structureStarts = this.SaveStructureStarts is null ? loadedStarts : this.SaveStructureStarts(chunk.X, chunk.Z, loadedStarts);

                SerializeChunk(writer, chunk, structureStarts);

                if (chunk.IsGenerated && chunk is Chunk complete)
                    entities = this.CollectEntities(complete, unloading);
            }

            writer.EndCompound();

            await writer.TryFinishAsync();
        }

        // Disposing the writer finishes the compression trailer before copying the bytes.
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

            foreach (var unsupported in chunk.UnspawnableEntities)
                entities.Add(unsupported);

            foreach (var entity in this.Entities.Values)
            {
                if (entity is not Entity levelEntity || ChunkOf(levelEntity.Position) != (chunk.X, chunk.Z))
                    continue;

                if (EntityNbt.Save(levelEntity) is not NbtCompound saved)
                {
                    if (unloading && levelEntity is Mob { HasAi: true })
                        this.Entities.TryRemove(levelEntity.EntityId, out _);
                    continue;
                }

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
        await using (NbtWriterStream writer = new(strm, ChunkCompression, ""))
        {
            writer.WriteInt("DataVersion", LevelData.DataVersion);
            writer.WriteArray("Position", [chunkX, chunkZ]);
            writer.WriteTag(entities);
            writer.EndCompound();

            await writer.TryFinishAsync();
        }

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
        foreach (var entity in Entities.Values.OrderBy(entity => entity.EntityId).ToArray())
        {
            cts.ThrowIfCancellationRequested();
            await entity.TickAsync();
        }
        await TickBlocksAsync();
    }

    internal async Task TickBlocksAsync()
    {
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
        foreach (var update in neighborUpdates)
            await update.Level.BlockUpdateNeighborsAsync(update);
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
        if (chunkCompound.TryGetTag<NbtList>("ObsidianMobEggTicks", out var mobEggTicks))
            foreach (NbtCompound tick in mobEggTicks)
                chunk.MobEggTicks[new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z"))] = Math.Clamp(tick.GetInt("delay"), 0, 8300);

        if (chunkCompound.TryGetTag<NbtList>("ObsidianFrogspawnTicks", out var frogspawnTicks))
            foreach (var tick in frogspawnTicks.OfType<NbtCompound>())
                chunk.FrogspawnTicks[new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z"))] = Math.Clamp(tick.GetInt("delay"), 1, 12000);
        if (chunkCompound.TryGetTagValue<long>("InhabitedTime", out var inhabitedTime))
            chunk.InhabitedTime = inhabitedTime;

        // Older chunks used the overworld's minimum section in every dimension.
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

                chunk.SetBlockEntity(position.X, position.Y, position.Z, BlockEntityNbt.Load(blockEntityCompound, id.Value!, position));
            }
        }

        if (chunkCompound.TryGetTag<NbtArray<int>>("PostProcessing", out var postProcessing) && storedMinSection == minSection)
        {
            foreach (var packed in postProcessing.GetArray())
                chunk.PostProcessing.Add(new Vector((x << 4) + (packed & 15), this.minY + (packed >> 8), (z << 4) + ((packed >> 4) & 15)));
        }

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

        chunk.SetChunkStatus((ChunkGenStage)(Enum.TryParse(typeof(ChunkGenStage), chunkCompound.GetString("Status"), out var status) ? status : ChunkGenStage.empty));

        return chunk;
    }

    /// <summary>
    /// Writes a chunk's compound as region files store it, without its end tag.
    /// </summary>
    internal static void SerializeChunk(NbtWriterStream writer, IChunk chunk, NbtCompound? structureStarts)
    {
        writer.WriteLong("InhabitedTime", chunk is Chunk concrete ? concrete.InhabitedTime : 0);
        writer.WriteListStart("sections", NbtTagType.Compound, chunk.Sections.Length);

        foreach (var section in chunk.Sections)
        {
            if (section.YBase is null)
                throw new UnreachableException("Section Ybase should not be null");//THIS should never happen

            writer.WriteCompoundStart();

            // The containers are locked while they're written: live writes (fluid ticks, players) may grow a palette meanwhile.
            using (section.BlockStateContainer.EnterScope())
            {
                writer.WriteCompoundStart("block_states");

                if (section.BlockStateContainer.Palette is IndirectBlockPalette indirect)
                {
                    writer.WriteListStart("palette", NbtTagType.Compound, indirect.Count);

                    ReadOnlySpan<int> span = indirect.Values;
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
            }

            using (section.BiomeContainer.EnterScope())
            {
                if (section.BiomeContainer.Palette.Count >= 1)
                {
                    writer.WriteCompoundStart("biomes");

                    if (section.BiomeContainer.Palette is BaseIndirectPalette<BiomeCodec> indirectBiomePalette)
                    {
                        writer.WriteListStart("palette", NbtTagType.String, indirectBiomePalette.Count);

                        ReadOnlySpan<int> span = indirectBiomePalette.Values;
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
            }

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

        // Post-processing marks, each packed as (x | z << 4 | (y - min Y) << 8) within the chunk.
        if (chunk is Chunk generated)
        {
            writer.WriteArray("PostProcessing", generated.PostProcessing
                .Select(position => (position.X & 15) | (position.Z & 15) << 4 | (position.Y - chunk.MinY) << 8)
                .ToArray());

            // Scheduled fluid ticks, like vanilla's "fluid_ticks".
            generated.FluidTicks.Write(writer);
            var mobEggTicks = generated.MobEggTicks.ToArray();
            writer.WriteListStart("ObsidianMobEggTicks", NbtTagType.Compound, mobEggTicks.Length);
            foreach (var (position, delay) in mobEggTicks)
            {
                writer.WriteCompoundStart();
                writer.WriteInt("x", position.X);
                writer.WriteInt("y", position.Y);
                writer.WriteInt("z", position.Z);
                writer.WriteInt("delay", delay);
                writer.EndCompound();
            }
            writer.EndList();
            var frogspawnTicks = generated.FrogspawnTicks.ToArray();
            writer.WriteListStart("ObsidianFrogspawnTicks", NbtTagType.Compound, frogspawnTicks.Length);
            foreach (var (position, delay) in frogspawnTicks)
            {
                writer.WriteCompoundStart();
                writer.WriteInt("x", position.X);
                writer.WriteInt("y", position.Y);
                writer.WriteInt("z", position.Z);
                writer.WriteInt("delay", delay);
                writer.EndCompound();
            }
            writer.EndList();
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

    public async ValueTask DisposeAsync()
    {
        await regionFile.DisposeAsync();
        await this.entityRegionFile.DisposeAsync();
    }
}
