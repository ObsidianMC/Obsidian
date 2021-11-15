namespace Obsidian.API;

public interface IWorld
{
    public string Name { get; }
    public bool Loaded { get; }

    public long Time { get; }
    public Gamemode GameType { get; }

    public Block? GetBlock(Vector location);
    public Block? GetBlock(int x, int y, int z);
    public void SetBlock(Vector location, Block block);
    public void SetBlock(int x, int y, int z, Block block);

    public Task<IEntity> SpawnEntityAsync(VectorF position, EntityType type);
    public Task SpawnExperienceOrbs(VectorF position, short count);
    public Task SpawnPainting(Vector position, Painting painting, PaintingDirection direction, Guid uuid = default);
}
