using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A grindstone's window, like vanilla's <c>GrindstoneMenu</c>: two inputs and the result.
/// </summary>
public sealed class Grindstone : ResultContainer
{
    /// <summary>
    /// The result slot's index.
    /// </summary>
    public const int ResultSlot = 2;

    /// <summary>
    /// Gets or sets the position of the grindstone the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Creates a grindstone window's slots.
    /// </summary>
    public Grindstone() : base(3, InventoryType.Grindstone)
    {
        this.Title = "Repair & Disenchant";
    }

    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
}
