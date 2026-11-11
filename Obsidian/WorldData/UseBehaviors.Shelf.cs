using Obsidian.API.Inventory;
using Obsidian.Entities;
using System.Text.Json;

namespace Obsidian.WorldData;

// The use behaviour of shelves.
internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior ShelfBlock { get; } = new ShelfUse();

    /// <summary>
    /// Vanilla's <c>ShelfBlock.useItemOn</c>, for the main hand: a click on a shelf's front picks one of its three
    /// slots. An unpowered shelf swaps that slot with the hand, whole stacks, empty or not; a powered one swaps its
    /// row (and the rows of the shelves connected to it) with the end of the hotbar.
    /// </summary>
    private sealed class ShelfUse : IBlockUseBehavior
    {
        private const int Rows = 1;

        // The player inventory's first hotbar slot; the hotbar's slot i is this plus i.
        private const int FirstHotbarSlot = 36;

        private const int HotbarSize = 9;

        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.BlockEntity is not ShelfBlockEntity shelf || use.Hand == InteractionHand.OffHand)
                return UseResult.Pass;

            var hit = SelectableSlots.HitSlot(use.Block, use.Face, use.Cursor, Rows, ShelfBlockEntity.Slots);
            if (hit is not int slot)
                return UseResult.Pass;

            if (use.Block.GetProperty("powered") == "true")
                return await SwapRowsAsync(use);

            return await SwapOneAsync(use, shelf, slot);
        }

        // Vanilla's swapSingleItem: the hand's stack goes onto the shelf and what was there into the hand. A creative
        // player taking nothing off the shelf keeps what they held.
        private static async ValueTask<UseResult> SwapOneAsync(BlockUse use, ShelfBlockEntity shelf, int slot)
        {
            var held = use.HasItem ? use.Item : null;
            ItemStack? removed = null;
            ItemStack? inHand = null;
            var committed = use.TryCommit(() =>
            {
                var placed = held is null ? null : new ItemStack(held, held.Count);
                removed = shelf.Swap(slot, placed);

                var keeps = use.Player.GameMode == GameMode.Creative && removed is null && held is not null;
                inHand = keeps ? new ItemStack(held!, held!.Count) : removed;
                use.Player.Inventory.SetItem(use.HandSlot, inHand);
                use.ChangedInventorySlots.Add(use.HandSlot);
                use.ResyncHand = true;
                return true;
            }, destination: shelf.Inventory);

            if (!committed)
                return UseResult.Fail;

            shelf.Changed(InteractsAudibly(inHand) ? MobGameEvent.ItemInteractFinish : null);
            await BlockUseReturn.SendChangedSlotsAsync(use);

            string sound;
            if (removed is not null)
                sound = held is null ? "minecraft:block.shelf.take_item" : "minecraft:block.shelf.single_swap";
            else if (held is null)
                return UseResult.Pass;
            else
                sound = "minecraft:block.shelf.place_item";

            use.Level.BlockUpdates.PlaySound(use.Position, sound);
            return UseResult.SuccessServer;
        }

        // Vanilla's swapHotbar: each shelf of the row connected to the clicked one (at most three, from the left as one
        // looks at them) swaps its three slots with three slots of the hotbar, which end with the hotbar's last. Every
        // shelf's inventory and the player's are held together, so that no item is in two places at once.
        private static async ValueTask<UseResult> SwapRowsAsync(BlockUse use)
        {
            var held = use.Player.Inventory.GetItem(use.HandSlot);
            var clicked = (ShelfBlockEntity)use.BlockEntity!;
            ShelfBlockEntity?[] row = [];
            var swapped = false;

            // The row is read inside the commit, under the tick lock, once the clicked shelf was found as it was read.
            var committed = use.TryCommit(() =>
            {
                row = [.. ShelfConnections.ConnectedTo(use.Level, use.Position)
                    .Select(position => use.Level.GetResidentBlockEntity(position) as ShelfBlockEntity)];
                if (!TryLockOthers(use, row, out var others))
                    return false;

                using (others)
                    swapped = SwapLocked(use, row);

                return true;
            }, destination: clicked.Inventory);

            if (!committed)
                return UseResult.Fail;

            if (row.Length == 0)
                return UseResult.Consume;

            foreach (var shelf in row.OfType<ShelfBlockEntity>())
                shelf.Changed(MobGameEvent.EntityInteract);

            await BlockUseReturn.SendChangedSlotsAsync(use);
            if (!swapped)
                return UseResult.Consume;

            use.Level.BlockUpdates.PlaySound(use.Position, "minecraft:block.shelf.multi_swap");
            if (!ReferenceEquals(held, use.Player.Inventory.GetItem(use.HandSlot)))
                use.ResyncHand = true;

            return UseResult.SuccessServer;
        }

        // The inventories of the row's other shelves, inside the commit, which holds the player's and the clicked
        // shelf's: under the tick lock they're only tried, never waited for, and a busy one refuses the use.
        private static bool TryLockOthers(BlockUse use, ShelfBlockEntity?[] row, out HeldLocks others)
        {
            others = new HeldLocks();
            foreach (var shelf in row.OfType<ShelfBlockEntity>())
            {
                if (shelf.BlockPosition == use.Position)
                    continue;

                if (shelf.IsRemoved || !shelf.Inventory.TryLock(out var shelfLock))
                {
                    others.Dispose();
                    return false;
                }

                others.Add(shelfLock);
            }

            return true;
        }

        private static bool SwapLocked(BlockUse use, ShelfBlockEntity?[] row)
        {
            var inventory = use.Player.Inventory;
            var swapped = false;
            for (var part = 0; part < row.Length; part++)
            {
                if (row[part] is not ShelfBlockEntity shelf)
                    continue;

                for (var slot = 0; slot < ShelfBlockEntity.Slots; slot++)
                {
                    // Vanilla's 9 - (parts - part) * 3 + slot: the row ends with the hotbar's last slot.
                    var hotbar = HotbarSize - (row.Length - part) * ShelfBlockEntity.Slots + slot;
                    if (hotbar < 0)
                        continue;

                    var inventorySlot = FirstHotbarSlot + hotbar;
                    var placed = inventory.GetItem(inventorySlot);
                    var removed = shelf.Swap(slot, placed);
                    if (placed.IsNullOrAir() && removed is null)
                        continue;

                    inventory.SetItem(inventorySlot, removed);
                    use.ChangedInventorySlots.Add(inventorySlot);
                    swapped = true;
                }
            }

            return swapped;
        }

        // Vanilla's use_effects interact_vibrations, from the item's default components: whether putting the item in
        // the hand is heard. Spears' aren't.
        private static bool InteractsAudibly(ItemStack? item)
        {
            if (item is null)
                return true;

            var effects = CombatItems.Component(item, "use_effects");
            if (effects.ValueKind != JsonValueKind.Object
                || !effects.TryGetProperty("interact_vibrations", out var vibrations))
            {
                return true;
            }

            return vibrations.GetBoolean();
        }
    }

    /// <summary>Inventory locks held together, released together.</summary>
    private sealed class HeldLocks : IDisposable
    {
        private readonly List<IDisposable> locks = [];

        public void Add(IDisposable held) => this.locks.Add(held);

        public void Dispose()
        {
            foreach (var held in this.locks)
                held.Dispose();

            this.locks.Clear();
        }
    }
}
