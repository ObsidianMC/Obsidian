using Obsidian.API.ChunkData;
using Obsidian.API.Entities;
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
    public string Name { get; }
    public string Seed { get; }
    public string FolderPath { get; }
    /// <summary>
    /// The level's name, which clients know it by: its dimension's identifier, like <c>minecraft:the_nether</c>.
    /// </summary>
    public string DimensionName { get; }

    /// <summary>
    /// The level's dimension type. Vanilla's dimensions are named after theirs; plugins' dimensions may use any.
    /// </summary>
    public DimensionCodec DimensionType { get; }

    public string LevelDataFilePath { get; }

    public bool Loaded { get; }

    public long Time { get; set; }
    public int DayTime { get; set; }

    public LevelData LevelData { get; }

    public GameMode DefaultGamemode { get; }

    public int RegionCount { get; }
    public int LoadedChunkCount { get; }
    public int ChunksToGenCount { get; }

    public IPacketBroadcaster PacketBroadcaster { get; }
    public IEventDispatcher EventDispatcher { get; }

    public ConcurrentDictionary<Guid, IPlayer> Players { get; }

    public IEntitySpawner GetNewEntitySpawner();

    public IEnumerable<IEntity> GetNonPlayerEntitiesInRange(VectorD location, float distance);
    public IEnumerable<IEntity> GetEntitiesInRange(VectorD location, float distance);
    public IEnumerable<IPlayer> GetPlayersInRange(VectorD location, float distance);
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

    public ValueTask<bool> DestroyEntityAsync(IEntity entity);
    public ValueTask<IBlock?> GetBlockAsync(Vector location);
    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z);
    public ValueTask SetBlockAsync(Vector location, IBlock block);
    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block);

    public ValueTask SetBlockAsync(Vector location, IBlock block, bool doBlockUpdate);
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

    public bool TryRemovePlayer(IPlayer player);

    public ValueTask SetBlockEntity(Vector blockPosition, IBlockEntity tileEntityData);
    public ValueTask SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData);

    public ValueTask<IBlockEntity?> GetBlockEntityAsync(Vector blockPosition);
    public ValueTask<IBlockEntity?> GetBlockEntityAsync(int x, int y, int z);

    public ValueTask SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate = false);
    public ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate = false);

    public ValueTask<int?> GetWorldSurfaceHeightAsync(int x, int z);

    public bool TryAddEntity(IEntity entity);
    public bool TryAddPlayer(IPlayer player);

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
    public IEntity SpawnEntity(IEntity entity);
    public IEntity SpawnFallingBlock(VectorD position, IBlock block);
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
    public IEnumerable<IPlayer> PlayersInRange(Vector location);
    public Task DoWorldTickAsync();
    public Task FlushRegionsAsync();
    public Task<bool> LoadAsync(DimensionCodec codec);
    public Task SaveAsync();

    /// <summary>
    /// Generates a new level's chunks around its spawn. Cancelling stops starting new chunks and throws
    /// <see cref="OperationCanceledException"/>; chunks already generating finish in the background.
    /// </summary>
    public Task GenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the level with the given dimension codec. 
    /// This is only used when a new world is being created, otherwise LoadAsync is used.
    /// </summary>
    /// <param name="codec">The dimension codec to use for initialization.</param>
    public void Initialize(DimensionCodec codec);
}
