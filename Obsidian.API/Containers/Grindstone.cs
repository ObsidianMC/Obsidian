using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A grindstone's window, like vanilla's <c>GrindstoneMenu</c>: two inputs and the result.
/// </summary>
public sealed class Grindstone : ResultContainer
{
    public const int ResultSlot = 2;

    public Vector BlockPosition { get; set; }

    public Grindstone() : base(3, InventoryType.Grindstone)
    {
        this.Title = "Repair & Disenchant";
    }

    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
}
