using Obsidian.API.ChunkData;
using Obsidian.API.Entities;
using System.Collections.Concurrent;

namespace Obsidian.API;

public interface IWorld : IAsyncDisposable
{
    public string Name { get; }
    public string FolderPath { get; }
    public string PlayerDataPath { get; }
    public string LevelDataFilePath { get; }
    public string DimensionName { get; }
    public string? ParentWorldName { get; }

    public bool Loaded { get; }

    public long Time { get; set; }
    public int DayTime { get; set; }
    public string Seed { get; }

    public Level LevelData { get; }

    public Gamemode DefaultGamemode { get; }

    public int RegionCount { get; }
    public int LoadedChunkCount { get; }
    public int ChunksToGenCount { get; }

    public IPacketBroadcaster PacketBroadcaster { get; }
    public IEventDispatcher EventDispatcher { get; }
    public IWorldManager WorldManager { get; }

    public ConcurrentDictionary<Guid, IPlayer> Players { get; }

    public IEntitySpawner GetNewEntitySpawner();

    public IEnumerable<IEntity> GetNonPlayerEntitiesInRange(VectorF location, float distance);
    public IEnumerable<IEntity> GetEntitiesInRange(VectorF location, float distance);
    public IEnumerable<IPlayer> GetPlayersInRange(VectorF location, float distance);
    public IEnumerable<IPlayer> GetPlayersInChunkRange(Vector worldPosition);

    /// <summary>
    /// Gets a Chunk from a Region.
    /// If the Chunk doesn't exist, it will be scheduled for generation unless scheduleGeneration is false.
    /// </summary>
    /// <param name="scheduleGeneration">
    /// Whether to enqueue a job to generate the chunk if it doesn't exist and return null.
    /// When set to false, a partial Chunk is returned.</param>
    /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the full chunk or a partial chunk.</returns>
    public ValueTask<IChunk?> GetChunkAsync(int x, int z, bool scheduleGeneration = true);

    /// <summary>
    /// Gets a Chunk from a Region.
    /// If the Chunk doesn't exist, it will be scheduled for generation unless scheduleGeneration is false.
    /// </summary>
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

    public IEntity SpawnEntity(VectorF position, EntityType type);
    public IEntity SpawnFallingBlock(VectorF position, Material mat);
    public void SpawnExperienceOrbs(VectorF position, short count);
    public IEnumerable<IPlayer> PlayersInRange(Vector location);
    public Task DoWorldTickAsync();
    public Task FlushRegionsAsync();
}
