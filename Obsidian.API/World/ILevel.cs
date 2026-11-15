using Obsidian.API.ChunkData;
using Obsidian.API.Entities;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Collections.Concurrent;
using System.Threading;

namespace Obsidian.API.World;

/// <remarks>
/// Its redstone signals (see <see cref="Blocks.ISignalReader"/>) are read as its blocks are, waiting for the block
/// updates running at the time.
/// </remarks>
public interface ILevel : IAsyncDisposable, Blocks.ISignalReader
{
    /// <summary>
    /// Whether the block's face supports the requested area, like vanilla's <c>BlockState.isFaceSturdy</c>.
    /// Reads state shapes and published block entity shapes synchronously, without loading or generating chunks.
    /// Returns false outside the build range or complete loaded chunks. Use on the level tick or during block hooks.
    /// </summary>
    public bool IsFaceSturdy(Vector position, BlockFace face, Blocks.SupportType support = Blocks.SupportType.Full) =>
        false;

    /// <summary>
    /// Requests destruction of a loaded block on the next level tick, with optional loot and its fluid left behind.
    /// Returns whether it was queued. A changed block or block entity cancels the request before it runs.
    /// Player break hooks and tool checks do not run. Container removal still spills ordinary contents.
    /// </summary>
    public bool DestroyBlock(Vector position, bool dropItems, IEntity? breaker = null) => false;

    /// <summary>
    /// Tells the six neighbours of <paramref name="position"/> that it changed, from <paramref name="source"/>, like
    /// vanilla's <c>updateNeighborsAt</c> and <see cref="Blocks.IBlockLevel.UpdateNeighbors"/>: redstone components,
    /// comparators and plugin blocks' <see cref="Blocks.IBlockBehavior.NeighborChanged"/> react as to a block change.
    /// For a block entity whose power changed, say, from its tick or a level ticker.
    /// </summary>
    /// <remarks>
    /// The updates run at once, under the level's tick lock, like those of a block change: from any thread, waiting
    /// for the block updates running at the time, and within a block hook, after the hook's own. Don't call it while
    /// holding a lock that block hooks may wait on, such as a window's. Neighbours outside the complete loaded chunks
    /// aren't told.
    /// </remarks>
    public void UpdateNeighbors(Vector position, IBlock source) =>
        throw new NotSupportedException("This level doesn't support neighbour updates.");

    /// <summary>
    /// Spawns a host-owned item entity from a detached copy of stack, independent of plugin lifetime. Returns false
    /// for an empty stack or a failed insertion. This direct helper does not apply creative or block-drop rules.
    /// </summary>
    public bool SpawnItem(VectorD position, Inventory.ItemStack stack) => false;

    /// <summary>
    /// Lights an empty registered frame containing position (an interior block).
    /// Item/block callbacks may call this to implement their own ignition rules.
    /// </summary>
    public ValueTask<bool> TryLightPortalAsync(PortalType type, Vector position) =>
        ValueTask.FromResult(false);

    /// <summary>
    /// Keeps a square of chunks loaded, optionally ticking without players. Radius is in chunks (0 means one),
    /// at most 8. Ticking tickets also load a one-chunk border for block and fluid updates. Loading and generation
    /// start on the next chunk-management pass; the handle does not mean the chunks are ready yet.
    /// </summary>
    /// <remarks>
    /// Dispose to release. Tickets acquired by plugin code are automatically released when that plugin unloads.
    /// Tickets are not saved. Each level allows at most 256 handles and 1024 distinct retained chunks,
    /// including borders.
    /// A loaded-only ticket does not prevent simulation caused by a player or another ticket.
    /// </remarks>
    public IDisposable AddChunkTicket(int chunkX, int chunkZ, int radius = 0, bool ticking = true) =>
        throw new NotSupportedException("This level doesn't support chunk tickets.");

    /// <summary>
    /// Runs <paramref name="tick"/> once every tick of the level, like NeoForge's post level tick event: for what
    /// spans many block entities, such as a network that ticks once rather than once per member. It runs on the
    /// level's tick, after the block entities ticked and before their changes are saved and sent, so it may use the
    /// level's block entities as their own ticks do. It runs whether or not any chunk ticks.
    /// </summary>
    /// <remarks>
    /// Dispose the handle to stop it. Tickers run in the order they were added, from the next tick on (or this one if
    /// added before the level's tickers run). A plugin's run inside calls into the plugin, and are removed when it
    /// unloads. A tick that throws is logged and the ticker keeps running.
    /// </remarks>
    public IDisposable AddTicker(Func<ValueTask> tick) =>
        throw new NotSupportedException("This level doesn't support tickers.");

    /// <summary>
    /// The block state at <paramref name="position"/> if its chunk is loaded and complete, without loading or
    /// generating anything. Null otherwise; void air outside the build range of a loaded chunk.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread: reads published chunk sections, like <see cref="GetBlockAsync(Vector)"/>.
    /// A read racing an edit may see the state before or after it; several reads aren't an atomic snapshot.
    /// It does not wait for block updates or acquire the level's tick lock. Inside a block hook, use
    /// <see cref="Blocks.IBlockLevel.GetBlock"/>, whose reads stay stable until the hook returns.
    /// </remarks>
    public IBlock? GetLoadedBlock(Vector position) =>
        throw new NotSupportedException("This level doesn't support loaded block lookups.");

    /// <summary>
    /// The block entity at <paramref name="position"/> if its chunk is loaded and complete, without loading or
    /// generating anything, like <see cref="Blocks.IBlockLevel.GetBlockEntity"/>: for following a cable or network
    /// across block entities from a tick. Null otherwise, or once the block entity was removed or its chunk unloaded.
    /// </summary>
    /// <remarks>
    /// <see cref="GetBlockEntityAsync(Vector)"/>, by contrast, loads the chunk from disk, and adds an ungenerated one
    /// where none was saved. Use what this returns on the level's tick, as block entity ticks and tickers do.
    /// </remarks>
    public IBlockEntity? GetLoadedBlockEntity(Vector position) =>
        throw new NotSupportedException("This level doesn't support loaded block entity lookups.");

    /// <summary>
    /// Sets the biome of the quarts (the 4x4x4 block cells biomes are stored in) between <paramref name="from"/> and
    /// <paramref name="to"/>, like vanilla's <c>/fillbiome</c>: each corner is rounded down to its quart's, and the
    /// quarts whose lowest corner lies in the box between them are filled, where <paramref name="filter"/> (if any)
    /// accepts their current biome. Only loaded, generated chunks change; quarts elsewhere are left alone. The players
    /// who have a changed chunk are sent its biomes at the level's next tick, each one packet for all of theirs.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="IChunk.SetBiome(int, int, int, BiomeCodec)"/>, which changes a chunk's stored biomes without
    /// telling clients, this is how a plugin changes biomes players can see. Safe to call from any thread: it holds the
    /// level's tick lock while it fills, so it waits for the block updates running at the time. Don't call it while
    /// holding a lock block hooks may wait on, such as a window's. <paramref name="filter"/> is called once for each
    /// quart in the box, under the level's tick lock and the chunk's block lock but not under a section's lock. It may
    /// be shown a biome that a write from another thread changes before the fill applies, and it must not wait on
    /// another thread that needs those locks. The fill holds the tick lock for time proportional to the box's area in
    /// chunks, loaded or not, so the level's tick waits for it: keep boxes to loaded areas.
    /// </remarks>
    /// <returns>How many quarts changed biome. Zero sends nothing.</returns>
    public int FillBiomes(
        Vector from,
        Vector to,
        BiomeCodec biome,
        Func<BiomeCodec, bool>? filter = null) =>
        throw new NotSupportedException("This level doesn't support biome fills.");

    /// <summary>
    /// Gets the load's live instance, reading its file lazily. World scope shares across dimensions. Access plugin
    /// objects on the level tick or synchronize them yourself; call MarkDirty to publish changes for saving.
    /// </summary>
    public Data.SavedData<TValue> GetSavedData<TValue>(
        Data.SavedDataType<TValue> type, Func<TValue> create, Data.SavedDataScope scope = Data.SavedDataScope.Level) =>
        throw new NotSupportedException("This level doesn't support saved data.");

    /// <summary>
    /// Gets the level's configured name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the seed text supplied when the level was created.
    /// </summary>
    public string Seed { get; }
    /// <summary>
    /// Gets the filesystem path containing this level's chunks, entities and saved data.
    /// </summary>
    public string FolderPath { get; }
    /// <summary>
    /// The level's name, which clients know it by: its dimension's identifier, like <c>minecraft:the_nether</c>.
    /// </summary>
    public string DimensionName { get; }

    /// <summary>
    /// The level's dimension type. Vanilla's dimensions are named after theirs; plugins' dimensions may use any.
    /// </summary>
    public DimensionCodec DimensionType { get; }

    /// <summary>
    /// Gets the filesystem path of the level data file.
    /// </summary>
    public string LevelDataFilePath { get; }

    /// <summary>
    /// Gets whether the level has been loaded.
    /// </summary>
    public bool Loaded { get; }

    /// <summary>
    /// Gets or sets the stored world time in ticks, broadcasting changes to players.
    /// </summary>
    public long Time { get; set; }
    /// <summary>
    /// Gets or sets the stored time of day in ticks, broadcasting changes to players.
    /// </summary>
    public int DayTime { get; set; }

    /// <summary>
    /// Gets the level's mutable persisted settings and state.
    /// </summary>
    public LevelData LevelData { get; }

    /// <summary>
    /// Gets the default game mode stored in the level data.
    /// </summary>
    public GameMode DefaultGamemode { get; }

    /// <summary>
    /// Gets the number of regions currently held by the level.
    /// </summary>
    public int RegionCount { get; }
    /// <summary>
    /// Gets the number of generated chunks currently loaded across the level's regions.
    /// </summary>
    public int LoadedChunkCount { get; }
    /// <summary>
    /// Gets the number of queued and waiting chunk generation jobs.
    /// </summary>
    public int ChunksToGenCount { get; }

    /// <summary>
    /// Gets the service used to send packets to this level's players.
    /// </summary>
    public IPacketBroadcaster PacketBroadcaster { get; }
    /// <summary>
    /// Gets the service used to dispatch server events for this level.
    /// </summary>
    public IEventDispatcher EventDispatcher { get; }

    /// <summary>
    /// Gets the live mapping of player UUIDs to players in this level.
    /// </summary>
    public ConcurrentDictionary<Guid, IPlayer> Players { get; }

    /// <summary>
    /// Creates an entity spawner bound to this level.
    /// </summary>
    public IEntitySpawner GetNewEntitySpawner();

    /// <summary>
    /// Enumerates nonplayer entities within a three-dimensional radius in blocks.
    /// </summary>
    public IEnumerable<IEntity> GetNonPlayerEntitiesInRange(VectorD location, float distance);
    /// <summary>
    /// Enumerates players within horizontal range and nonplayer entities within three-dimensional range, in blocks.
    /// </summary>
    public IEnumerable<IEntity> GetEntitiesInRange(VectorD location, float distance);
    /// <summary>
    /// Enumerates players within a horizontal radius in blocks; zero requires an exact three-dimensional position
    /// match.
    /// </summary>
    public IEnumerable<IPlayer> GetPlayersInRange(VectorD location, float distance);
    /// <summary>
    /// Enumerates players that have the chunk containing the world block position loaded.
    /// </summary>
    public IEnumerable<IPlayer> GetPlayersInChunkRange(Vector worldPosition);

    /// <summary>
    /// Gets a Chunk from a Region.
    /// If the Chunk doesn't exist, it will be scheduled for generation unless scheduleGeneration is false.
    /// </summary>
    /// <param name="x">The chunk's X coordinate.</param>
    /// <param name="z">The chunk's Z coordinate.</param>
    /// <param name="scheduleGeneration">
    /// Whether to enqueue a job to generate the chunk if it doesn't exist and return null.
    /// When set to false, a partial Chunk is returned.</param>
    /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the full chunk or a partial chunk.</returns>
    public ValueTask<IChunk?> GetChunkAsync(int x, int z, bool scheduleGeneration = true);

    /// <summary>
    /// Gets a Chunk from a Region.
    /// If the Chunk doesn't exist, it will be scheduled for generation unless scheduleGeneration is false.
    /// </summary>
    /// <param name="worldLocation">The chunk's coordinates; only X and Z are used.</param>
    /// <param name="scheduleGeneration">
    /// Whether to enqueue a job to generate the chunk if it doesn't exist and return null.
    /// When set to false, a partial Chunk is returned.</param>
    /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the full chunk or a partial chunk.</returns>
    public ValueTask<IChunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true);

    /// <summary>
    /// Removes an entity from this level and queues its removal for clients, returning whether removal succeeded.
    /// </summary>
    public ValueTask<bool> DestroyEntityAsync(IEntity entity);
    /// <summary>
    /// Gets the block at world block coordinates, loading its chunk without generation; returns <c>null</c> if
    /// unavailable and void air outside build height.
    /// </summary>
    public ValueTask<IBlock?> GetBlockAsync(Vector location);
    /// <summary>
    /// Gets the block at world block coordinates, loading its chunk without generation; returns <c>null</c> if
    /// unavailable and void air outside build height.
    /// </summary>
    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z);
    /// <summary>
    /// Sets a block at a world block position and notifies clients without requesting neighbor updates.
    /// </summary>
    public ValueTask SetBlockAsync(Vector location, IBlock block);
    /// <summary>
    /// Sets a block at world block coordinates and notifies clients without requesting neighbor updates.
    /// </summary>
    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block);

    /// <summary>
    /// Sets a block at world block coordinates and notifies clients, optionally requesting block updates.
    /// </summary>
    public ValueTask SetBlockAsync(Vector location, IBlock block, bool doBlockUpdate);
    /// <summary>
    /// Sets a block at world block coordinates and notifies clients, optionally requesting block updates.
    /// </summary>
    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block, bool doBlockUpdate);

    /// <summary>
    /// Changes the block at <paramref name="location"/>, with the side effects <paramref name="flags"/> ask for, like
    /// vanilla's <c>setBlock</c>: <see cref="Blocks.BlockUpdateFlags.All"/> is what a player placing a block does, and
    /// what <c>doBlockUpdate</c> asks for; without it, a change has no side effects besides players seeing it.
    /// </summary>
    public ValueTask SetBlockAsync(Vector location, IBlock block, Blocks.BlockUpdateFlags flags);

    /// <summary>
    /// Schedules a tick of <paramref name="block"/>'s block at <paramref name="location"/> in <paramref name="delay"/>
    /// game ticks, unless one is scheduled there already, like vanilla's <c>scheduleTick</c>. It runs if that block is
    /// still there then (a plugin block's <see cref="Blocks.IBlockBehavior.Tick"/>), and is saved with the chunk as
    /// vanilla's <c>block_ticks</c>. Ignored where the chunk isn't loaded and complete.
    /// </summary>
    public void ScheduleBlockTick(
        Vector location,
        IBlock block,
        int delay,
        Blocks.TickPriority priority = Blocks.TickPriority.Normal);

    /// <summary>
    /// Removes the matching player instance from this level's player collection, returning whether it was present.
    /// </summary>
    public bool TryRemovePlayer(IPlayer player);

    /// <summary>
    /// Stores block entity data at world block coordinates.
    /// </summary>
    public ValueTask SetBlockEntity(Vector blockPosition, IBlockEntity tileEntityData);
    /// <summary>
    /// Stores block entity data at world block coordinates.
    /// </summary>
    public ValueTask SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData);

    /// <summary>
    /// Gets block entity data at world block coordinates, or <c>null</c> when none is available.
    /// </summary>
    public ValueTask<IBlockEntity?> GetBlockEntityAsync(Vector blockPosition);
    /// <summary>
    /// Gets block entity data at world block coordinates, or <c>null</c> when none is available.
    /// </summary>
    public ValueTask<IBlockEntity?> GetBlockEntityAsync(int x, int y, int z);

    /// <summary>
    /// The items of the block at <paramref name="position"/> as another block reaches them through its
    /// <paramref name="side"/> face (or from no particular side when null), like NeoForge's item handler capability:
    /// a plugin block's own (see <see cref="Blocks.IBlockBehavior.GetItemAccess"/>), else its block entity's (see
    /// <see cref="Inventory.IItemAccessProvider"/>), with both halves of a double chest as one. Null when it has none
    /// from that side, or its chunk isn't loaded and complete. Synchronous: it never loads a chunk.
    /// </summary>
    public Inventory.IItemAccess? GetItemAccess(Vector position, BlockFace? side);

    /// <summary>
    /// Sets a block at world block coordinates without client notification, optionally requesting block updates.
    /// </summary>
    public ValueTask SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate = false);
    /// <summary>
    /// Sets a block at world block coordinates without client notification, optionally requesting block updates.
    /// </summary>
    public ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate = false);

    /// <summary>
    /// Gets the world-surface heightmap value at world block X and Z, or <c>null</c> if the chunk is unavailable.
    /// </summary>
    public ValueTask<int?> GetWorldSurfaceHeightAsync(int x, int z);

    /// <summary>
    /// Attempts to register an entity in this level, returning whether it was added.
    /// </summary>
    public bool TryAddEntity(IEntity entity);
    /// <summary>
    /// Adds a player to this level's player collection unless its UUID is already present.
    /// </summary>
    public bool TryAddPlayer(IPlayer player);

    /// <summary>
    /// Spawns the requested type at a world position; falling blocks use sand placed 20 blocks higher.
    /// </summary>
    public IEntity SpawnEntity(VectorD position, EntityType type);

    /// <summary>
    /// Spawns an entity of a plugin's entity type, moving at <paramref name="motion"/> blocks per tick; a projectile
    /// remembers <paramref name="owner"/> as what shot it.
    /// </summary>
    public IEntity SpawnEntity(
        VectorD position,
        Entities.EntityDefinition definition,
        VectorD motion = default,
        IEntity? owner = null);
    /// <summary>
    /// Registers and spawns the existing entity, throwing if it cannot be registered in this level.
    /// </summary>
    public IEntity SpawnEntity(IEntity entity);
    /// <summary>
    /// Spawns a falling block carrying the supplied state, offsetting X and Z by half a block.
    /// </summary>
    public IEntity SpawnFallingBlock(VectorD position, IBlock block);
    /// <summary>
    /// Spawns experience orbs whose values total <paramref name="count"/> at a world position.
    /// </summary>
    public void SpawnExperienceOrbs(VectorD position, short count);

    /// <summary>
    /// Shows particles to the players within 32 blocks of <paramref name="position"/>, like vanilla's
    /// <c>/particle</c>: with a count of 0, one particle moving along <paramref name="spread"/> times
    /// <paramref name="speed"/>; otherwise <paramref name="count"/> particles scattered around the position by up to
    /// about <paramref name="spread"/> on each axis, moving randomly at up to about <paramref name="speed"/>.
    /// </summary>
    /// <param name="particle">
    /// The particle: a plugin's <see cref="ParticleDefinition"/>, or vanilla's particle data.
    /// </param>
    /// <param name="position">Where the particles appear.</param>
    /// <param name="count">How many particles appear.</param>
    /// <param name="spread">How far they're scattered on each axis, or the direction of a single particle.</param>
    /// <param name="speed">How fast they move.</param>
    /// <param name="force">
    /// Whether players up to 512 blocks away see them too, whatever their particle setting.
    /// </param>
    public void SpawnParticles(
        ParticleData particle,
        VectorD position,
        int count = 1,
        VectorF spread = default,
        float speed = 0,
        bool force = false);
    /// <summary>
    /// Enumerates players that have the chunk containing the world block position loaded.
    /// </summary>
    public IEnumerable<IPlayer> PlayersInRange(Vector location);
    /// <summary>
    /// Ticks the level once. The server does, every tick; a plugin rarely needs to.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or a player's tick, grant or save (a plugin's event handler run by one, say):
    /// it would wait for a tick that may be waiting for it.
    /// </exception>
    public Task DoWorldTickAsync();

    /// <summary>
    /// Saves the level's regions: its chunks and their entities.
    /// </summary>
    /// <remarks>
    /// It waits for the level to be between ticks.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or a player's tick, grant or save (a plugin's event handler run by one, say):
    /// it would wait for a tick that may be waiting for it. Plugins should save outside ticks: from a scheduled task or
    /// a command, say.
    /// </exception>
    public Task FlushRegionsAsync();

    /// <summary>
    /// Loads the level using the supplied dimension type, returning whether loading succeeded.
    /// </summary>
    public Task<bool> LoadAsync(DimensionCodec codec);

    /// <summary>
    /// Saves the level's state: for a world, its level data and maps; for a dimension, its regions and its state.
    /// </summary>
    /// <remarks>
    /// A dimension's save waits for it to be between ticks, to save its regions; a world's writes its level data while
    /// the levels tick, after any save of the world under way, which may itself be waiting for them.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or a player's tick, grant or save (a plugin's event handler run by one, say):
    /// it would wait for a tick that may be waiting for it. Plugins should save outside ticks: from a scheduled task or
    /// a command, say.
    /// </exception>
    public Task SaveAsync();

    /// <summary>
    /// Generates a new level's chunks around its spawn. Cancelling stops starting new chunks and throws
    /// <see cref="OperationCanceledException"/>; chunks already generating finish in the background.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The level isn't generated yet and it's called from inside a level's tick, or a player's tick, grant or save: it
    /// would wait for a tick that may be waiting for it.
    /// </exception>
    public Task GenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the level with the given dimension codec. 
    /// This is only used when a new world is being created, otherwise LoadAsync is used.
    /// </summary>
    /// <param name="codec">The dimension codec to use for initialization.</param>
    public void Initialize(DimensionCodec codec);
}
