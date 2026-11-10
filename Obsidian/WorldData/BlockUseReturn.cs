using Obsidian.API.Inventory;
using Obsidian.Entities;

namespace Obsidian.WorldData;

/// <summary>
/// The one way a use gives the player an item: a plant taken out of a pot, a book off a shelf, a lodestone compass,
/// a filled or emptied bucket. Like vanilla's <c>Inventory.add</c> (followed by <c>Player.drop</c> for what doesn't
/// fit) and <c>ItemUtils.createFilledResult</c>.
/// </summary>
/// <remarks>
/// <see cref="Give"/> and <see cref="Exchange"/> run inside <see cref="BlockUse.TryCommit"/>'s change, which holds the
/// player's inventory and the tick lock: the item lands, the hand is consumed and what doesn't fit is queued to drop
/// in the same atomic step that changes the block, and the drop is published with the commit's other effects once
/// the inventories unlock. The slots they change are added to <see cref="BlockUse.ChangedInventorySlots"/>, which
/// <see cref="SendChangedSlotsAsync"/> sends after the commit, holding no lock.
/// </remarks>
internal static class BlockUseReturn
{
    // The storage slots a bucket's creative rule searches for the result, by the player inventory's slots: the
    // hotbar, the rest of the main inventory and the offhand.
    private static readonly int[] storageSlots = [.. Enumerable.Range(36, 9), .. Enumerable.Range(9, 27), 45];

    /// <summary>
    /// Vanilla's <c>Inventory.add</c>: puts <paramref name="stack"/> into the user's inventory as far as it fits, onto
    /// a matching stack (the held slot, the offhand, then the rest), then into the first empty slot, hotbar first. What
    /// doesn't fit drops in front of the player once the commit publishes. A creative player's inventory takes all of
    /// it, as vanilla's does, even when full.
    /// </summary>
    /// <returns>
    /// A copy of the stack after insertion: empty when the inventory took it all, else what drops. Vanilla's criteria
    /// read this temporary stack.
    /// </returns>
    public static ItemStack Give(BlockUse use, ItemStack stack)
    {
        var effects = InsideCommit(use);
        var rest = new ItemStack(stack, stack.Count);
        var creative = use.Player.GameMode == GameMode.Creative;
        var changed = PlayerBase.AddToInventory(use.Player.Inventory, use.Player.CurrentHeldItemSlot, rest, creative);
        foreach (var slot in changed)
            use.ChangedInventorySlots.Add(slot);

        if (rest.Count > 0)
        {
            var dropped = new ItemStack(rest, rest.Count);
            effects.After(() => ItemEntity.DropUncounted(use.Player, dropped));
        }

        return rest;
    }

    /// <summary>
    /// Vanilla's <c>ItemUtils.createFilledResult</c>: one of the used stack becomes <paramref name="result"/>. Outside
    /// creative mode one is taken from the hand; an emptied hand holds the result, and otherwise the result goes into
    /// the inventory like <see cref="Give"/>, dropping when it doesn't fit. In creative mode the hand stays as it is;
    /// with <paramref name="limitCreative"/> (vanilla's default) the result goes into the inventory only when no
    /// storage slot holds the same item already, and without it, always.
    /// </summary>
    /// <returns>
    /// The temporary result after routing, which vanilla's <c>filled_bucket</c> criterion reads: whole when it went
    /// into the hand, dropped, or wasn't needed in creative mode; empty when the inventory took it.
    /// </returns>
    public static ItemStack Exchange(BlockUse use, ItemStack result, bool limitCreative = true)
    {
        InsideCommit(use);
        var inventory = use.Player.Inventory;
        if (use.Player.GameMode == GameMode.Creative)
        {
            if (!limitCreative)
                return Give(use, result);

            var held = storageSlots.Any(slot => inventory.GetItem(slot) is ItemStack item && item.Equals(result));
            return held ? new ItemStack(result, result.Count) : Give(use, result);
        }

        inventory.RemoveItem(use.HandSlot, 1);
        use.ChangedInventorySlots.Add(use.HandSlot);
        use.ResyncHand = true;
        if (inventory.GetItem(use.HandSlot) is not null)
            return Give(use, result);

        inventory.SetItem(use.HandSlot, new ItemStack(result, result.Count));
        return new ItemStack(result, result.Count);
    }

    /// <summary>Sends the slots the committed use changed. Call holding no lock.</summary>
    public static async ValueTask SendChangedSlotsAsync(BlockUse use)
    {
        foreach (var slot in use.ChangedInventorySlots)
            await use.Player.SyncInventorySlotAsync(slot);
    }

    // The commit's effects, which only TryCommit's change has: the inventory and tick lock are held there.
    private static BlockUseEffects InsideCommit(BlockUse use) =>
        use.Level.ActiveBlockUse ?? throw new InvalidOperationException(
            "An item is given to a player only inside BlockUse.TryCommit's change.");
}
