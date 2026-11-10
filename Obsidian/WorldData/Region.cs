using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Utilities.Collections;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
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
    /// The transfer lock of the region's level (see <see cref="EntityTransferLock"/>), under which entities move between
    /// its regions and its saves take theirs. A region made on its own has one of its own.
    /// </summary>
    internal EntityTransferLock EntityTransfers { get; init; } = new();

    /// <summary>
    /// Whether the region is retiring, which no entity may leave it for another level from anymore (see
    /// <see cref="AbstractLevel.TryPinForTravel"/>). Set under the level's transfer lock.
    /// </summary>
    internal bool TransfersClosed { get; set; }

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

    // Opened in the constructor, or once the region's predecessor closed its own (see the constructor).
    private RegionFile regionFile = null!;

    // At most 1024 entries, released with the region. Only ids and boxes are retained, never full chunk/piece NBT.
    private readonly ConcurrentDictionary<int, Lazy<Task<SavedStructureStart[]>>> savedStartBounds = new();

    // Like vanilla, entities of complete chunks are kept in their own region files (entities/r.x.z.mca).
    private RegionFile entityRegionFile = null!;

    // Reading the region files' headers; every file operation waits for it, since regions are loaded without waiting.
    private readonly Lazy<Task> initialization;

    // Serializes filling empty chunk slots, so concurrent callers never end up with different instances of a chunk.
    private readonly SemaphoreSlim chunkSlotLock = new(1, 1);

    // The saves admitted and not yet committed, by admission order, from before their snapshot (see AdmitSave).
    private readonly Dictionary<long, TaskCompletionSource> saves = [];
    private long lastSave;

    // Which of the saves are unloads' (see AdmitSave). Under the saves' lock.
    private readonly HashSet<long> unloadSaves = [];

    // The slots an unload is taking the chunk of, so two unloads overlapping never both take one. Under FluidTickLock.
    private readonly HashSet<(int X, int Z)> unloadingSlots = [];

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
    /// The entity region file, for tests that watch or fail its writes (see <see cref="RegionFile.Observe"/>).
    /// </summary>
    internal RegionFile EntityFile => this.entityRegionFile;

    /// <summary>
    /// Run as an unloading snapshot has taken its chunk's unsaved marks, under the tick lock, for tests that make an
    /// earlier write of the chunk fail just then.
    /// </summary>
    internal Action? UnloadMarksTaken { get; set; }

    // Test rendezvous at snapshot inventory admission, while the level transfer lock is held.
    internal Action? BeforeInventorySnapshot { get; set; }

    // Test observation after an admission failed and all snapshot locks were released.
    internal Action? InventorySnapshotDeferred { get; set; }

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

    // Structure lifetime includes unfinished chunks and lasts through the final committed save.
    internal Action<IChunk>? StructureChunkLoaded { get; init; }

    internal Action<IChunk>? StructureChunkUnloaded { get; init; }

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
    /// Tells a block entity its chunk unloaded (the level's <see cref="AbstractLevel.OnBlockEntityUnloaded"/>), right
    /// after the chunk's final snapshot took it out of the region, still under the level's simulation gate when the
    /// level unloads it, but holding no lock.
    /// </summary>
    internal Action<BlockEntity>? BlockEntityUnloaded { get; init; }
    private readonly HashSet<IChunk> retiredBlockAccess = [];

    // A removed region cannot publish or attach chunks, including loads and generation already in flight.
    private bool lightingRetired;

    // Set as the region's final snapshot takes its chunks: structure notifications wait for file closure, then mark
    // the chunk a fresh region object loads.
    private bool structureChangesSealed;
    private TaskCompletionSource? regionUnload;
    private readonly long[,] chunkVersions = new long[CubicRegionSize, CubicRegionSize];

    // Read under FluidTickLock, which also orders registration/removal in the owning level.
    internal Task? RegionUnload => this.regionUnload?.Task;

    internal bool StructureChangesSealed => this.structureChangesSealed;

    /// <summary>
    /// The final saves of the level's unloaded chunks not yet committed, which loads read before the files (see
    /// <see cref="PendingChunkWrites"/>). A region made on its own has its own.
    /// </summary>
    internal PendingChunkWrites PendingWrites { get; init; } = new();

    /// <summary>Validates and optionally marks a stored start chunk, atomically with removal. Under FluidTickLock.</summary>
    internal bool TryAccessStructureChunk(Chunk chunk, bool markUnsaved)
    {
        if (this.structureChangesSealed)
            return false;

        var x = NumericsHelper.Modulo(chunk.X, CubicRegionSize);
        var z = NumericsHelper.Modulo(chunk.Z, CubicRegionSize);
        if (!ReferenceEquals(this.loadedChunks[x, z], chunk))
            return false;

        if (markUnsaved)
            chunk.MarkUnsaved();

        return true;
    }

    internal void EndRegionUnload() => this.regionUnload?.TrySetResult();

    // The dimension's build range, which decides the section count of loaded chunks.
    private readonly int minY;
    private readonly int height;

    /// <param name="regionFolderName">The chunk region files' folder in <paramref name="worldFolderPath"/>: Obsidian's
    /// <c>regions</c>, or vanilla's <c>region</c>. Entities are in <c>entities</c> either way.</param>
    /// <param name="predecessor">
    /// The region object this one replaces at its position, which may still hold the files while it writes its final
    /// saves (see <see cref="RegionUnload"/>). The files are opened once it closed them; until then reads go to the
    /// level's pending writes (see <see cref="PendingWrites"/>), which hold every save it's writing, and then to its
    /// files, which hold every other chunk as committed.
    /// </param>
    internal Region(int x, int z, string worldFolderPath, string regionFolderName = "regions",
        NbtCompression chunkCompression = NbtCompression.ZLib, int minY = -64, int height = 384,
        Region? predecessor = null)
    {
        X = x;
        Z = z;
        this.minY = minY;
        this.height = height;
        RegionFolder = Path.Join(worldFolderPath, regionFolderName);
        Directory.CreateDirectory(RegionFolder);
        ChunkCompression = chunkCompression;

        var entityFolder = Path.Join(worldFolderPath, "entities");
        Directory.CreateDirectory(entityFolder);
        this.chunkFilePath = Path.Join(RegionFolder, $"r.{X}.{Z}.mca");
        this.entityFilePath = Path.Join(entityFolder, $"r.{X}.{Z}.mca");

        // The files are opened exclusively, so not while the region this one replaces still has them.
        this.predecessor = predecessor;
        if (predecessor is null)
            this.OpenFiles();

        this.initialization = new(() => this.InitializeFilesAsync(predecessor?.RegionUnload));
    }

    // Read until this region opens the files (see the constructor); null once it has.
    private Region? predecessor;

    private readonly string chunkFilePath;
    private readonly string entityFilePath;

    private void OpenFiles()
    {
        this.regionFile = new RegionFile(this.chunkFilePath, this.ChunkCompression, CubicRegionSize);
        this.entityRegionFile = new RegionFile(this.entityFilePath, this.ChunkCompression, CubicRegionSize);
    }

    private async Task InitializeFilesAsync(Task? predecessor)
    {
        if (predecessor is not null)
        {
            // Its failures are its own; its unwritten chunks stay pending in the level, which retries them here.
            await predecessor.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            this.OpenFiles();
            this.predecessor = null;
        }

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
    /// <param name="snapshotsTaken">
    /// Completed once the save has taken its snapshots of the chunks, or failed before taking them all: from then on
    /// it only compresses and writes what it took, so the level may tick again (see
    /// <c>AbstractLevel.FlushRegionsAsync</c>).
    /// </param>
    internal async Task WriteChangesAsync(TaskCompletionSource? snapshotsTaken = null)
    {
        try
        {
            await this.WriteChangesCoreAsync(snapshotsTaken);
        }
        finally
        {
            // Also when it failed before taking them, so the level doesn't wait on it.
            snapshotsTaken?.TrySetResult();
        }
    }

    private async Task WriteChangesCoreAsync(TaskCompletionSource? snapshotsTaken)
    {
        await this.initialization.Value;

        var admitted = this.AdmitSave();
        var taken = new List<ChunkSave>();
        try
        {
            await this.flushLock.WaitAsync();
            try
            {
                await this.WriteChangesUnderLockAsync(taken, snapshotsTaken);
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

    /// <summary>
    /// Starts saving <paramref name="chunks"/>, like vanilla's eager chunk saves (see
    /// <c>AbstractLevel.SaveChunksEagerlyAsync</c>), unless another save of the region is taking or writing
    /// snapshots, the region isn't ready, or it's closing: the level's tick never waits for those. It saves their
    /// blocks, and their entities only until the region first writes them (see <see cref="NeedsEntitySave"/>). A chunk
    /// whose generator lock is held, or that no staging slot is free for, is left out.
    /// </summary>
    /// <param name="snapshotted">
    /// Filled with the chunks the save took, before <paramref name="snapshotsTaken"/> completes; the region doesn't
    /// touch it after.
    /// </param>
    /// <param name="snapshotsTaken">
    /// Completed once the chunks are snapshotted, or the save failed before (see <see cref="WriteChangesAsync"/>).
    /// </param>
    /// <returns>The save, which ends once its chunks are written; null when it didn't start.</returns>
    internal Task? TrySaveChunks(
        IReadOnlyList<IChunk> chunks,
        List<IChunk> snapshotted,
        TaskCompletionSource snapshotsTaken)
    {
        if (!this.initialization.IsValueCreated || !this.initialization.Value.IsCompletedSuccessfully)
            return null;

        if (!this.flushLock.Wait(0))
            return null;

        long admitted;
        try
        {
            lock (this.FluidTickLock)
            {
                if (this.lightingRetired)
                {
                    this.flushLock.Release();
                    return null;
                }
            }

            admitted = this.AdmitSave();
        }
        catch (ObjectDisposedException)
        {
            // The region is closing.
            this.flushLock.Release();
            return null;
        }

        return this.SaveChunksUnderLockAsync(chunks, admitted, snapshotted, snapshotsTaken);
    }

    // Called holding flushLock, which it releases.
    private async Task SaveChunksUnderLockAsync(
        IReadOnlyList<IChunk> chunks,
        long admitted,
        List<IChunk> snapshotted,
        TaskCompletionSource snapshotsTaken)
    {
        var taken = new List<ChunkSave>();
        try
        {
            try
            {
                var saves = chunks.Select(chunk => (chunk, NeedsEntitySave(chunk)));
                var snapshots = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var saving = this.SaveTogetherAsync(saves, unloading: false, taken, snapshots, wait: false);

                // The batch takes no more saves once its snapshots are taken.
                await snapshots.Task;
                snapshotted.AddRange(taken.Select(save => save.Chunk));
                snapshotsTaken.TrySetResult();

                await saving;
            }
            finally
            {
                this.flushLock.Release();
            }
        }
        finally
        {
            snapshotsTaken.TrySetResult();
            this.EndSave(admitted);

            foreach (var save in taken)
            {
                save.Dispose();
                await this.ReleaseRetiredAsync(save);
            }
        }
    }

    /// <summary>
    /// Whether an eager save writes the chunk's entities with its blocks: while it's complete and the region hasn't
    /// written its entities (see <see cref="Chunk.SavedEntityCount"/>). Until then, those of a chunk completed since it
    /// was loaded are only in memory, or in its proto record's <c>entities</c>, which saving the complete chunk empties:
    /// saved without them, they'd be lost if the server stopped before the autosave or an unload wrote them. A chunk
    /// loaded complete has them in its entity file already, and only writes them once more.
    /// </summary>
    private static bool NeedsEntitySave(IChunk chunk) => chunk is Chunk { IsGenerated: true, SavedEntityCount: < 0 };

    private async Task WriteChangesUnderLockAsync(List<ChunkSave> taken, TaskCompletionSource? snapshotsTaken)
    {
        // Pending light goes in first, so a chunk it reaches counts as changed.
        lock (this.FluidTickLock)
            this.FlushLight?.Invoke();

        var entityChunks = this.ChunksWithEntities();
        await this.SaveTogetherAsync(
            this.ChunksToFlush(entityChunks),
            unloading: this.lightingRetired,
            taken,
            snapshotsTaken);
    }

    /// <summary>
    /// The chunks a flush saves, with whether it saves their entities, each decided just before its snapshot. Like
    /// vanilla, only chunks that changed since their last save are written, and only chunks with entities, or that had
    /// some, write their entities. A removed region's final save still retires every chunk.
    /// </summary>
    private IEnumerable<(IChunk Chunk, bool Entities)> ChunksToFlush(HashSet<(int X, int Z)> entityChunks)
    {
        foreach (Chunk chunk in this.loadedChunks.Cast<Chunk>())
        {
            var entities = this.MayHaveEntitiesToSave(chunk, entityChunks);
            if (!this.lightingRetired && !entities && !chunk.IsUnsaved)
                continue;

            yield return (chunk, entities);
        }
    }

    /// <summary>
    /// Saves <paramref name="chunks"/> as one batch, adding each save to <paramref name="taken"/> as it's taken: the
    /// snapshots are taken one at a time, in order, each holding the tick lock only while its chunk is serialized;
    /// compressing and staging them runs in parallel, while the next ones are taken. They're committed together, for
    /// two flushes per file however many chunks changed.
    /// </summary>
    /// <remarks>
    /// Called holding <see cref="flushLock"/>: staging holds one of the <see cref="flushSlots"/> while it waits for its
    /// chunk's turn, so two batches of one region taking turns of the same chunks could each wait for slots the other
    /// holds until its commit.
    /// </remarks>
    /// <param name="unloading">Whether the chunks unload (see <see cref="SnapshotChunkAsync"/>).</param>
    /// <param name="snapshotsTaken">Completed once every snapshot is taken (see <see cref="WriteChangesAsync"/>).</param>
    /// <param name="wait">
    /// False to leave chunks unsaved instead of waiting: it stops at the first chunk no staging slot is free for, and
    /// leaves out a chunk whose generator lock is held. An eager save runs while the level's tick waits for its
    /// snapshots, which slow writes elsewhere, or a generation step, would otherwise hold up.
    /// </param>
    private async Task SaveTogetherAsync(IEnumerable<(IChunk Chunk, bool Entities)> chunks, bool unloading,
        List<ChunkSave> taken, TaskCompletionSource? snapshotsTaken = null, bool wait = true)
    {
        var staging = new List<Task>();
        Task completing = Task.CompletedTask;
        try
        {
            foreach (var (chunk, entities) in chunks)
            {
                if (wait)
                    await flushSlots.WaitAsync();
                else if (!flushSlots.Wait(0))
                    break;

                var save = NewSave(chunk);
                taken.Add(save);
                bool snapshotted;
                try
                {
                    snapshotted = await this.SnapshotChunkAsync(save, unloading, saveEntities: entities, wait);
                }
                catch
                {
                    flushSlots.Release();
                    throw;
                }

                if (!snapshotted)
                {
                    // Its generator lock was held, and nothing was taken.
                    taken.RemoveAt(taken.Count - 1);
                    save.Dispose();
                    flushSlots.Release();
                    continue;
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
            // Each snapshot holds what its chunk is written with, and its write turn orders later saves after it.
            snapshotsTaken?.TrySetResult();

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
    internal async Task SyncToDiskAsync()
    {
        await this.initialization.Value;
        await Task.WhenAll(this.regionFile.FlushAsync(), this.entityRegionFile.FlushAsync());
    }

    public async ValueTask<IChunk> GetChunkAsync(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is not null)
            return chunk;

        // A chunk neither pending nor in the file can't be loaded, so there's no read to serialize. Both only gain
        // chunks that were loaded, which are found above.
        if (!await this.HasStoredChunkAsync(x, z))
            return null!;

        await chunkSlotLock.WaitAsync();
        try
        {
            while (true)
            {
                long version;
                lock (this.FluidTickLock)
                {
                    if (loadedChunks[x, z] is IChunk resident)
                        return resident;

                    version = this.VersionOf(x, z);
                }

                var (fromDisk, pending) = await this.ReadStoredChunkAsync(x, z);
                lock (this.FluidTickLock)
                {
                    if (loadedChunks[x, z] is IChunk resident)
                        return resident;

                    // Stored and unloaded again while this read ran (see SetChunk), so what it read may be older than
                    // the pending write that unload left.
                    if (this.VersionOf(x, z) != version)
                        continue;

                    if (fromDisk is not null)
                        this.PublishChunk(x, z, fromDisk, fromDisk: true);

                    this.AdoptIfPublished(x, z, fromDisk, pending);
                    return fromDisk!;
                }
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

        // Reading the files waits for them (see ReadCompoundAsync); a pending write is read without them.
        await chunkSlotLock.WaitAsync();
        try
        {
            while (true)
            {
                long version;
                lock (this.FluidTickLock)
                {
                    if (loadedChunks[x, z] is IChunk resident)
                        return resident;

                    version = this.VersionOf(x, z);
                }

                var (fromDisk, pending) = await this.ReadStoredChunkAsync(x, z);
                var stored = fromDisk ?? create();
                lock (this.FluidTickLock)
                {
                    if (loadedChunks[x, z] is IChunk resident)
                        return resident;

                    // Stored and unloaded again while this read ran (see GetChunkAsync). Retried, and create runs
                    // again, as often as that keeps happening: the factories only allocate, and a slot refilled and
                    // emptied every read is a slot being unloaded every time, which ends with its unloads.
                    if (this.VersionOf(x, z) != version)
                        continue;

                    chunk = stored;
                    this.PublishChunk(x, z, chunk, ReferenceEquals(chunk, fromDisk));
                    this.AdoptIfPublished(x, z, fromDisk, pending);
                    return chunk;
                }
            }
        }
        finally
        {
            chunkSlotLock.Release();
        }
    }

    /// <summary>Reads reference metadata without publishing a chunk or loading its entities.</summary>
    internal async ValueTask<SavedStructureStart[]> ReadSavedStructureStartsAsync(int chunkX, int chunkZ)
    {
        var x = NumericsHelper.Modulo(chunkX, CubicRegionSize);
        var z = NumericsHelper.Modulo(chunkZ, CubicRegionSize);
        if (this.loadedChunks[x, z] is Chunk { StructureStarts: NbtCompound loaded })
            return SavedStructureStart.Read(loaded);

        // Not cached: the file has another version until the write commits, which drops the cached one.
        if (this.FindPending(x, z) is { Chunk: byte[] pending })
            return ReadStarts(ParseCompound(pending));

        if (!await this.FileHasChunkAsync(x, z))
            return [];

        var key = x + z * CubicRegionSize;
        var read = this.savedStartBounds.GetOrAdd(key, static (position, region) =>
            new Lazy<Task<SavedStructureStart[]>>(() => region.ReadSavedStartBoundsAsync(position)), this);
        try
        {
            return await read.Value;
        }
        catch
        {
            this.savedStartBounds.TryRemove(new KeyValuePair<int, Lazy<Task<SavedStructureStart[]>>>(key, read));
            throw;
        }
    }

    private async Task<SavedStructureStart[]> ReadSavedStartBoundsAsync(int position)
    {
        var chunk = await this.ReadCompoundAsync(entities: false, position % CubicRegionSize, position / CubicRegionSize);
        return ReadStarts(chunk);
    }

    private static SavedStructureStart[] ReadStarts(NbtCompound? chunk)
    {
        if (chunk is null || !chunk.TryGetTag<NbtCompound>("structures", out var structures)
            || !structures.TryGetTag<NbtCompound>("starts", out var starts))
        {
            return [];
        }

        return SavedStructureStart.Read(starts);
    }

    /// <summary>
    /// Loads metadata into an unsealed region, including during final-save I/O. Only a fresh load that still owns
    /// the slot's version can publish into a retired region; a delayed notification cannot reinstall an old chunk.
    /// Returns null when sealing requires the caller to wait for removal and retry with a new region.
    /// </summary>
    internal async ValueTask<Chunk?> GetStructureChunkAsync(int chunkX, int chunkZ)
    {
        var x = NumericsHelper.Modulo(chunkX, CubicRegionSize);
        var z = NumericsHelper.Modulo(chunkZ, CubicRegionSize);
        lock (this.FluidTickLock)
        {
            if (this.structureChangesSealed)
                return null;
            if (this.loadedChunks[x, z] is Chunk resident)
                return resident;
        }

        // Reading the files waits for them (see ReadCompoundAsync); a pending write is read without them.
        await this.chunkSlotLock.WaitAsync();
        try
        {
            while (true)
            {
                long version;
                lock (this.FluidTickLock)
                {
                    if (this.structureChangesSealed)
                        return null;
                    if (this.loadedChunks[x, z] is Chunk resident)
                        return resident;
                    version = this.VersionOf(x, z);
                }

                var (fromDisk, pending) = await this.ReadStoredChunkAsync(x, z);
                var chunk = fromDisk ?? new Chunk(chunkX, chunkZ, this.minY, this.height, ChunkGenStage.structure_starts);
                lock (this.FluidTickLock)
                {
                    if (this.structureChangesSealed)
                        return null;
                    if (this.loadedChunks[x, z] is Chunk resident)
                        return resident;
                    if (this.VersionOf(x, z) != version)
                        continue;

                    this.PublishChunk(x, z, chunk, fromDisk is not null, allowRetired: true);
                    this.AdoptIfPublished(x, z, fromDisk, pending);
                    return chunk;
                }
            }
        }
        finally
        {
            this.chunkSlotLock.Release();
        }
    }

    /// <summary>
    /// Unloads the chunk at the local coordinates: takes its final save and takes it out of the region in one step (see
    /// <see cref="DetachForUnloadAsync"/>), then writes the save (see <see cref="WriteUnloadedAsync"/>).
    /// </summary>
    public async Task UnloadChunk(int x, int z)
    {
        var unloaded = await this.DetachForUnloadAsync([(x, z)]);
        await this.WriteUnloadedAsync(unloaded);
    }

    /// <summary>
    /// Unloads the chunks at the local coordinates like <see cref="UnloadChunk"/>, but together, so each file is
    /// committed once however many unload. A chunk whose write fails stays pending for the level's next save to retry;
    /// the others are written, then the failure is thrown.
    /// </summary>
    internal async Task UnloadChunksAsync(IEnumerable<(int X, int Z)> positions)
    {
        var unloaded = await this.DetachForUnloadAsync([.. positions.Distinct()]);
        await this.WriteUnloadedAsync(unloaded);
    }

    /// <summary>
    /// Takes the final save of each chunk at the local coordinates and takes the chunk out of the region in the same
    /// step, like vanilla's chunk map does as it unloads a chunk: its block access, block entities, entities and lighting
    /// are retired, its save becomes the level's pending write for its position (see <see cref="PendingWrites"/>), which
    /// loads read from then on, and its slot is emptied. Then its block entities hear their chunk unloaded.
    /// </summary>
    /// <remarks>
    /// The level calls this under its simulation gate, so no tick sees a chunk between its snapshot and its removal, and
    /// the unload callbacks run between ticks like every other block entity callback. Nothing can change the chunk
    /// afterwards, so the write (<see cref="WriteUnloadedAsync"/>) needs no further checks, and runs outside the gate.
    /// </remarks>
    internal async Task<UnloadedChunks> DetachForUnloadAsync(IReadOnlyList<(int X, int Z)> positions)
    {
        await this.initialization.Value;

        // Admitted before the snapshots, so the region doesn't close before their writes (see WriteUnloadedAsync).
        var unloaded = new UnloadedChunks(this.AdmitSave(unload: true));
        List<ChunkSave> retired;
        try
        {
            retired = await this.DetachAdmittedAsync(positions, unloaded);
        }
        catch
        {
            this.EndSave(unloaded.Admitted);
            throw;
        }

        // The writes start before the callbacks, and go on without them: a callback may load a chunk unloaded here
        // and save it, and its writes wait for these, whose turns come first.
        unloaded.Written = this.WriteDetachedAsync(unloaded);
        try
        {
            foreach (var save in retired)
                await this.ReleaseRetiredAsync(save);
        }
        catch (Exception callbackFailure)
        {
            // The callback's failure comes first, but the write's and the snapshots' are reported with it, as
            // WriteUnloadedAsync would have, since the caller won't reach it.
            var written = await unloaded.Written;
            ThrowFailures([callbackFailure, .. unloaded.Failures, .. written]);
            throw;
        }

        return unloaded;
    }

    // Takes and detaches the chunks; returns every save that retired something, for the callbacks to let go of.
    private async Task<List<ChunkSave>> DetachAdmittedAsync(IReadOnlyList<(int X, int Z)> positions,
        UnloadedChunks unloaded)
    {
        var retired = new List<ChunkSave>();
        foreach (var (x, z) in positions)
        {
            // The position is claimed, not the instance stored there: generation may replace the chunk while the
            // snapshot waits for its lock, and an overlapping unload must leave the replacement to this one.
            var slot = (NumericsHelper.Modulo(x, CubicRegionSize), NumericsHelper.Modulo(z, CubicRegionSize));
            IChunk? chunk;
            lock (this.FluidTickLock)
            {
                chunk = this.loadedChunks[x, z];
                if (chunk is null || !this.unloadingSlots.Add(slot))
                    continue;
            }

            try
            {
                var save = NewSave(chunk);
                retired.Add(save);
                try
                {
                    await this.SnapshotChunkAsync(save, unloading: true, saveEntities: true);
                }
                catch (Exception failure)
                {
                    // The chunk stays, retired like its region's chunks, and its region's next save writes it, with
                    // the marks and entities the snapshot put back; what it retired is let go all the same.
                    unloaded.Failures.Add(failure);
                    continue;
                }

                unloaded.Saves.Add((save, this.Detach(save)));
            }
            finally
            {
                lock (this.FluidTickLock)
                    this.unloadingSlots.Remove(slot);
            }
        }

        return retired;
    }

    // Writes the detached chunks' saves (see WriteAdmittedAsync), then ends the unload's admission.
    private async Task<List<Exception>> WriteDetachedAsync(UnloadedChunks unloaded)
    {
        // Off the caller's thread, which runs the callbacks meanwhile.
        await Task.Yield();

        var failures = new List<Exception>();
        try
        {
            await this.WriteAdmittedAsync(unloaded.Saves, failures);
        }
        finally
        {
            this.EndSave(unloaded.Admitted);
        }

        return failures;
    }

    // Publishes the chunk's save as its position's pending write and empties its slot, atomically for readers: one that
    // finds the slot empty finds the save. The save's turns were reserved with its snapshot, so its write comes after
    // every earlier write of the chunk, and before any of the chunk loaded from it.
    private PendingChunkWrite Detach(ChunkSave save)
    {
        var chunk = save.Snapshotted!;
        var pending = new PendingChunkWrite(
            chunk.X,
            chunk.Z,
            save.Nbt?.Written.ToArray(),
            save.EntityNbt?.Written.ToArray(),
            save.EntityCount);

        lock (this.FluidTickLock)
        {
            this.PendingWrites.Add(pending);

            if (ReferenceEquals(this.loadedChunks[save.X, save.Z], chunk))
            {
                this.DetachLighting(chunk);
                this.loadedChunks[save.X, save.Z] = null;
                this.chunkVersions[save.X, save.Z]++;
                this.retiredBlockAccess.Remove(chunk);
                this.StructureChunkUnloaded?.Invoke(chunk);
            }
        }

        return pending;
    }

    /// <summary>
    /// Waits for the writes of the saves <see cref="DetachForUnloadAsync"/> took, which it started, together, outside
    /// the level's simulation gate. Each
    /// committed save leaves the pending writes; one whose write fails stays there, flagged failed, so loads still read
    /// it and the level's next save retries it (see <see cref="RetryPendingWritesAsync"/>). Failures, the snapshots'
    /// too, are thrown once every save was written or failed.
    /// </summary>
    internal async Task WriteUnloadedAsync(UnloadedChunks unloaded)
    {
        var written = await unloaded.Written!;
        ThrowFailures([.. unloaded.Failures, .. written]);
    }

    /// <summary>
    /// Writes again the pending saves of this region's positions whose writes failed (see
    /// <see cref="PendingChunkWrites.Failed"/>). A save a later unload replaced, or that a chunk was loaded from (which
    /// saves itself), is left out.
    /// </summary>
    internal async Task RetryPendingWritesAsync(IEnumerable<PendingChunkWrite> writes)
    {
        await this.initialization.Value;

        var saves = new List<(ChunkSave Save, PendingChunkWrite Pending)>();
        lock (this.FluidTickLock)
        {
            foreach (var pending in writes)
            {
                var x = NumericsHelper.Modulo(pending.ChunkX, CubicRegionSize);
                var z = NumericsHelper.Modulo(pending.ChunkZ, CubicRegionSize);
                if (this.loadedChunks[x, z] is not null || !this.PendingWrites.IsCurrent(pending))
                    continue;

                saves.Add((this.SaveOf(pending, x, z), pending));
            }
        }

        var failures = new List<Exception>();
        long admitted;
        try
        {
            admitted = this.AdmitSave();
        }
        catch
        {
            foreach (var (save, _) in saves)
                save.Dispose();

            throw;
        }

        try
        {
            await this.WriteAdmittedAsync(saves, failures);
        }
        finally
        {
            this.EndSave(admitted);
        }

        ThrowFailures(failures);
    }

    // A save of a pending write, with its turns. Call under FluidTickLock, which orders it with the chunk's loads.
    private ChunkSave SaveOf(PendingChunkWrite pending, int x, int z)
    {
        var save = new ChunkSave { X = x, Z = z };
        if (pending.Chunk is byte[] chunk)
        {
            save.Nbt = new PooledBufferStream(chunk.Length);
            save.Nbt.Write(chunk);
            save.ChunkWrite = this.regionFile.ReserveWrite(x, z);
        }

        if (pending.Entities is byte[] entities)
        {
            save.EntityNbt = new PooledBufferStream(entities.Length);
            save.EntityNbt.Write(entities);
            save.EntityCount = pending.EntityCount;
            save.EntityWrite = this.entityRegionFile.ReserveWrite(x, z);
        }

        return save;
    }

    // Stages the saves one at a time, each in its turns, commits them together, then settles each pending write. The
    // caller admitted them (see AdmitSave), so the region stays open until they end.
    private async Task WriteAdmittedAsync(List<(ChunkSave Save, PendingChunkWrite Pending)> saves,
        List<Exception> failures)
    {
        try
        {
            try
            {
                foreach (var (save, _) in saves)
                {
                    try
                    {
                        await this.StageSaveAsync(save);
                    }
                    catch (Exception failure)
                    {
                        failures.Add(failure);
                    }
                }
            }
            finally
            {
                // Commits with the writes other saves staged meanwhile; failures reach each save's writes.
                await this.CommitAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            foreach (var (save, pending) in saves)
            {
                try
                {
                    await this.CompleteSaveAsync(save);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }

                if (save.Saved)
                    this.PendingWrites.Remove(pending);
                else
                    pending.Failed = true;
            }
        }
        finally
        {
            foreach (var (save, _) in saves)
                save.Dispose();
        }
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
            ExceptionDispatchInfo.Throw(failures[0]);

        if (failures.Count > 1)
            throw new AggregateException(failures);
    }

    /// <summary>
    /// The final saves an unload took (see <see cref="DetachForUnloadAsync"/>), with the pending writes they became, and
    /// the snapshots that failed, whose chunks stayed.
    /// </summary>
    internal sealed class UnloadedChunks(long admitted)
    {
        // The unload's admission (see AdmitSave), which its write ends.
        internal long Admitted { get; } = admitted;

        internal List<(ChunkSave Save, PendingChunkWrite Pending)> Saves { get; } = [];

        internal List<Exception> Failures { get; } = [];

        // The saves' writes, started as the chunks were detached, with their failures.
        internal Task<List<Exception>>? Written { get; set; }
    }

    /// <summary>Validates a captured chunk under FluidTickLock: an unloaded one left its slot with its final snapshot.</summary>
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
        using (this.EntityTransfers.Enter())
            return this.EvictUnderTransferLock(entity, keepSaved);
    }

    /// <summary>
    /// Keeps an entity that travelled but no level could take in, in its chunk, which saves it: as one waiting to
    /// spawn on the level's next tick, or, while another entity in the level has its UUID (which would drop it there),
    /// as one kept as saved until the chunk loads again. Call holding the level's transfer lock.
    /// </summary>
    /// <returns>Whether the chunk, loaded and with its entities not unloaded, keeps it.</returns>
    internal bool TryKeepUnplaced(Entity entity, AbstractLevel level)
    {
        var (chunkX, chunkZ) = ChunkOf(entity.Position);
        var local = this.GetLoadedChunk(
            NumericsHelper.Modulo(chunkX, CubicRegionSize),
            NumericsHelper.Modulo(chunkZ, CubicRegionSize));
        if (local is not Chunk { X: var x, Z: var z } chunk || (x, z) != (chunkX, chunkZ))
            return false;

        using (chunk.EntityLock.EnterScope())
        {
            if (chunk.EntitiesUnloaded
                || EntityNbt.Save(entity) is not NbtCompound saved
                || EntityNbt.ToGeneratedEntity(saved) is not GeneratedEntity pending)
            {
                return false;
            }

            if (level.FindEntity(entity.Uuid) is null)
                chunk.PendingEntities.Add(pending);
            else
                chunk.UnspawnableEntities.Add(saved);
        }

        API.Data.AttachmentStorage.For(entity).Retire();
        level.QueueEntitySpawn(chunk);
        return true;
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

    // How many times the slot at the position changed (see PublishChunk), by local or level coordinates.
    private long VersionOf(int x, int z) =>
        this.chunkVersions[NumericsHelper.Modulo(x, CubicRegionSize), NumericsHelper.Modulo(z, CubicRegionSize)];

    // The position's pending write (see PendingWrites), newer than the files while it's there. Callers may give the
    // chunk's local or level coordinates, like the slots and files take.
    private PendingChunkWrite? FindPending(int x, int z) => this.PendingWrites.Find(
        this.X * CubicRegionSize + NumericsHelper.Modulo(x, CubicRegionSize),
        this.Z * CubicRegionSize + NumericsHelper.Modulo(z, CubicRegionSize));

    // Whether a chunk is stored at the position: pending, or in the file.
    private async ValueTask<bool> HasStoredChunkAsync(int x, int z)
    {
        if (this.FindPending(x, z) is not null)
            return true;

        return await this.FileHasChunkAsync(x, z);
    }

    /// <summary>
    /// Reads the chunk stored at the position, with its entities: from its pending write when there's one, which is
    /// newer than the files, and from the files otherwise. A write committing meanwhile leaves the files as new.
    /// </summary>
    /// <returns>The chunk, or null when none is stored, and the pending write it was read from.</returns>
    private async Task<(Chunk? Chunk, PendingChunkWrite? Pending)> ReadStoredChunkAsync(int x, int z)
    {
        var pending = this.FindPending(x, z);
        var chunkCompound = pending?.Chunk is byte[] chunkNbt
            ? ParseCompound(chunkNbt)
            : await this.ReadCompoundAsync(entities: false, x, z);
        if (chunkCompound is null)
            return (null, pending);

        var chunk = DeserializeChunk(chunkCompound);

        // Entities of complete chunks are in the entity region file; they spawn like generated ones.
        if (chunk.IsGenerated)
        {
            var entityChunk = pending?.Entities is byte[] entityNbt
                ? ParseCompound(entityNbt)
                : await this.ReadCompoundAsync(entities: true, x, z);
            if (entityChunk is not null && entityChunk.TryGetTag<NbtList>("Entities", out var entities))
            {
                foreach (var entity in entities.OfType<NbtCompound>())
                {
                    if (entity.TryGetTagValue<string>("id", out var id) && EntityNbt.IsKnownType(id) &&
                        EntityNbt.ToGeneratedEntity(entity) is GeneratedEntity spawning)
                    {
                        chunk.PendingEntities.Add(spawning);
                    }
                    else
                    {
                        chunk.UnspawnableEntities.Add(entity);
                    }
                }
            }
        }

        return (chunk, pending);
    }

    private static NbtCompound? ParseCompound(byte[] nbt) => NbtReader.ReadTag(nbt, readName: true, out _) as NbtCompound;

    /// <summary>
    /// Once a chunk read from a pending write is stored in its slot, drops the pending write: the chunk, unsaved as
    /// every loaded chunk is, saves itself, and its writes come after the pending one's, whose turns were reserved
    /// first. Call under <see cref="FluidTickLock"/>.
    /// </summary>
    private void AdoptIfPublished(int x, int z, Chunk? read, PendingChunkWrite? pending)
    {
        if (pending is not null && read is not null && ReferenceEquals(this.loadedChunks[x, z], read))
            this.PendingWrites.Remove(pending);
    }

    // Reads the chunk file, or the entity file: through the predecessor while it still has them open (see the
    // constructor), then once this region opened them.
    private async Task<NbtCompound?> ReadCompoundAsync(bool entities, int x, int z)
    {
        RentedArray<byte>? data = null;
        var read = false;
        if (this.predecessor is Region previous)
        {
            try
            {
                var shared = entities ? previous.entityRegionFile : previous.regionFile;
                data = await shared.RentChunkBytesAsync(x, z);
                read = true;
            }
            catch (ObjectDisposedException)
            {
                // It closed them meanwhile, so this region opens them.
            }
        }

        if (!read)
        {
            await this.initialization.Value;
            var file = entities ? this.entityRegionFile : this.regionFile;
            data = await file.RentChunkBytesAsync(x, z);
        }

        if (data is not RentedArray<byte> rented)
            return null;

        // Parsed in place: tags copy what they keep (arrays, strings), so the rented bytes go back once it's read.
        using (rented)
            return NbtReader.ReadTag(rented.Span, readName: true, out _) as NbtCompound;
    }

    // Whether the chunk file has the chunk, read like ReadCompoundAsync. A closed file still answers as it closed.
    private async ValueTask<bool> FileHasChunkAsync(int x, int z)
    {
        if (this.predecessor is Region previous)
            return previous.regionFile.HasChunk(x, z);

        await this.initialization.Value;
        return this.regionFile.HasChunk(x, z);
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
    private void PublishChunk(int x, int z, IChunk chunk, bool fromDisk = false, bool allowRetired = false)
    {
        if (this.lightingRetired && !allowRetired)
            return;

        // Pin the replacement before releasing the previous instance at this position.
        this.StructureChunkLoaded?.Invoke(chunk);
        if (!ReferenceEquals(this.loadedChunks[x, z], chunk))
        {
            var slotX = NumericsHelper.Modulo(x, CubicRegionSize);
            var slotZ = NumericsHelper.Modulo(z, CubicRegionSize);
            this.chunkVersions[slotX, slotZ]++;
        }
        if (this.loadedChunks[x, z] is IChunk previous && !ReferenceEquals(previous, chunk))
        {
            this.DetachLighting(previous);
            this.retiredBlockAccess.Remove(previous);
            this.StructureChunkUnloaded?.Invoke(previous);
        }

        if (chunk is Chunk concrete)
            concrete.CompleteGeneration = this.CompleteChunk;

        // Lock-free readers must not see a complete chunk until all its section hooks are ready.
        if (!this.lightingRetired)
            this.LightLoaded?.Invoke(chunk, false);

        this.loadedChunks[x, z] = chunk;
        if (this.lightingRetired)
            this.DetachLighting(chunk);

        // Entity initialization reads this slot; queue it only after publication and lighting attachment.
        if (!this.lightingRetired && fromDisk && chunk is Chunk { IsGenerated: true } complete)
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

    /// <summary>
    /// Retires the region as the level removes it, before its final snapshot: flushes final light and detaches every
    /// chunk from lighting and block access, stops loads and generation from publishing chunks into it, and seals
    /// structure notifications, which wait for <see cref="RegionUnload"/> and go to a fresh region object.
    /// </summary>
    internal void Retire()
    {
        lock (this.FluidTickLock)
        {
            this.regionUnload ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.lightingRetired = true;
            this.structureChangesSealed = true;
            this.FlushLight?.Invoke();
            foreach (var chunk in this.loadedChunks)
                this.DetachLighting(chunk);
        }
    }

    /// <summary>
    /// Takes back <see cref="Retire"/> when the region's final snapshot failed, so the level keeps it: loads and
    /// generation publish into it again, structure notifications reach it again, and those waiting for its unload go
    /// on. The chunks still in it stay retired, as a chunk whose own unload snapshot failed does: they don't tick, the
    /// region's next save writes them, and a later unload takes them.
    /// </summary>
    internal void Unretire()
    {
        TaskCompletionSource? waiting;
        lock (this.FluidTickLock)
        {
            this.lightingRetired = false;
            this.structureChangesSealed = false;
            waiting = this.regionUnload;
            this.regionUnload = null;
        }

        waiting?.TrySetResult();
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

    private static ChunkSave NewSave(IChunk chunk) => new()
    {
        Chunk = chunk,
        X = NumericsHelper.Modulo(chunk.X, CubicRegionSize),
        Z = NumericsHelper.Modulo(chunk.Z, CubicRegionSize)
    };

    /// <summary>
    /// Takes what a save writes of a chunk into <paramref name="save"/>: its NBT, uncompressed, and its entities, as of
    /// one moment under the tick lock, and its turn to be written to each file, in the order snapshots are taken. An
    /// unloading save also serializes a chunk unchanged since its last save while that save is still being written, so
    /// the pending write it becomes (see <see cref="Detach"/>) holds the chunk whatever that write's outcome.
    /// </summary>
    /// <param name="waitForLock">False to take nothing when the generator's lock of the chunk is held.</param>
    /// <returns>Whether the snapshot was taken, which it is unless <paramref name="waitForLock"/> is false.</returns>
    private async Task<bool> SnapshotChunkAsync(ChunkSave save, bool unloading, bool saveEntities,
        bool waitForLock = true)
    {
        var canWait = !this.FluidTickLock.IsHeldByCurrentThread
            && !EntityTransferLock.IsAnyHeldByCurrentThread;
        while (true)
        {
            var (taken, busy) = await this.TrySnapshotChunkAsync(save, unloading, saveEntities, waitForLock);
            if (taken || busy is null || !waitForLock)
                return taken;

            if (!canWait)
            {
                throw new InvalidOperationException(
                    "A busy snapshot cannot wait while its caller holds tick or transfer.");
            }

            // No dirty marks, retirement or writes were taken. Wait without generator, tick, transfer or entity
            // locks, then retry against current membership. Eager saves leave the whole chunk for a later save.
            this.InventorySnapshotDeferred?.Invoke();
            using (await busy.LockAsync())
            {
            }
        }
    }

    private async Task<(bool Taken, BaseContainer? Busy)> TrySnapshotChunkAsync(
        ChunkSave save, bool unloading, bool saveEntities, bool waitForLock)
    {
        var (chunk, x, z) = (save.Chunk!, save.X, save.Z);
        NbtWriterStream? writer = null;
        try
        {
            // Snapshot blocks, fluid ticks and entities together; take the generator's lock before the fluid lock.
            var (locked, chunkLock) = await this.LockChunkAsync(chunk, waitForLock);
            if (!locked)
                return (false, null);

            using (chunkLock)
            {
                lock (this.FluidTickLock)
                {
                    this.FlushLight?.Invoke();

                    // The chunk stored now, read under the lock: generation may have replaced the one this save was
                    // given while it waited (see ReplaceChunk). One no longer stored was unloaded and is saved as it
                    // was.
                    var current = this.loadedChunks[x, z] ?? chunk;
                    save.Snapshotted = current;

                    // Structure state may call plugin serializers. Preserve its existing tick-lock scope, but
                    // finish it before the host-only inventory admission adds transfer and inventory locks.
                    var loadedStarts = (current as Chunk)?.StructureStarts;
                    var mayWrite = current is not Chunk trackedStarts || trackedStarts.IsUnsaved
                        || unloading && this.regionFile.HasWriteInFlight(x, z);
                    var structureStarts = loadedStarts;
                    ExceptionDispatchInfo? structureFailure = null;
                    if (this.SaveStructureStarts is not null && mayWrite)
                    {
                        try
                        {
                            structureStarts = this.SaveStructureStarts(current.X, current.Z, loadedStarts);
                        }
                        catch (Exception failure)
                        {
                            // Preserve the original failure/retirement cleanup point without running the
                            // serializer while the host admission owns extra locks.
                            structureFailure = ExceptionDispatchInfo.Capture(failure);
                        }
                    }

                    using var transfers = this.EntityTransfers.Enter();
                    this.BeforeInventorySnapshot?.Invoke();
                    var inventories = this.SnapshotInventories(current, unloading || saveEntities);
                    if (!InventorySnapshot.TryEnter(inventories, out var admitted, out var busy))
                        return (false, busy);

                    // Only host serializers run here. All inventory acquisition, including hashing dirty state
                    // and retirement, reuses this admission. No partial unload can precede a busy inventory.
                    using var snapshot = admitted;

                    // No block hooks or async setters may change this instance after its final snapshot.
                    if (unloading && ReferenceEquals(current, chunk))
                        this.RetireBlockAccess(current);

                    if (unloading && current is Chunk retiring)
                        BlockEntityLifecycle.Retire(retiring, save.Retired, save.Unloaded);

                    // Taken after the light flush, which may write into the chunk, and the containers' retirement, which
                    // freezes their items. A chunk unchanged since its last save is on disk as it is, so only its
                    // entities may need saving.
                    //
                    // An unloading save also takes a chunk unchanged since a save still being written, whose write may
                    // fail. Checked before the marks are taken: with none in flight, every earlier write has ended,
                    // and a failed one put the chunk's marks back before it did (see RegionFile.ChunkWrite.Failed), so
                    // the marks have it. No write of the chunk is reserved meanwhile: that takes this lock.
                    var earlierInFlight = unloading && this.regionFile.HasWriteInFlight(x, z);
                    var write = current is not Chunk tracked || tracked.TakeUnsaved();
                    if (unloading)
                        this.UnloadMarksTaken?.Invoke();

                    write |= earlierInFlight;

                    if (write)
                    {
                        save.Taken = current as Chunk;
                        structureFailure?.Throw();

                        // Only serialized here, into memory; compressing waits until the lock is released.
                        save.Nbt = new PooledBufferStream(UncompressedChunkCapacity);
                        writer = new NbtWriterStream(save.Nbt, NbtCompression.None, "");
                        SerializeChunk(writer.Value, current, structureStarts);
                    }

                    if (write)
                    {
                        save.ChunkWrite = this.regionFile.ReserveWrite(x, z);
                        if (save.Taken is Chunk taken)
                            save.ChunkWrite.Failed = taken.MarkUnsaved;
                    }

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

            // Entities may keep parts of their live NBT in what they save, which the level may change once the
            // snapshot is taken (see AbstractLevel.FlushRegionsAsync), so they're serialized with it too.
            if (save.Entities is NbtList entities)
            {
                save.EntityCount = entities.Count;
                save.EntityNbt = SerializeEntities(chunk.X, chunk.Z, entities);
                save.Entities = null;
            }

            return (true, null);
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

    // Called with tick and transfer held: vehicle membership cannot change before the snapshot consumes it.
    private IEnumerable<BaseContainer> SnapshotInventories(IChunk chunk, bool saveEntities)
    {
        foreach (var container in chunk.GetBlockEntities().OfType<ContainerBlockEntity>())
            yield return container.Inventory;

        if (!saveEntities || !chunk.IsGenerated)
            yield break;

        foreach (var (_, entity) in this.entities)
        {
            if (entity is IVehicleContainer vehicle && ChunkOf(entity.Position) == (chunk.X, chunk.Z))
                yield return vehicle.Inventory;
        }
    }

    /// <summary>
    /// Takes the generator's lock of the chunk (see <see cref="LockChunk"/>), or, when <paramref name="wait"/> is false
    /// and it's held, gives up.
    /// </summary>
    /// <returns>Whether it's taken, and its handle: null when the generator doesn't lock chunks.</returns>
    private async ValueTask<(bool Taken, IDisposable? Handle)> LockChunkAsync(IChunk chunk, bool wait)
    {
        if (this.LockChunk is null)
            return (true, null);

        var locking = this.LockChunk(chunk.X, chunk.Z);
        if (wait || locking.IsCompleted)
            return (true, await locking);

        // Generation holds it, maybe for a whole step. A generator's lock can't be tried, so the wait goes on, and the
        // lock is given back once it's taken.
        _ = ReleaseOnceTakenAsync(locking.AsTask());
        return (false, null);
    }

    // Failing to take the lock leaves nothing held; the next save that takes it meets the failure again.
    private static async Task ReleaseOnceTakenAsync(Task<IDisposable?> locking)
    {
        await ((Task)locking).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (locking.IsCompletedSuccessfully)
            locking.Result?.Dispose();
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

            if (save.EntityNbt is not null)
                await this.StageEntitiesAsync(save);

            save.Staged = true;
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
    /// Both files' failures are handled before throwing, so the next save retries each: a failure of one file is
    /// rethrown as it is, and failures of both are thrown together in an <see cref="AggregateException"/>, the chunk
    /// file's first.
    /// </summary>
    private async Task CompleteSaveAsync(ChunkSave save)
    {
        if (save.ChunkWrite is { } chunkWrite)
            await chunkWrite.Done;

        if (save.EntityWrite is { } entityWrite)
            await entityWrite.Done;

        // A later metadata read must see the committed version, including starts saved after an earlier read.
        if (save.Nbt is not null)
            this.savedStartBounds.TryRemove(save.X + save.Z * CubicRegionSize, out _);

        var chunkFailure = save.ChunkWrite?.Failure;
        if (chunkFailure is not null)
            save.Taken?.MarkUnsaved();

        // The file may still list the entities its last commit wrote, which the next save must overwrite even when
        // the chunk has none left.
        var entityFailure = save.EntityWrite?.Failure;
        if (entityFailure is not null && save.Chunk is Chunk unknown)
            unknown.SavedEntityCount = -1;

        if (chunkFailure is not null && entityFailure is not null)
            throw new AggregateException(chunkFailure, entityFailure);

        if ((chunkFailure ?? entityFailure) is Exception failure)
            ExceptionDispatchInfo.Throw(failure);

        save.Completed = true;
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
    /// <param name="unload">
    /// Whether it's an unload's, admitted before its snapshot and held through its write: closing waits for it, but a
    /// flush doesn't, since the unload's callbacks run between its snapshot and its write and may flush the region.
    /// </param>
    /// <exception cref="ObjectDisposedException">The region is closing.</exception>
    private long AdmitSave(bool unload = false)
    {
        lock (this.saves)
        {
            ObjectDisposedException.ThrowIf(this.closing, this);

            var admitted = ++this.lastSave;
            this.saves[admitted] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (unload)
                this.unloadSaves.Add(admitted);

            return admitted;
        }
    }

    private void EndSave(long admitted)
    {
        TaskCompletionSource? ended;
        lock (this.saves)
        {
            this.saves.Remove(admitted, out ended);
            this.unloadSaves.Remove(admitted);
        }

        ended?.TrySetResult();
    }

    // Admission order, not the saves themselves, decides what a flush waits for, so two flushes never wait for each
    // other. Unloads aren't waited for (see AdmitSave).
    private Task SavesAdmittedBeforeAsync(long admitted)
    {
        lock (this.saves)
        {
            var before = this.saves
                .Where(save => save.Key < admitted && !this.unloadSaves.Contains(save.Key))
                .Select(save => save.Value.Task);
            return Task.WhenAll(before);
        }
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

        using (this.EntityTransfers.Enter())
        {
            using (chunk.EntityLock.EnterScope())
        {
            if (chunk.EntitiesUnloaded)
                return null;

            foreach (var pending in chunk.PendingEntities)
                entities.Add(EntityNbt.ToNbt(pending));

            foreach (var unsupported in chunk.UnspawnableEntities)
                entities.Add(unsupported);

            var leaving = this.SaveLevelEntities(chunk, entities, unloading);

            // Taken out only once every one is saved: a save that throws leaves them all in the level.
            foreach (var levelEntity in leaving)
            {
                if (this.TryRemoveEntity(levelEntity))
                    EntityGrid.Leave(levelEntity);
            }

            if (unloading)
                chunk.EntitiesUnloaded = true;
        }
        }

        return entities;
    }

    /// <summary>
    /// Adds the saves of the level's entities in the chunk to <paramref name="entities"/>. When unloading, their
    /// attachments retire first, so no write to them is missed, and those to take out of the level are returned. A save
    /// that throws gives back the attachments it retired, so the entities stay in the level as they were.
    /// </summary>
    private List<Entity> SaveLevelEntities(Chunk chunk, NbtList entities, bool unloading)
    {
        var leaving = new List<Entity>();
        var retired = new List<API.Data.AttachmentStorage>();
        try
        {
            foreach (var (_, entity) in this.entities)
            {
                if (entity is not Entity levelEntity || ChunkOf(levelEntity.Position) != (chunk.X, chunk.Z))
                    continue;

                if (unloading && levelEntity is not IPlayer)
                {
                    var attachments = API.Data.AttachmentStorage.For(levelEntity);
                    if (attachments.TryRetire())
                        retired.Add(attachments);
                }

                if (EntityNbt.Save(levelEntity) is not NbtCompound saved)
                {
                    if (unloading && levelEntity is Mob { HasAi: true })
                        leaving.Add(levelEntity);

                    continue;
                }

                entities.Add(saved);
                if (unloading)
                    leaving.Add(levelEntity);
            }
        }
        catch
        {
            foreach (var attachments in retired)
                attachments.Unretire();

            throw;
        }

        return leaving;
    }

    /// <summary>
    /// Stages a chunk's entities like vanilla's <c>EntityStorage</c> writes them: <c>DataVersion</c>, <c>Position</c>
    /// and <c>Entities</c>, in the save's turn, so whether the file has an entry for the chunk is as of the saves
    /// before.
    /// </summary>
    private async Task StageEntitiesAsync(ChunkSave save)
    {
        var nbt = save.EntityNbt!;
        var turn = save.EntityWrite!;

        await turn.TurnAsync;

        // Vanilla deletes the entry of a chunk left without entities; region files can't delete entries, so a chunk that had
        // entities keeps an empty list, and one that never had any gets no entry.
        if (save.EntityCount == 0 && !this.entityRegionFile.HasChunk(save.X, save.Z))
        {
            if (save.Chunk is Chunk empty)
                empty.SavedEntityCount = 0;

            return;
        }

        PooledBufferStream? compressed = null;
        try
        {
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
            saved.SavedEntityCount = save.EntityCount;
    }

    // The entity file's entry for a chunk's entities, uncompressed.
    private static PooledBufferStream SerializeEntities(int chunkX, int chunkZ, NbtList entities)
    {
        var nbt = new PooledBufferStream(4 * 1024);
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

            return nbt;
        }
        catch
        {
            nbt.Return();
            throw;
        }
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
            // Nearly every entity stays in its region, which changes nothing, so that's checked without the lock. One
            // that seems to have left is checked again under it: travel, removal or a save may be taking it meanwhile.
            if (this.Holds(entity.Position))
                continue;

            using (this.EntityTransfers.Enter())
            {
                if (!this.entities.TryGetValue(entity.EntityId, out var resident) || !ReferenceEquals(resident, entity))
                    continue;

                if (this.Holds(entity.Position))
                    continue;

                var (chunkX, chunkZ) = ChunkOf(entity.Position);
                if (entity.Level is AbstractLevel level && level.GetRegionForChunk(chunkX, chunkZ) is Region target
                    && target.TryAddEntity(entity))
                {
                    this.TryRemoveEntity(entity);
                }
            }
        }
    }

    // Whether an entity at the position belongs to this region's chunks.
    private bool Holds(VectorD position)
    {
        var (chunkX, chunkZ) = ChunkOf(position);
        return chunkX >> CubicRegionSizeShift == this.X && chunkZ >> CubicRegionSizeShift == this.Z;
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

        // Frogspawn a frog laid used to hatch on a countdown of Obsidian's own; it hatches on its scheduled block tick
        // now, which the countdowns of older saves become.
        if (chunkCompound.TryGetTag<NbtList>("ObsidianFrogspawnTicks", out var frogspawnTicks))
        {
            chunk.BlockTicks.Load(frogspawnTicks.OfType<NbtCompound>().Select(tick => new SavedBlockTick(
                new Vector(tick.GetInt("x"), tick.GetInt("y"), tick.GetInt("z")),
                "minecraft:frogspawn",
                Math.Clamp(tick.GetInt("delay"), 1, 12000))));
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

            // Older Obsidian saves omitted runtime maps that full vanilla chunks keep.
            var full = chunkCompound.TryGetTagValue<string>("Status", out var heightmapStatus)
                && heightmapStatus is "minecraft:full" or "full";
            foreach (var (type, target) in chunk.Heightmaps)
            {
                var generationOnly = type is HeightmapType.WorldSurfaceWG or HeightmapType.OceanFloorWG;
                if (generationOnly && full)
                    continue;

                if (!heightmaps!.HasTag(HeightmapName(type)))
                    target.Recalculate();
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

        // A loaded chunk retains even temporary maps that its save explicitly included.
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

        // Vanilla reads the generated or loaded references alongside the starts saved in their start chunks.
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
            writer.WriteArray(HeightmapName(type), heightmap.GetDataArray());
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

        // A region whose predecessor hadn't closed yet may never have opened its files. Each file closes even when
        // the other fails to, so a region object opened in this one's place can open both.
        try
        {
            if (this.regionFile is not null)
                await this.regionFile.DisposeAsync();
        }
        finally
        {
            try
            {
                if (this.entityRegionFile is not null)
                    await this.entityRegionFile.DisposeAsync();
            }
            finally
            {
                foreach (var chunk in this.loadedChunks)
                    this.StructureChunkUnloaded?.Invoke(chunk);
            }
        }
    }

    /// <summary>
    /// What a save took of a chunk (see <see cref="SnapshotChunkAsync"/>) until it's written. Disposing it gives back
    /// the uncompressed NBT and gives up the turns it didn't stage.
    /// </summary>
    internal sealed class ChunkSave : IDisposable
    {
        // The chunk the save was taken for; null for a retried pending write, whose chunk is gone.
        public IChunk? Chunk { get; init; }

        // The chunk the snapshot serialized: the one stored at the position, which generation may have replaced.
        public IChunk? Snapshotted { get; set; }

        // The chunk's local coordinates in the region.
        public required int X { get; init; }

        public required int Z { get; init; }

        // The chunk's NBT, uncompressed, and its turn in the region file; null when the chunk was unchanged.
        public PooledBufferStream? Nbt { get; set; }

        public RegionFile.ChunkWrite? ChunkWrite { get; set; }

        // The chunk whose unsaved marks this save took, so a save failing before the chunk is written puts them back.
        public Chunk? Taken { get; set; }

        // The entities to save, until the snapshot serializes them into EntityNbt, with their count; null when they
        // aren't saved. Their turn in the entity region file follows.
        public NbtList? Entities { get; set; }

        public PooledBufferStream? EntityNbt { get; set; }

        public int EntityCount { get; set; }

        public RegionFile.ChunkWrite? EntityWrite { get; set; }

        // Whether the snapshot was staged in full (see StageSaveAsync), and whether its writes then ended without
        // failing (see CompleteSaveAsync). A save failing before it's staged gives its writes up, which don't fail.
        public bool Staged { get; set; }

        public bool Completed { get; set; }

        // Whether the chunk is saved as the snapshot took it.
        public bool Saved => this.Staged && this.Completed;

        public List<BaseContainer> Retired { get; } = [];

        public List<BlockEntity> Unloaded { get; } = [];

        public void Dispose()
        {
            this.Nbt?.Return();
            this.EntityNbt?.Return();
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
