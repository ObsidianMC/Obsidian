namespace Obsidian.API.World.Features;

/// <summary>
/// Tests the block at a position, like vanilla's BlockPredicate.
/// </summary>
public interface IBlockPredicate
{
    public string Type { get; }

    public bool Test(IWorldGenLevel level, Vector position);
}
