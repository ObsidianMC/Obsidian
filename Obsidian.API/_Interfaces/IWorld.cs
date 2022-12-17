namespace Obsidian.API;

public interface IWorld
{
    public string Name { get; }

    public bool Loaded { get; }

    public string DimensionName { get; }

    public long Time { get; }

    public string Seed { get; }

    public Gamemode DefaultGamemode { get; }

    public Task<IBlock?> GetBlockAsync(Vector location);
    public Task<IBlock?> GetBlockAsync(int x, int y, int z);
    public Task SetBlockAsync(Vector location, IBlock block);
    public Task SetBlockAsync(int x, int y, int z, IBlock block);

    public Task SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate);

    public Task SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate);

    public Task<int?> GetWorldSurfaceHeightAsync(int x, int z);

    public Task<IEntity> SpawnEntityAsync(VectorF position, EntityType type);
    public Task SpawnExperienceOrbs(VectorF position, short count);
}
