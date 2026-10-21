using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A smithing table's window, like vanilla's <c>SmithingMenu</c>: the template, the base item, the addition and the
/// result.
/// </summary>
public sealed class SmithingTable : ResultContainer
{
    public const int TemplateSlot = 0;
    public const int BaseSlot = 1;
    public const int AdditionSlot = 2;
    public const int ResultSlot = 3;

    public Vector BlockPosition { get; set; }

    public SmithingTable() : base(4, InventoryType.Smithing)
    {
        this.Title = "Upgrade Gear";
    }

    public override void SetResult(ItemStack? result) => this.SetItem(ResultSlot, result);
    public override ItemStack? GetResult() => this.GetItem(ResultSlot);
}
