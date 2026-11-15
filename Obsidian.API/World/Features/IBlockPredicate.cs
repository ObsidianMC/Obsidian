namespace Obsidian.API.World.Features;

/// <summary>
/// Tests the block at a position, like vanilla's BlockPredicate.
/// </summary>
public interface IBlockPredicate
{
    /// <summary>
    /// Gets the registered codec type identifier for this world generation component.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Tests the block at a world block position in the supplied generation view.
    /// </summary>
    public bool Test(IWorldGenLevel level, Vector position);
}
