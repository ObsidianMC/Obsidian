using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// An enchanting table's window: the item to enchant in slot 0 and the lapis lazuli in slot 1.
/// </summary>
public sealed class EnchantmentTable : BaseContainer
{
    /// <summary>
    /// Gets the container's id, <c>enchantment_table</c>.
    /// </summary>
    public string Id => "enchantment_table";

    /// <summary>
    /// Gets or sets the position of the enchanting table, whose bookshelves the offers count.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Creates an enchanting table window's two empty slots.
    /// </summary>
    public EnchantmentTable() : base(2, InventoryType.Enchantment)
    {
        this.Title = "Enchantment Table";
    }

    /// <inheritdoc/>
    /// <exception cref="IndexOutOfRangeException"><paramref name="slot"/> isn't 0 or 1.</exception>
    public override void SetItem(int slot, ItemStack? item)
    {
        if (slot > this.Size - 1 || slot < 0)
            throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

        base.SetItem(slot, item);
    }

}
