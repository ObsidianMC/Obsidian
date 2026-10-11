namespace Obsidian.API.Inventory;

/// <summary>
/// What a plugin's item does when a player uses it. Implement the methods the item needs; the others leave the item
/// to Obsidian's default handling, which for an item without vanilla behaviour is nothing.
/// </summary>
/// <remarks>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while the item stays registered (stacks
/// of it stay in inventories and keep their id), so keep state in the item stack's components or the plugin, not here.
/// </remarks>
public interface IItemBehavior
{
    /// <summary>
    /// Called when a player uses the item without aiming at a block (right-clicking the air).
    /// </summary>
    /// <returns>Whether the behaviour handled the use, which skips Obsidian's own handling.</returns>
    public ValueTask<bool> UseAsync(ItemUseContext context) => ValueTask.FromResult(false);

    /// <summary>
    /// Called when a player uses the item on a block (right-clicking it), before the block reacts or the item places a
    /// block.
    /// </summary>
    /// <returns>Whether the behaviour handled the use, which skips Obsidian's own handling.</returns>
    public ValueTask<bool> UseOnBlockAsync(ItemUseOnBlockContext context) => ValueTask.FromResult(false);
}

/// <summary>
/// A player using an item.
/// </summary>
public class ItemUseContext
{
    public required IPlayer Player { get; init; }

    /// <summary>
    /// The stack the player uses.
    /// </summary>
    public required ItemStack Stack { get; init; }

    public required InteractionHand Hand { get; init; }
}

/// <summary>
/// A player using an item on a block.
/// </summary>
public sealed class ItemUseOnBlockContext : ItemUseContext
{
    public required Vector Position { get; init; }

    public required BlockFace Face { get; init; }

    public required IBlock Block { get; init; }
}
