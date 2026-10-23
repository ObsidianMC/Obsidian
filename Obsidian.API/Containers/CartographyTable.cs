using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A cartography table's window, like vanilla's <c>CartographyTableMenu</c>: the map, the paper, empty map or glass
/// pane that changes it, and the result.
/// </summary>
public sealed class CartographyTable : ResultContainer
{
    public const int MapSlot = 0;
    public const int AdditionalSlot = 1;
    public const int ResultSlot = 2;

    public Vector BlockPosition { get; set; }

    public CartographyTable() : base(3, InventoryType.CartographyTable)
    {
        this.Title = "Cartography Table";
    }

    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
}
