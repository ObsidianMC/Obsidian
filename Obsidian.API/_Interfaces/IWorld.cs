using Obsidian.API.ChunkData;
using Obsidian.API.Entities;

namespace Obsidian.API;

public interface IWorld : IAsyncDisposable
{
    public string Name { get; }

    public bool Loaded { get; }

    public string DimensionName { get; }

    public long Time { get; set; }
    public int DayTime { get; set; }
    public string Seed { get; }

    public Level LevelData { get; }

    public Gamemode DefaultGamemode { get; }

    public int RegionCount { get; }
    public int LoadedChunkCount { get; }
    public int ChunksToGenCount { get; }

    public IEntitySpawner GetNewEntitySpawner();

    public IEnumerable<IEntity> GetNonPlayerEntitiesInRange(VectorF location, float distance);
    public IEnumerable<IEntity> GetEntitiesInRange(VectorF location, float distance);

    public ValueTask<IChunk?> GetChunkAsync(int x, int z, bool scheduleGeneration = true);
    public ValueTask<IChunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true);
    public ValueTask<bool> DestroyEntityAsync(IEntity entity);
    public ValueTask<IBlock?> GetBlockAsync(Vector location);
    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z);
    public ValueTask SetBlockAsync(Vector location, IBlock block);
    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block);
    public bool TryRemovePlayer(IPlayer player);
    public ValueTask<bool> HandleBlockUpdateAsync(IBlockUpdate update);
    public ValueTask BlockUpdateNeighborsAsync(IBlockUpdate update);
    public ValueTask ScheduleBlockUpdateAsync(IBlockUpdate blockUpdate);

    public ValueTask SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate = false) => SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);
    public ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate = false);

    public ValueTask<int?> GetWorldSurfaceHeightAsync(int x, int z);

    public IEntity SpawnEntity(VectorF position, EntityType type);
    public IEntity SpawnFallingBlock(VectorF position, Material mat);
    public void SpawnExperienceOrbs(VectorF position, short count);
    public IEnumerable<IPlayer> PlayersInRange(Vector location);
    public Task DoWorldTickAsync();
    public Task FlushRegionsAsync();
}
