using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

// The use behaviour of chiseled bookshelves.
internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior ChiseledBookShelfBlock { get; } = new ChiseledBookshelfUse();

    /// <summary>
    /// Vanilla's <c>ChiseledBookShelfBlock</c>: a click on its front picks a slot by where it lands; a book goes into
    /// an empty slot, and a click without one takes the slot's book.
    /// </summary>
    private sealed class ChiseledBookshelfUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.BlockEntity is not ChiseledBookshelfBlockEntity shelf)
                return UseResult.Pass;

            if (!ChiseledBookshelfBlockEntity.IsBook(use.Item))
                return UseResult.TryWithEmptyHand;

            if (BookshelfSlots.HitSlot(use.Block, use.Face, use.Cursor) is not int slot)
                return UseResult.Pass;

            if (BookshelfSlots.IsOccupied(use.Block, slot))
                return UseResult.TryWithEmptyHand;

            var book = use.Item!;
            if (!use.TryCommit(() => shelf.TryPut(slot, book), consumeItem: true, destination: shelf.Inventory))
                return UseResult.Fail;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Player.Statistics.Increment(book.Holder.UnlocalizedName, type: "minecraft:used");

            var enchanted = book.Is(ItemsRegistry.EnchantedBook);
            var sound = enchanted
                ? "minecraft:block.chiseled_bookshelf.insert.enchanted"
                : "minecraft:block.chiseled_bookshelf.insert";
            use.Level.BlockUpdates.PlaySound(use.Position, sound);

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (use.BlockEntity is not ChiseledBookshelfBlockEntity shelf)
                return UseResult.Pass;

            if (BookshelfSlots.HitSlot(use.Block, use.Face, use.Cursor) is not int slot)
                return UseResult.Pass;

            if (!BookshelfSlots.IsOccupied(use.Block, slot))
                return UseResult.Consume;

            var enchanted = false;
            var taken = use.TryCommit(() =>
            {
                if (shelf.TryTake(slot) is not ItemStack book)
                    return false;

                enchanted = book.Is(ItemsRegistry.EnchantedBook);
                BlockUseReturn.Give(use, book);
                return true;
            }, destination: shelf.Inventory);

            if (!taken)
                return UseResult.Fail;

            var sound = enchanted
                ? "minecraft:block.chiseled_bookshelf.pickup.enchanted"
                : "minecraft:block.chiseled_bookshelf.pickup";
            use.Level.BlockUpdates.PlaySound(use.Position, sound);

            await BlockUseReturn.SendChangedSlotsAsync(use);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }
    }
}

/// <summary>
/// Which slot of a chiseled bookshelf a click picks, like vanilla's <c>SelectableSlotContainer</c> for its two rows
/// of three: only a click on the face it faces picks one.
/// </summary>
internal static class BookshelfSlots
{
    private const int Rows = 2;

    private const int Columns = 3;

    /// <summary>
    /// The slot under the click at <paramref name="cursor"/> on <paramref name="face"/>, or null for a face other than
    /// the shelf's front.
    /// </summary>
    public static int? HitSlot(IBlock shelf, BlockFace face, VectorD cursor)
    {
        var facing = shelf.GetProperty("facing");
        if (!string.Equals(face.ToString(), facing, StringComparison.OrdinalIgnoreCase))
            return null;

        // The point across the face from its left edge as one looks at it, and up from its bottom edge, in vanilla's
        // floats.
        var across = face switch
        {
            BlockFace.North => (float)(1.0 - cursor.X),
            BlockFace.South => (float)cursor.X,
            BlockFace.West => (float)cursor.Z,
            BlockFace.East => (float)(1.0 - cursor.Z),
            _ => float.NaN
        };
        if (float.IsNaN(across))
            return null;

        var up = (float)cursor.Y;
        var row = Section(1f - up, Rows);
        var column = Section(across, Columns);

        return column + row * Columns;
    }

    /// <summary>Whether the block shows a book in <paramref name="slot"/>.</summary>
    public static bool IsOccupied(IBlock shelf, int slot) => shelf.GetProperty($"slot_{slot}_occupied") == "true";

    // Vanilla's getSection: the sixteenth of the face the click lands on, in sections of equal width. Ported as vanilla
    // computes it in floats, since a click near a section's edge must pick the same slot.
    private static int Section(float relative, int sections)
    {
        var pixel = relative * 16f;
        var width = 16f / sections;
        var section = (int)MathF.Floor(pixel / width);

        return Math.Clamp(section, 0, sections - 1);
    }
}
