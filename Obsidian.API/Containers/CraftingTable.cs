using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A crafting window's slots: the result in slot 0 and the 3 by 3 grid in slots 1 to 9.
/// </summary>
public sealed class CraftingTable : ResultContainer
{
    /// <summary>
    /// Creates a crafting window's empty slots.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> isn't <see cref="InventoryType.Crafting"/> or
    /// <see cref="InventoryType.Crafter"/>.</exception>
    public CraftingTable(InventoryType type = InventoryType.Crafting) : base(10, type)
    {
        if (type is not (InventoryType.Crafting or InventoryType.Crafter))
            throw new ArgumentException("CraftingTable must be of type InventoryType.Crafting or InventoryType.Crafter", nameof(type));

        this.Title = type == InventoryType.Crafting ? "Crafting Table" : "Crafter";
    }

    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(0, result);

    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(0);
}
