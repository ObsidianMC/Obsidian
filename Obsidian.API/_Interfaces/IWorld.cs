namespace Obsidian.API;

public interface IWorld
{
    public string Name { get; }

    public bool Loaded { get; }

    public string DimensionName { get; }

    public long Time { get; }

    public string Seed { get; }

    public Gamemode DefaultGamemode { get; }

    public Task<Block?> GetBlockAsync(Vector location);
    public Task<Block?> GetBlockAsync(int x, int y, int z);
    public Task SetBlockAsync(Vector location, Block block);
    public Task SetBlockAsync(int x, int y, int z, Block block);

    public Task SetBlockUntrackedAsync(int x, int y, int z, Block block, bool doBlockUpdate);

    public Task SetBlockUntrackedAsync(Vector location, Block block, bool doBlockUpdate);

    public Task<int?> GetWorldSurfaceHeightAsync(int x, int z);

    public Task<IEntity> SpawnEntityAsync(VectorF position, EntityType type);
    public Task SpawnExperienceOrbs(VectorF position, short count);
    public Task SpawnPaintingAsync(Vector position, Painting painting, PaintingDirection direction, Guid uuid = default);
}
