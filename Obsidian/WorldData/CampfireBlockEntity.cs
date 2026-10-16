using Obsidian.API;
using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>CampfireBlockEntity</c>: four food slots, each cooked by its recipe's cook time while the campfire is lit.
/// </summary>
/// <remarks>
/// Saved by <see cref="BlockEntityNbt"/> with vanilla's <c>Items</c>, <c>CookingTimes</c> and <c>CookingTotalTimes</c> keys.
/// </remarks>
public sealed class CampfireBlockEntity : IBlockEntity
{
    /// <summary>Vanilla's campfire holds four items.</summary>
    public const int SlotCount = 4;

    public required string Id { get; init; }

    public required Vector BlockPosition { get; init; }

    /// <summary>The food in each slot, or <c>null</c> when empty.</summary>
    public ItemStack?[] Items { get; } = new ItemStack?[SlotCount];

    /// <summary>Ticks each slot has cooked so far.</summary>
    public int[] CookingProgress { get; } = new int[SlotCount];

    /// <summary>Ticks each slot needs to finish cooking.</summary>
    public int[] CookingTotal { get; } = new int[SlotCount];

    /// <summary>The index of the first empty slot, or -1 when all four hold food.</summary>
    public int FirstEmptySlot()
    {
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (Items[slot] is not { IsAir: false, Count: > 0 })
                return slot;
        }

        return -1;
    }

    /// <summary>Whether any slot still holds food.</summary>
    public bool HasFood() => Items.Any(item => item is { IsAir: false, Count: > 0 });

    public void ToNbt() => throw new NotSupportedException("Campfires are saved by BlockEntityNbt.");

    public void FromNbt() => throw new NotSupportedException("Campfires are loaded by BlockEntityNbt.");

    public IBlockEntity Clone()
    {
        var copy = new CampfireBlockEntity { Id = Id, BlockPosition = BlockPosition };
        Array.Copy(Items, copy.Items, SlotCount);
        Array.Copy(CookingProgress, copy.CookingProgress, SlotCount);
        Array.Copy(CookingTotal, copy.CookingTotal, SlotCount);
        return copy;
    }
}
