using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A smithing table's window, like vanilla's <c>SmithingMenu</c>: the template, the base item, the addition and the
/// result.
/// </summary>
public sealed class SmithingTable : ResultContainer
{
    /// <summary>
    /// The smithing template slot's index.
    /// </summary>
    public const int TemplateSlot = 0;
    /// <summary>
    /// The base item slot's index.
    /// </summary>
    public const int BaseSlot = 1;
    /// <summary>
    /// The addition slot's index.
    /// </summary>
    public const int AdditionSlot = 2;
    /// <summary>
    /// The result slot's index.
    /// </summary>
    public const int ResultSlot = 3;

    /// <summary>
    /// Gets or sets the position of the smithing table the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Creates a smithing table window's slots.
    /// </summary>
    public SmithingTable() : base(4, InventoryType.Smithing)
    {
        this.Title = "Upgrade Gear";
    }

    /// <inheritdoc/>
    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
    /// <inheritdoc/>
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
}
