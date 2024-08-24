namespace Obsidian.API;

public interface IWorld : IAsyncDisposable
{
    public string Name { get; }

    public bool Loaded { get; }

    public string DimensionName { get; }

    public long Time { get; set; }
    public int DayTime { get; set; }
    public string Seed { get; }

    public Gamemode DefaultGamemode { get; }

    public int RegionCount { get; }
    public int LoadedChunkCount { get; }
    public int ChunksToGenCount { get; }

    public int GetTotalLoadedEntities();

    public Task<IBlock?> GetBlockAsync(Vector location);
    public Task<IBlock?> GetBlockAsync(int x, int y, int z);
    public Task SetBlockAsync(Vector location, IBlock block);
    public Task SetBlockAsync(int x, int y, int z, IBlock block);

    public Task SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate);

    public Task SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate);

    public Task<int?> GetWorldSurfaceHeightAsync(int x, int z);

    public Task<IEntity> SpawnEntityAsync(VectorF position, EntityType type);
    public void SpawnExperienceOrbs(VectorF position, short count);

    public Task DoWorldTickAsync();
    public Task FlushRegionsAsync();
}
