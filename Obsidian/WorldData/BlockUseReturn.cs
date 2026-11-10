using Obsidian.API.Inventory;
using Obsidian.Entities;

namespace Obsidian.WorldData;

/// <summary>
/// An item a use gives the player, like vanilla's <c>Inventory.add</c> followed by <c>Player.drop</c> for what doesn't
/// fit: a plant taken out of a pot, a book off a shelf, a new lodestone compass.
/// </summary>
/// <remarks>
/// <see cref="Give"/> runs inside <see cref="BlockUse.TryCommit"/>'s change, which holds the player's inventory, so the
/// item lands in the same step that takes it from the block. <see cref="FinishAsync"/> runs after the commit, holding
/// no lock: it sends the slots that changed and drops what didn't fit.
/// </remarks>
internal sealed class BlockUseReturn
{
    private readonly ItemStack stack;

    private List<int> changed = [];

    private BlockUseReturn(ItemStack stack) => this.stack = stack;

    /// <summary>
    /// Puts <paramref name="stack"/> into the user's inventory as far as it fits: onto a matching stack (the held slot,
    /// the offhand, then the rest), then into the first empty slot. A creative player's inventory takes all of it, as
    /// vanilla's does, even when full. Call inside <see cref="BlockUse.TryCommit"/>'s change.
    /// </summary>
    public static BlockUseReturn Give(BlockUse use, ItemStack stack)
    {
        var given = new BlockUseReturn(new ItemStack(stack, stack.Count));
        var creative = use.Player.GameMode == GameMode.Creative;
        given.changed = PlayerBase.AddToInventory(
            use.Player.Inventory,
            use.Player.CurrentHeldItemSlot,
            given.stack,
            creative);

        return given;
    }

    /// <summary>
    /// Sends the slots the item went into, and drops what didn't fit in front of the player, like vanilla's
    /// <c>Player.drop</c> without throwing it randomly; that counts no statistic. Call holding no lock.
    /// </summary>
    public async ValueTask FinishAsync(BlockUse use)
    {
        foreach (var slot in this.changed)
            await use.Player.SyncInventorySlotAsync(slot);

        if (this.stack.Count > 0)
            ItemEntity.DropUncounted(use.Player, this.stack);
    }
}
