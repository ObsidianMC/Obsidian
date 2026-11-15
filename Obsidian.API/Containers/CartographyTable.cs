using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A cartography table's window, like vanilla's <c>CartographyTableMenu</c>: the map, the paper, empty map or glass
/// pane that changes it, and the result.
/// </summary>
public sealed class CartographyTable : ResultContainer
{
    /// <summary>
    /// The map slot's index.
    /// </summary>
    public const int MapSlot = 0;
    /// <summary>
    /// The index of the slot for the paper, empty map or glass pane that changes the map.
    /// </summary>
    public const int AdditionalSlot = 1;
    /// <summary>
    /// The result slot's index.
    /// </summary>
    public const int ResultSlot = 2;

    /// <summary>
    /// Gets or sets the position of the cartography table the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Creates a cartography table window's slots.
    /// </summary>
    public CartographyTable() : base(3, InventoryType.CartographyTable)
    {
        this.Title = "Cartography Table";
    }

    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
}
