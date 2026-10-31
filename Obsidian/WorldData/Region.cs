using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Utilities.Collections;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators.Mojang;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Obsidian.WorldData;

public partial class Region : IRegion
{
    public const int CubicRegionSizeShift = 5;
    public const int CubicRegionSize = 1 << CubicRegionSizeShift;

    public int X { get; }
    public int Z { get; }

    public bool IsDirty { get; private set; } = true;

    public string RegionFolder { get; }

    public NbtCompression ChunkCompression { get; }

    private readonly ConcurrentDictionary<int, IEntity> entities = new();

    public IReadOnlyDictionary<int, IEntity> Entities => this.entities;

    /// <summary>
    /// Adds the entity to the region's entities. Only the region's level calls this: it also keeps the entity in its
    /// <see cref="EntityGrid"/>, which a move between regions of the level leaves alone.
    /// </summary>
    internal bool TryAddEntity(IEntity entity) => this.entities.TryAdd(entity.EntityId, entity);

    /// <summary>
    /// Removes this instance of the entity from the region's entities (not another with its id).
    /// </summary>
    internal bool TryRemoveEntity(IEntity entity) =>
        this.entities.TryRemove(new KeyValuePair<int, IEntity>(entity.EntityId, entity));

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

    // Serializes filling empty chunk slots, so concurrent callers never end up with different instances of a chunk.
    private readonly SemaphoreSlim chunkSlotLock = new(1, 1);

    // The saves admitted and not yet committed, by admission order, from before their snapshot (see AdmitSave).
    private readonly Dictionary<long, TaskCompletionSource> saves = [];
    private long lastSave;

    // Set as the region closes: no save is admitted after.
    private bool closing;

    // One flush of the region at a time, so the saves of two flushes never wait for each other's commits.
    private readonly SemaphoreSlim flushLock = new(1, 1);

    /// <summary>
    /// Bounds the chunks a flush has snapshotted but not yet staged, across regions: each holds its uncompressed NBT
    /// until compressed, and compressing more at once than there are cores gains nothing.
    /// </summary>
    private static readonly SemaphoreSlim flushSlots = new(Math.Max(2, Environment.ProcessorCount));

    // What a chunk's NBT is serialized into at first; a chunk of a few sections takes about this much.
    private const int UncompressedChunkCapacity = 64 * 1024;

    /// <summary>
    /// Where the region reports damage found in its files, such as chunks sharing sectors.
    /// </summary>
    internal ILogger? Logger { get; init; }

    /// <summary>
    /// The chunk region file, for tests that watch or pause its writes (see <see cref="RegionFile.Observe"/>).
    /// </summary>
    internal RegionFile ChunkFile => this.regionFile;

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

    // Runtime light resolves under FluidTickLock before a save takes its block and light snapshot.
    internal Action? FlushLight { get; init; }

    // Invoked under FluidTickLock before publishing a slot or completing its still-stored proto chunk.
    internal Action<IChunk, bool>? LightLoaded { get; init; }

    // Chunk unload detaches at its final snapshot; region removal flushes before detaching and saving its chunks.
    internal Action<IChunk>? LightUnloaded { get; init; }

    // Tick containers and block access retire at the final save snapshot, before asynchronous compression and I/O.
    internal Action<IChunk>? BlockAccessUnloaded { get; init; }

    /// <summary>
    /// Tells a block entity its chunk unloaded (the level's <see cref="AbstractLevel.OnBlockEntityUnloaded"/>), after
    /// the chunk's final save, holding no lock.
    /// </summary>
    internal Action<BlockEntity>? BlockEntityUnloaded { get; init; }
    private readonly HashSet<IChunk> retiredBlockAccess = [];

    // A removed region cannot publish or attach chunks, including loads and generation already in flight.
    private bool lightingRetired;

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

        this.initialization = new(this.InitializeFilesAsync);
    }

    private async Task InitializeFilesAsync()
    {
        await Task.WhenAll(this.regionFile.InitializeAsync(), this.entityRegionFile.InitializeAsync());

        if (this.Logger is null)
            return;

        if (this.regionFile.OverlappingChunks != 0)
            Log.OverlappingChunks(this.Logger, this.regionFile.OverlappingChunks, "chunk", this.X, this.Z);

        if (this.entityRegionFile.OverlappingChunks != 0)
            Log.OverlappingChunks(this.Logger, this.entityRegionFile.OverlappingChunks, "entity", this.X, this.Z);
    }

    public async Task<bool> InitAsync()
    {
        await this.initialization.Value;
        return true;
    }

    public async Task FlushAsync(CancellationToken cts = default)
    {
        await this.WriteChangesAsync();
        await this.SyncToDiskAsync();
    }

    /// <summary>
    /// Writes the chunks that changed since their last save to the region's files, without waiting for the disk (see
    /// <see cref="SyncToDiskAsync"/>).
    /// </summary>
    internal async Task WriteChangesAsync()
    {
        await this.initialization.Value;

        var admitted = this.AdmitSave();
        var taken = new List<ChunkSave>();
        try
        {
            await this.flushLock.WaitAsync();
            try
            {
                await this.WriteChangesUnderLockAsync(taken);
            }
            finally
            {
                this.flushLock.Release();
            }
        }
        finally
        {
            // After the save ends: handlers of the closing windows may flush this region again, which waits for the
            // saves admitted before it.
            this.EndSave(admitted);

            foreach (var save in taken)
            {
                save.Dispose();
                await this.ReleaseRetiredAsync(save);
            }
        }

        // Saves admitted before this flush (unloads) are committed too, so syncing after it reaches the disk with
        // them. Their failures are their callers'.
        await this.SavesAdmittedBeforeAsync(admitted).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task WriteChangesUnderLockAsync(List<ChunkSave> taken)
    {
        // Like vanilla, only chunks that changed since their last save are written, and only chunks with entities, or
        // that had some, write their entities. A removed region's final save still retires every chunk. Pending light
        // goes in first, so a chunk it reaches counts as changed.
        lock (this.FluidTickLock)
            this.FlushLight?.Invoke();

        // Snapshots are taken one at a time, in order, each holding the tick lock only while the chunk is serialized;
        // compressing and staging them runs in parallel, while the next ones are taken. They're committed together,
        // for two flushes per file however many chunks changed.
        var entityChunks = this.ChunksWithEntities();
        var staging = new List<Task>();
        Task completing = Task.CompletedTask;
        try
        {
            foreach (Chunk c in loadedChunks.Cast<Chunk>())
            {
                var entities = this.MayHaveEntitiesToSave(c, entityChunks);
                if (!this.lightingRetired && !entities && !c.IsUnsaved)
                    continue;

                await flushSlots.WaitAsync();
                var save = NewSave(c);
                taken.Add(save);
                try
                {
                    await this.SnapshotChunkAsync(save, unloading: this.lightingRetired, saveEntities: entities);
                }
                catch
                {
                    flushSlots.Release();
                    throw;
                }

                staging.Add(Task.Run(async () =>
                {
                    try
                    {
                        await this.StageSaveAsync(save);
                    }
                    finally
                    {
                        flushSlots.Release();
                    }
                }));
            }
        }
        finally
        {
            // Every save taken is staged and committed even when a later snapshot fails, whose failure is then the
            // one reported. Commit failures reach each save's writes.
            await Task.WhenAll(staging).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await this.CommitAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            completing = Task.WhenAll(taken.Select(this.CompleteSaveAsync));
            await completing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await Task.WhenAll(staging);
        await completing;
    }

    /// <summary>
    /// Waits for what was written to the region's files to reach the disk.
    /// </summary>
    internal Task SyncToDiskAsync() => Task.WhenAll(this.regionFile.FlushAsync(), this.entityRegionFile.FlushAsync());

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
            Chunk? fromDisk = null;
            chunk = loadedChunks[x, z];
            if (chunk is null)
            {
                fromDisk = await GetChunkFromFileAsync(x, z);
                chunk = fromDisk;
            }

            lock (this.FluidTickLock)
            {
                chunk = loadedChunks[x, z] ?? chunk;
                if (chunk is not null)
                    this.PublishChunk(x, z, chunk, ReferenceEquals(chunk, fromDisk));

                return chunk!;
            }
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
            Chunk? fromDisk = null;
            chunk = loadedChunks[x, z];
            if (chunk is null && this.regionFile.HasChunk(x, z))
            {
                fromDisk = await GetChunkFromFileAsync(x, z);
                chunk = fromDisk;
            }

            chunk ??= create();
            lock (this.FluidTickLock)
            {
                chunk = loadedChunks[x, z] ?? chunk;
                this.PublishChunk(x, z, chunk, ReferenceEquals(chunk, fromDisk));
                return chunk;
            }
        }
        finally
        {
            chunkSlotLock.Release();
        }
    }

    public async Task UnloadChunk(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is null)
            return;

        await SerializeChunkAsync(chunk, unloading: true);
        lock (this.FluidTickLock)
        {
            if (!ReferenceEquals(loadedChunks[x, z], chunk))
                return;

            this.DetachLighting(chunk);
            loadedChunks[x, z] = null;
            this.retiredBlockAccess.Remove(chunk);
        }
    }

    /// <summary>Validates a captured chunk under FluidTickLock, including an unloading save still doing I/O.</summary>
    internal bool IsChunkResident(IChunk chunk)
    {
        var x = NumericsHelper.Modulo(chunk.X, CubicRegionSize);
        var z = NumericsHelper.Modulo(chunk.Z, CubicRegionSize);
        return !this.lightingRetired
            && !this.retiredBlockAccess.Contains(chunk)
            && ReferenceEquals(this.loadedChunks[x, z], chunk);
    }

    internal IChunk? GetLoadedChunk(int x, int z) => loadedChunks[x, z] is { IsGenerated: true } chunk ? chunk : null;

    /// <summary>
    /// Marks every loaded chunk unsaved (see <see cref="Chunk.MarkUnsaved"/>), for changes the chunks can't tell they
    /// save, like dropped structure starts.
    /// </summary>
    internal void MarkChunksUnsaved()
    {
        foreach (var chunk in this.loadedChunks.OfType<Chunk>())
            chunk.MarkUnsaved();
    }

    /// <summary>
    /// Takes an entity out of the region as the plugin owning it unloads (see <see cref="Plugins.PluginEntities"/>).
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="keepSaved">
    /// Whether to keep the entity in its chunk as it would be saved, among the saved entities the server can't spawn,
    /// when the chunk is loaded.
    /// </param>
    /// <returns>Whether the entity was in the region.</returns>
    internal bool Evict(IEntity entity, bool keepSaved)
    {
        lock (ItemEntity.TransferLock)
            return this.EvictUnderTransferLock(entity, keepSaved);
    }

    private bool EvictUnderTransferLock(IEntity entity, bool keepSaved)
    {
        var (chunkX, chunkZ) = ChunkOf(entity.Position);
        var local = this.GetLoadedChunk(
            NumericsHelper.Modulo(chunkX, CubicRegionSize),
            NumericsHelper.Modulo(chunkZ, CubicRegionSize));

        API.Data.AttachmentStorage.For(entity).Retire();

        if (!keepSaved || entity is not Entity concrete || local is not Chunk { X: var x, Z: var z } chunk
            || (x, z) != (chunkX, chunkZ))
        {
            if (!this.TryRemoveEntity(entity))
                return false;
        }
        else
        {
            // Under the chunk's entity lock, so a save finds the entity either in the level or kept.
            using (chunk.EntityLock.EnterScope())
            {
                if (!this.TryRemoveEntity(entity))
                    return false;

                if (!chunk.EntitiesUnloaded && EntityNbt.Save(concrete) is NbtCompound saved)
                    chunk.UnspawnableEntities.Add(saved);
            }
        }

        EntityGrid.Leave(entity);

        if (entity is Mob mob)
            mob.DetachForPluginUnload();

        if (entity is Entity host)
        {
            host.CancelEntityPortalTravel();
            host.Navigator = null;
            host.GoalController = null;
            host.SpawnedBy = null;
        }

        return true;
    }

    /// <summary>
    /// Spawns again the entities kept in the loaded chunks whose plugin entity types are loaded again (see
    /// <see cref="Evict"/>), on the level's next tick.
    /// </summary>
    internal void RespawnKeptEntities(AbstractLevel level)
    {
        foreach (var chunk in this.loadedChunks.OfType<Chunk>())
        {
            if (!chunk.IsGenerated)
                continue;

            var respawning = false;

            using (chunk.EntityLock.EnterScope())
            {
                // An unloaded chunk saved its kept entities; they spawn when it's loaded again.
                if (chunk.EntitiesUnloaded)
                    continue;

                var stillKept = new List<NbtCompound>();

                foreach (var kept in chunk.UnspawnableEntities)
                {
                    if (HasBoundPluginType(kept) && EntityNbt.ToGeneratedEntity(kept) is GeneratedEntity pending)
                        chunk.PendingEntities.Add(pending);
                    else
                        stillKept.Add(kept);
                }

                respawning = stillKept.Count != chunk.UnspawnableEntities.Count;

                if (respawning)
                {
                    chunk.UnspawnableEntities.Clear();
                    chunk.UnspawnableEntities.AddRange(stillKept);
                }
            }

            if (respawning)
                level.QueueEntitySpawn(chunk);
        }
    }

    // Whether a saved entity is of a plugin's entity type whose plugin is loaded.
    private static bool HasBoundPluginType(NbtCompound saved) =>
        saved.TryGetTagValue<string>("id", out var id)
        && EntityDefinitionsRegistry.TryGet(id, out var definition)
        && definition.Binding is not null;

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
                    if (entity.TryGetTagValue<string>("id", out var id) && EntityNbt.IsKnownType(id) &&
                        EntityNbt.ToGeneratedEntity(entity) is GeneratedEntity pending)
                    {
                        chunk.PendingEntities.Add(pending);
                    }
                    else
                    {
                        chunk.UnspawnableEntities.Add(entity);
                    }
                }
            }
        }

        return chunk;
    }

    private async Task<NbtCompound?> ReadCompoundAsync(RegionFile file, int x, int z)
    {
        await this.initialization.Value;

        if (await file.RentChunkBytesAsync(x, z) is not RentedArray<byte> data)
            return null;

        // Parsed in place: tags copy what they keep (arrays, strings), so the rented bytes go back once it's read.
        using (data)
            return NbtReader.ReadTag(data.Span, readName: true, out _) as NbtCompound;
    }

    /// <summary>
    /// The chunks the region holds, complete or still generating.
    /// </summary>
    internal IEnumerable<IChunk> ResidentChunks()
    {
        foreach (var chunk in loadedChunks)
        {
            if (chunk is not null)
                yield return chunk;
        }
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
        if (chunk is null)
            return;

        var x = NumericsHelper.Modulo(chunk.X, CubicRegionSize);
        var z = NumericsHelper.Modulo(chunk.Z, CubicRegionSize);
        lock (this.FluidTickLock)
            this.PublishChunk(x, z, chunk);
    }

    // Slot changes and lighting attachment share the tick lock; no read can later attach a stale instance.
    private void PublishChunk(int x, int z, IChunk chunk, bool fromDisk = false)
    {
        if (this.lightingRetired)
            return;

        if (this.loadedChunks[x, z] is IChunk previous && !ReferenceEquals(previous, chunk))
        {
            this.DetachLighting(previous);
            this.retiredBlockAccess.Remove(previous);
        }

        if (chunk is Chunk concrete)
            concrete.CompleteGeneration = this.CompleteChunk;

        // Lock-free readers must not see a complete chunk until all its section hooks are ready.
        this.LightLoaded?.Invoke(chunk, false);
        this.loadedChunks[x, z] = chunk;

        // Entity initialization reads this slot; queue it only after publication and lighting attachment.
        if (fromDisk && chunk is Chunk { IsGenerated: true } complete)
            this.EntitiesLoaded?.Invoke(complete);
    }

    /// <summary>
    /// Publishes completion and attaches lighting together, only for the chunk still stored in its slot.
    /// </summary>
    private void CompleteChunk(Chunk chunk)
    {
        lock (this.FluidTickLock)
        {
            if (this.IsChunkResident(chunk))
                this.LightLoaded?.Invoke(chunk, true);

            chunk.SetChunkStatusCore(ChunkGenStage.full);
        }
    }

    private void DetachLighting(IChunk chunk)
    {
        if (chunk is Chunk concrete)
            concrete.CompleteGeneration = null;

        this.RetireBlockAccess(chunk);
    }

    private void RetireBlockAccess(IChunk chunk)
    {
        if (chunk is not null)
            API.Data.AttachmentStorage.For(chunk).Retire();

        if (this.retiredBlockAccess.Add(chunk))
        {
            this.LightUnloaded?.Invoke(chunk);
            this.BlockAccessUnloaded?.Invoke(chunk);
        }
    }

    /// <summary>Flushes final light and detaches every chunk when the level removes this region.</summary>
    internal void RetireLighting()
    {
        lock (this.FluidTickLock)
        {
            this.lightingRetired = true;
            this.FlushLight?.Invoke();
            foreach (var chunk in this.loadedChunks)
                this.DetachLighting(chunk);
        }
    }

    /// <summary>
    /// Stores <paramref name="replacement"/> in place of <paramref name="stored"/>, if that's the chunk stored at its
    /// position. Generation replaces a chunk it failed on under the chunk's lock, so a save waiting for the lock
    /// serializes the replacement (see <see cref="SnapshotChunkAsync"/>).
    /// </summary>
    internal void ReplaceChunk(IChunk stored, IChunk replacement)
    {
        var x = NumericsHelper.Modulo(stored.X, CubicRegionSize);
        var z = NumericsHelper.Modulo(stored.Z, CubicRegionSize);

        lock (this.FluidTickLock)
        {
            if (ReferenceEquals(this.loadedChunks[x, z], stored))
                this.PublishChunk(x, z, replacement);
        }
    }

    /// <summary>
    /// Saves a chunk, unless it's unchanged since its last save (see <see cref="Chunk.IsUnsaved"/>), and once it's
    /// complete, its entities to the entity region file.
    /// </summary>
    /// <param name="unloading">Whether the chunk is being unloaded: its saved entities are also taken out of the level.</param>
    /// <param name="entities">Whether to save the entities of a complete chunk that isn't unloading; unloading always
    /// saves them.</param>
    internal async Task SerializeChunkAsync(IChunk chunk, bool unloading = false, bool entities = true)
    {
        await this.initialization.Value;

        var admitted = this.AdmitSave();
        var save = NewSave(chunk);
        try
        {
            try
            {
                await this.SnapshotChunkAsync(save, unloading, entities);
                await this.StageSaveAsync(save);
            }
            finally
            {
                // Commits with the writes other saves staged meanwhile; failures reach each save's writes.
                await this.CommitAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            await this.CompleteSaveAsync(save);
        }
        finally
        {
            save.Dispose();
            this.EndSave(admitted);

            // Also when the snapshot failed after retiring them, once its locks are released.
            await this.ReleaseRetiredAsync(save);
        }
    }

    private static ChunkSave NewSave(IChunk chunk) => new()
    {
        Chunk = chunk,
        X = NumericsHelper.Modulo(chunk.X, CubicRegionSize),
        Z = NumericsHelper.Modulo(chunk.Z, CubicRegionSize)
    };

    /// <summary>
    /// Takes what a save writes of a chunk into <paramref name="save"/>: its NBT, uncompressed, and its entities, as of
    /// one moment under the tick lock, and its turn to be written to each file, in the order snapshots are taken. An
    /// unloading save takes its turns even with nothing to write: waiting for them, it waits for the chunk's earlier
    /// writes, so the chunk read once it's unloaded is the latest.
    /// </summary>
    private async Task SnapshotChunkAsync(ChunkSave save, bool unloading, bool saveEntities)
    {
        var (chunk, x, z) = (save.Chunk, save.X, save.Z);
        NbtWriterStream? writer = null;
        try
        {
            // Snapshot blocks, fluid ticks and entities together; take the generator's lock before the fluid lock.
            using (this.LockChunk is null ? null : await this.LockChunk(chunk.X, chunk.Z))
            {
                lock (this.FluidTickLock)
                {
                    this.FlushLight?.Invoke();

                    // The chunk stored now, read under the lock: generation may have replaced the one this save was
                    // given while it waited (see ReplaceChunk). One no longer stored was unloaded and is saved as it
                    // was.
                    var current = this.loadedChunks[x, z] ?? chunk;

                    // No block hooks or async setters may change this instance after its final snapshot.
                    if (unloading && ReferenceEquals(current, chunk))
                        this.RetireBlockAccess(current);

                    if (unloading && current is Chunk retiring)
                        BlockEntityLifecycle.Retire(retiring, save.Retired, save.Unloaded);

                    // Taken after the light flush, which may write into the chunk, and the containers' retirement, which
                    // freezes their items. A chunk unchanged since its last save is on disk as it is, so only its
                    // entities may need saving.
                    var write = current is not Chunk tracked || tracked.TakeUnsaved();
                    if (write)
                    {
                        save.Taken = current as Chunk;

                        var loadedStarts = (current as Chunk)?.StructureStarts;
                        var structureStarts = this.SaveStructureStarts is null
                            ? loadedStarts
                            : this.SaveStructureStarts(current.X, current.Z, loadedStarts);

                        // Only serialized here, into memory; compressing waits until the lock is released.
                        save.Nbt = new PooledBufferStream(UncompressedChunkCapacity);
                        writer = new NbtWriterStream(save.Nbt, NbtCompression.None, "");
                        SerializeChunk(writer.Value, current, structureStarts);
                    }

                    if (write || unloading)
                        save.ChunkWrite = this.regionFile.ReserveWrite(x, z);

                    if ((unloading || saveEntities) && current.IsGenerated && current is Chunk complete)
                        save.Entities = this.CollectEntities(complete, unloading);

                    if (save.Entities is not null || unloading)
                        save.EntityWrite = this.entityRegionFile.ReserveWrite(x, z);
                }
            }

            if (writer is NbtWriterStream finishing)
            {
                finishing.EndCompound();
                finishing.TryFinish();
            }
        }
        catch
        {
            // The writer goes first: disposing it writes what it buffered into the NBT the save gives back.
            writer?.Dispose();
            writer = null;

            save.Taken?.MarkUnsaved();
            save.Dispose();
            throw;
        }
        finally
        {
            writer?.Dispose();
        }
    }

    /// <summary>
    /// Compresses what <see cref="SnapshotChunkAsync"/> took and stages it in each file, each write in its turn, for
    /// the next commit. Turns with nothing to write are given up, which still waits for the turns before them.
    /// </summary>
    private async Task StageSaveAsync(ChunkSave save)
    {
        PooledBufferStream? compressed = null;
        try
        {
            if (save.Nbt is not null)
            {
                compressed = this.regionFile.Compress(save.Nbt.Written.Span);
                save.Nbt.Return();

                await this.regionFile.StageAsync(save.ChunkWrite!, compressed.Written);
            }

            if (save.Entities is not null)
                await this.StageEntitiesAsync(save);
        }
        catch
        {
            save.Taken?.MarkUnsaved();
            throw;
        }
        finally
        {
            compressed?.Return();

            // Ends only the turns that weren't staged; their commit ends the others.
            save.ChunkWrite?.Dispose();
            save.EntityWrite?.Dispose();
        }
    }

    // Commits what both files have staged.
    private Task CommitAsync() => Task.WhenAll(this.regionFile.CommitAsync(), this.entityRegionFile.CommitAsync());

    /// <summary>
    /// Waits for the save's turns, and every turn of the chunk before them, to end; throws when its writes failed.
    /// </summary>
    private async Task CompleteSaveAsync(ChunkSave save)
    {
        if (save.ChunkWrite is { } chunkWrite)
            await chunkWrite.Done;

        if (save.EntityWrite is { } entityWrite)
            await entityWrite.Done;

        if (save.ChunkWrite?.Failure is Exception chunkFailure)
        {
            save.Taken?.MarkUnsaved();
            ExceptionDispatchInfo.Throw(chunkFailure);
        }

        if (save.EntityWrite?.Failure is Exception entityFailure)
        {
            // The file may still list the entities its last commit wrote.
            if (save.Chunk is Chunk unknown)
                unknown.SavedEntityCount = -1;

            ExceptionDispatchInfo.Throw(entityFailure);
        }
    }

    /// <summary>
    /// Lets the containers and block entities an unloading save retired go, once it's written or failed. It's not part
    /// of the save: handlers of the closing windows may flush this region, which waits for the saves admitted before.
    /// </summary>
    private async Task ReleaseRetiredAsync(ChunkSave save)
    {
        // Inventories were frozen before the snapshot; a failed write must still close the retired windows.
        foreach (var inventory in save.Retired)
            await AbstractLevel.CloseWindowsAsync(inventory);

        // Saved and retired, the block entities hear their chunk unloaded while they still reach their level.
        foreach (var blockEntity in save.Unloaded)
        {
            await blockEntity.WaitForLiveCallAsync();
            this.BlockEntityUnloaded?.Invoke(blockEntity);
            BlockEntityLifecycle.Detach(blockEntity);
        }

        save.Retired.Clear();
        save.Unloaded.Clear();
    }

    /// <summary>
    /// Admits a save (a flush, or one chunk's) before it takes anything, so closing the region waits for it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The region is closing.</exception>
    private long AdmitSave()
    {
        lock (this.saves)
        {
            ObjectDisposedException.ThrowIf(this.closing, this);

            var admitted = ++this.lastSave;
            this.saves[admitted] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return admitted;
        }
    }

    private void EndSave(long admitted)
    {
        TaskCompletionSource? ended;
        lock (this.saves)
            this.saves.Remove(admitted, out ended);

        ended?.TrySetResult();
    }

    // Admission order, not the saves themselves, decides what a flush waits for, so two flushes never wait for each
    // other.
    private Task SavesAdmittedBeforeAsync(long admitted)
    {
        lock (this.saves)
            return Task.WhenAll(this.saves.Where(save => save.Key < admitted).Select(save => save.Value.Task));
    }

    // Stops admitting saves, then waits for every one admitted, however many are admitted while it waits.
    private async Task CloseSavesAsync()
    {
        while (true)
        {
            Task admitted;
            lock (this.saves)
            {
                this.closing = true;
                if (this.saves.Count == 0)
                    return;

                admitted = Task.WhenAll(this.saves.Values.Select(save => save.Task));
            }

            await admitted;
        }
    }

    /// <summary>
    /// The chunks the region's entities are in, whose entities a flush saves.
    /// </summary>
    private HashSet<(int X, int Z)> ChunksWithEntities()
    {
        var chunks = new HashSet<(int X, int Z)>();
        foreach (var (_, entity) in this.entities)
        {
            if (entity is Entity levelEntity)
                chunks.Add(ChunkOf(levelEntity.Position));
        }

        return chunks;
    }

    /// <summary>
    /// Whether a flush saves the entities of <paramref name="chunk"/>: it's complete, and has entities in the level or
    /// waiting to spawn, or its entity file may still list entities it no longer has.
    /// </summary>
    private bool MayHaveEntitiesToSave(Chunk chunk, HashSet<(int X, int Z)> entityChunks)
    {
        if (!chunk.IsGenerated)
            return false;

        if (entityChunks.Contains((chunk.X, chunk.Z)) || chunk.PendingEntities.Count > 0 || chunk.UnspawnableEntities.Count > 0)
            return true;

        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));
        return chunk.SavedEntityCount != 0 && this.entityRegionFile.HasChunk(x, z);
    }

    /// <summary>
    /// The entities to save with a complete chunk: those still waiting to spawn, and those of the level in the chunk. When
    /// unloading, the level's are taken out of it too, and the chunk's pending ones won't spawn anymore.
    /// </summary>
    /// <returns><c>null</c> once an unloading save took the entities, which a later save of the chunk mustn't overwrite.</returns>
    private NbtList? CollectEntities(Chunk chunk, bool unloading)
    {
        var entities = new NbtList(NbtTagType.Compound, "Entities");

        lock (ItemEntity.TransferLock)
        {
            using (chunk.EntityLock.EnterScope())
        {
            if (chunk.EntitiesUnloaded)
                return null;

            foreach (var pending in chunk.PendingEntities)
                entities.Add(EntityNbt.ToNbt(pending));

            foreach (var unsupported in chunk.UnspawnableEntities)
                entities.Add(unsupported);

            foreach (var (_, entity) in this.entities)
            {
                if (entity is not Entity levelEntity || ChunkOf(levelEntity.Position) != (chunk.X, chunk.Z))
                    continue;

                if (unloading && levelEntity is not IPlayer)
                    API.Data.AttachmentStorage.For(levelEntity).Retire();

                if (EntityNbt.Save(levelEntity) is not NbtCompound saved)
                {
                    if (unloading && levelEntity is Mob { HasAi: true } && this.TryRemoveEntity(levelEntity))
                        EntityGrid.Leave(levelEntity);
                    continue;
                }

                entities.Add(saved);
                if (unloading && this.TryRemoveEntity(levelEntity))
                    EntityGrid.Leave(levelEntity);
            }

            if (unloading)
                chunk.EntitiesUnloaded = true;
        }
        }

        return entities;
    }

    /// <summary>
    /// Stages a chunk's entities like vanilla's <c>EntityStorage</c> writes them: <c>DataVersion</c>, <c>Position</c>
    /// and <c>Entities</c>, in the save's turn, so whether the file has an entry for the chunk is as of the saves
    /// before.
    /// </summary>
    private async Task StageEntitiesAsync(ChunkSave save)
    {
        var (chunkX, chunkZ) = (save.Chunk.X, save.Chunk.Z);
        var entities = save.Entities!;
        var turn = save.EntityWrite!;

        await turn.TurnAsync;

        // Vanilla deletes the entry of a chunk left without entities; region files can't delete entries, so a chunk that had
        // entities keeps an empty list, and one that never had any gets no entry.
        if (entities.Count == 0 && !this.entityRegionFile.HasChunk(save.X, save.Z))
        {
            if (save.Chunk is Chunk empty)
                empty.SavedEntityCount = 0;

            return;
        }

        var nbt = new PooledBufferStream(4 * 1024);
        PooledBufferStream? compressed = null;
        try
        {
            using (var writer = new NbtWriterStream(nbt, NbtCompression.None, ""))
            {
                writer.WriteInt("DataVersion", ServerConstants.DataVersion);
                writer.WriteArray("Position", [chunkX, chunkZ]);
                writer.WriteTag(entities);
                writer.EndCompound();

                writer.TryFinish();
            }

            compressed = this.entityRegionFile.Compress(nbt.Written.Span);
            await this.entityRegionFile.StageAsync(turn, compressed.Written);
        }
        finally
        {
            nbt.Return();
            compressed?.Return();
        }

        // In the turn, which passes only once this is committed: a failed commit makes it unknown again (see
        // CompleteSaveAsync).
        if (save.Chunk is Chunk saved)
            saved.SavedEntityCount = entities.Count;
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
        foreach (var entity in this.entities.Values.OrderBy(entity => entity.EntityId).ToArray())
        {
            cts.ThrowIfCancellationRequested();
            await entity.TickAsync();
        }

        this.MoveEntitiesToTheirRegions();
    }

    /// <summary>
    /// Hands entities that moved into another loaded region's chunks over to that region, which saves them with its chunks.
    /// </summary>
    internal void MoveEntitiesToTheirRegions()
    {
        foreach (var (_, entity) in this.entities)
        {
            lock (ItemEntity.TransferLock)
            {
                if (!this.entities.TryGetValue(entity.EntityId, out var resident) || !ReferenceEquals(resident, entity))
                    continue;

                var (chunkX, chunkZ) = ChunkOf(entity.Position);
                if (chunkX >> CubicRegionSizeShift == this.X && chunkZ >> CubicRegionSizeShift == this.Z)
                    continue;

                if (entity.Level is AbstractLevel level && level.GetRegionForChunk(chunkX, chunkZ) is Region target
                    && target.TryAddEntity(entity))
                {
                    this.TryRemoveEntity(entity);
                }
            }
        }
    }

    #region NBT Ops
    // The chunk tags SerializeChunk writes. Lighting is marked for recomputation by vanilla.
    private static readonly HashSet<string> modeledChunkTags =
    [
        "DataVersion",
        "xPos",
        "zPos",
        "yPos",
        "Status",
        "InhabitedTime",
        "sections",
        "Heightmaps",
        "block_entities",
        "entities",
        "structures",
        "PostProcessing",
        "fluid_ticks",
        "block_ticks",
        "ObsidianMobEggTicks",
        "ObsidianFrogspawnTicks",
        Portals.PortalOwnership.NbtKey,
        "obsidian:attachments",
        "isLightOn"
    ];

    private Chunk DeserializeChunk(NbtCompound chunkCompound)
    {
        int x = chunkCompound.GetInt("xPos");
        int z = chunkCompound.GetInt("zPos");

        var chunk = new Chunk(x, z, this.minY, this.height);
        API.Data.AttachmentStorage.For(chunk).Load(chunkCompound);
        Portals.PortalOwnership.Read(chunk, chunkCompound);
        if (chunkCompound.TryGetTag<NbtList>("ObsidianMobEggTicks", out var mobEggTicks))
        {
            foreach (NbtCompound tick in mobEggTicks)
                chunk.MobEggTicks[new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z"))] = Math.Clamp(tick.GetInt("delay"), 0, 8300);
        }

        if (chunkCompound.TryGetTag<NbtList>("ObsidianFrogspawnTicks", out var frogspawnTicks))
        {
            foreach (var tick in frogspawnTicks.OfType<NbtCompound>())
                chunk.FrogspawnTicks[new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z"))] = Math.Clamp(tick.GetInt("delay"), 1, 12000);
        }

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

            var section = chunk.Sections[sectionIndex];

            if (sectionCompound.TryGetTag<NbtCompound>("block_states", out var blockStates))
                ChunkSectionNbt.ReadBlockStates(blockStates, section.BlockStateContainer);

            // The storage was filled directly, so the section doesn't know whether it holds blocks yet.
            (section as ChunkSection)?.RecalculateEmpty();

            if (sectionCompound.TryGetTag<NbtCompound>("biomes", out var biomes))
                ChunkSectionNbt.ReadBiomes(biomes, section.BiomeContainer);

            if (sectionCompound.TryGetTag("SkyLight", out var skyLightTag))
                LoadLight(section, ((NbtArray<byte>)skyLightTag).GetArray(), LightType.Sky);

            if (sectionCompound.TryGetTag("BlockLight", out var blockLightTag))
                LoadLight(section, ((NbtArray<byte>)blockLightTag).GetArray(), LightType.Block);
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
            // Plugins' block entities load as saved data, which the level makes theirs on its tick once the chunk is
            // complete (see AbstractLevel.RestoreKeptBlockEntities), in turn with plugins unloading.

            foreach (var blockEntityCompound in blockEntities.Cast<NbtCompound>())
            {
                if (!blockEntityCompound.TryGetTag<NbtTag<string>>("id", out var id) || id.Value == "minecraft:bed")
                    continue;

                var position = new Vector(blockEntityCompound.GetInt("x"), blockEntityCompound.GetInt("y"), blockEntityCompound.GetInt("z"));
                if (position.Y < this.minY || position.Y >= this.minY + this.height)
                    continue;

                var blockEntity = BlockEntityNbt.Load(blockEntityCompound, id.Value!, position, pluginInstances: false);
                chunk.SetBlockEntity(position.X, position.Y, position.Z, blockEntity);
            }
        }

        if (storedMinSection == minSection)
            ReadPostProcessing(chunkCompound, chunk);

        if (chunkCompound.TryGetTag<NbtList>("fluid_ticks", out var fluidTicks))
            chunk.FluidTicks.Read(fluidTicks, x, z, FluidNames.TryParse);

        if (chunkCompound.TryGetTag<NbtList>("block_ticks", out var blockTicks))
            chunk.BlockTicks.Read(blockTicks, x, z);

        // Entities of a chunk that isn't complete yet, like a vanilla proto chunk's "entities".
        if (chunkCompound.TryGetTag<NbtList>("entities", out var entities))
        {
            foreach (var entityCompound in entities.OfType<NbtCompound>())
            {
                if (EntityNbt.ToGeneratedEntity(entityCompound) is GeneratedEntity pending)
                    chunk.PendingEntities.Add(pending);
            }
        }

        var hasStructures = chunkCompound.TryGetTag<NbtCompound>("structures", out var structures);
        if (hasStructures && structures!.TryGetTag<NbtCompound>("starts", out var starts))
            chunk.StructureStarts = starts;

        // The world upgrade converts legacy data before loading. Keep fields we do not model, including structure
        // references; a version mismatch must never silently discard them.
        foreach (var (name, tag) in chunkCompound)
        {
            if (!modeledChunkTags.Contains(name))
                chunk.UnmodeledTags.Add(tag);
        }
        if (hasStructures && structures!.TryGetTag<NbtCompound>("References", out var references))
            chunk.StructureReferences = references;

        chunk.SetChunkStatus(ParseStatus(chunkCompound.TryGetTag<NbtTag<string>>("Status", out var status) ? status.Value : null));

        // Completing generation releases optional maps. A loaded vanilla chunk still owns the maps it saved,
        // including OCEAN_FLOOR and MOTION_BLOCKING_NO_LEAVES, so retain those through subsequent saves.
        if (heightmapsMatch)
        {
            foreach (var (name, heightmap) in heightmaps!)
            {
                if (!Enum.TryParse<HeightmapType>(name.Replace("_", ""), true, out var type) || chunk.Heightmaps.ContainsKey(type))
                    continue;
                var retained = new Heightmap(type, chunk);
                retained.data.storage = ((NbtArray<long>)heightmap).GetArray();
                chunk.Heightmaps[type] = retained;
            }
        }

        return chunk;
    }

    // The chunk isn't published yet, so its sections take the light into the storage they have rather than through
    // SetLight's defensive copy; sections of another kind, or light of an unexpected length, still go through SetLight.
    private static void LoadLight(IChunkSection section, byte[] light, LightType type)
    {
        if (section is ChunkSection loading && loading.TryLoadLight(light, type))
            return;

        section.SetLight(light, type);
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

        // The old generators still have separate steps. A loaded terrain chunk has completed all three.
        if (name == "terrain")
            return ChunkGenStage.carvers;

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

    /// <summary>
    /// Writes post-processing marks in vanilla's shape (see <see cref="ReadPostProcessing"/>): a list for every section,
    /// of positions packed as <c>x | y &lt;&lt; 4 | z &lt;&lt; 8</c> within it.
    /// </summary>
    private static void WritePostProcessing(NbtWriterStream writer, Chunk chunk)
    {
        var bySection = chunk.PostProcessing.ToLookup(position => (position.Y - chunk.MinY) >> 4);

        writer.WriteListStart("PostProcessing", NbtTagType.List, chunk.Sections.Length);
        for (var section = 0; section < chunk.Sections.Length; section++)
        {
            var marks = bySection[section].ToList();

            writer.WriteListStart("", NbtTagType.Short, marks.Count);
            foreach (var position in marks)
                writer.WriteShort((short)((position.X & 15) | (position.Y & 15) << 4 | (position.Z & 15) << 8));

            writer.EndList();
        }

        writer.EndList();
    }

    /// <summary>
    /// Writes a chunk's compound as region files store it, without its end tag.
    /// </summary>
    internal static void SerializeChunk(NbtWriterStream writer, IChunk chunk, NbtCompound? structureStarts)
    {
        writer.WriteTag(API.Data.AttachmentStorage.For(chunk).Snapshot());
        writer.WriteLong("InhabitedTime", chunk is Chunk concrete ? concrete.InhabitedTime : 0);
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
            // Written straight from the section's storage: the writer copies a span before returning, still under the
            // save's locks.
            writer.WriteArray("SkyLight", section.SkyLightArray.Span);
            writer.WriteArray("BlockLight", section.BlockLightArray.Span);

            writer.EndCompound();

        }
        writer.EndList();

        var blockEntities = chunk.GetBlockEntities()
            .Select(BlockEntityNbt.Save)
            .OfType<NbtCompound>()
            .ToList();
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

        // Vanilla's structures.starts. Obsidian follows references from the starts, which are recomputed from the seed,
        // so it only keeps the references a chunk was loaded with, for vanilla to read.
        var structureReferences = (chunk as Chunk)?.StructureReferences;
        {
            writer.WriteCompoundStart("structures");
            writer.WriteCompoundStart("starts");
            if (structureStarts is not null)
                writer.WriteEntries(structureStarts);

            writer.EndCompound();

            writer.WriteTag(structureReferences ?? new NbtCompound("References"));

            writer.EndCompound();
        }

        if (chunk is Chunk generated)
        {
            foreach (var tag in generated.UnmodeledTags)
                writer.WriteTag(tag);

            WritePostProcessing(writer, generated);

            // Scheduled block and fluid ticks, like vanilla's "block_ticks" and "fluid_ticks".
            generated.BlockTicks.Write(writer);
            generated.FluidTicks.Write(writer, "fluid_ticks", FluidNames.Name);
            var mobEggTicks = generated.HasMobEggTicks ? generated.MobEggTicks.ToArray() : [];
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
            var frogspawnTicks = generated.HasFrogspawnTicks ? generated.FrogspawnTicks.ToArray() : [];
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
            Portals.PortalOwnership.Write(writer, generated);
        }

        writer.WriteInt("xPos", chunk.X);
        writer.WriteInt("zPos", chunk.Z);
        writer.WriteInt("yPos", chunk.MinY >> 4);

        // Legacy generation stages share 26.3's terrain status on disk.
        writer.WriteInt("DataVersion", ServerConstants.DataVersion);
        var status = chunk.ChunkStatus is ChunkGenStage.noise or ChunkGenStage.surface or ChunkGenStage.carvers
            ? "terrain" : chunk.ChunkStatus.ToString();
        writer.WriteString("Status", $"minecraft:{status}");
        // Lighting is recomputed when vanilla opens an Obsidian save.
        writer.WriteBool("isLightOn", false);
        if (chunk is not Chunk stored || !stored.UnmodeledTags.Any(tag => tag.Name == "LastUpdate"))
            writer.WriteLong("LastUpdate", 0);

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
        // Loading a region starts reading its headers without waiting (see AbstractLevel.LoadRegion). Closing the
        // files under that read can leave them open until it ends, after the level is disposed, so it's waited out.
        // A failed read fails the region's other operations, which wait for it too, not its disposal.
        if (this.initialization.IsValueCreated)
            await this.initialization.Value.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // No save is admitted from now on; those admitted are committed first. Their failures are their callers'.
        await this.CloseSavesAsync();

        await regionFile.DisposeAsync();
        await this.entityRegionFile.DisposeAsync();
    }

    /// <summary>
    /// What a save took of a chunk (see <see cref="SnapshotChunkAsync"/>) until it's written. Disposing it gives back
    /// the uncompressed NBT and gives up the turns it didn't stage.
    /// </summary>
    private sealed class ChunkSave : IDisposable
    {
        public required IChunk Chunk { get; init; }

        // The chunk's local coordinates in the region.
        public required int X { get; init; }

        public required int Z { get; init; }

        // The chunk's NBT, uncompressed, and its turn in the region file; null when the chunk was unchanged.
        public PooledBufferStream? Nbt { get; set; }

        public RegionFile.ChunkWrite? ChunkWrite { get; set; }

        // The chunk whose unsaved marks this save took, so a save failing before the chunk is written puts them back.
        public Chunk? Taken { get; set; }

        // The entities to save, and their turn in the entity region file; null when they aren't saved.
        public NbtList? Entities { get; set; }

        public RegionFile.ChunkWrite? EntityWrite { get; set; }

        public List<BaseContainer> Retired { get; } = [];

        public List<BlockEntity> Unloaded { get; } = [];

        public void Dispose()
        {
            this.Nbt?.Return();
            this.ChunkWrite?.Dispose();
            this.EntityWrite?.Dispose();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} chunks of {Kind} region ({X}, {Z}) share sectors "
            + "with another chunk; the file is damaged. Their sectors are kept until no chunk uses them.")]
        public static partial void OverlappingChunks(ILogger logger, int count, string kind, int x, int z);
    }
}
