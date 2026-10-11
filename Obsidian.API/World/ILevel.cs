using Obsidian.API.ChunkData;
using Obsidian.API.Entities;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.Collections.Concurrent;
using System.Threading;

namespace Obsidian.API.World;

public interface ILevel : IAsyncDisposable
{
    public string Name { get; }
    public string Seed { get; }
    public string FolderPath { get; }
    public string DimensionName { get; }
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
    public bool TryRemovePlayer(IPlayer player);
    public ValueTask<bool> HandleBlockUpdateAsync(IBlockUpdate update);
    public ValueTask BlockUpdateNeighborsAsync(IBlockUpdate update);
    public ValueTask ScheduleBlockUpdateAsync(IBlockUpdate blockUpdate);

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
    public IEntity SpawnEntity(IEntity entity);
    public IEntity SpawnFallingBlock(VectorD position, Material mat);
    public void SpawnExperienceOrbs(VectorD position, short count);
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
