namespace Obsidian.API.Blocks;

/// <summary>
/// What a plugin's block does when a player uses it. Implement the methods the block needs; the others leave the block
/// to Obsidian's default handling.
/// </summary>
/// <remarks>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while the block stays registered (it
/// stays in the world and keeps its states' ids), so keep state in the block's state or the plugin, not here.
/// </remarks>
public interface IBlockBehavior
{
    /// <summary>
    /// Called when a player uses the block (right-clicks it without sneaking), before the item in their hand is used.
    /// </summary>
    /// <returns>Whether the behaviour handled the use, which skips using the item.</returns>
    public ValueTask<bool> UseAsync(BlockUseContext context) => ValueTask.FromResult(false);
}

/// <summary>
/// A player using a block.
/// </summary>
public sealed class BlockUseContext
{
    public required IPlayer Player { get; init; }

    public required Vector Position { get; init; }

    /// <summary>
    /// The block's state.
    /// </summary>
    public required IBlock Block { get; init; }

    /// <summary>
    /// The face of the block the player aimed at.
    /// </summary>
    public required BlockFace Face { get; init; }

    public required InteractionHand Hand { get; init; }
}
