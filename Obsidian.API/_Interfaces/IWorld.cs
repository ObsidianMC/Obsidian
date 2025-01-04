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

    public Gamemode DefaultGamemode { get; }

    public int RegionCount { get; }
    public int LoadedChunkCount { get; }
    public int ChunksToGenCount { get; }

    public IEntitySpawner GetNewEntitySpawner();

    public ValueTask<IBlock?> GetBlockAsync(Vector location);
    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z);
    public ValueTask SetBlockAsync(Vector location, IBlock block);
    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block);

    public ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate);

    public ValueTask SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate);

    public ValueTask<int?> GetWorldSurfaceHeightAsync(int x, int z);

    public IEntity SpawnEntity(VectorF position, EntityType type);
    public void SpawnExperienceOrbs(VectorF position, short count);

    public Task DoWorldTickAsync();
    public Task FlushRegionsAsync();
}
