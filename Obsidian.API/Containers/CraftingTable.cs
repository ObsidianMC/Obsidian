using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

public sealed class CraftingTable : ResultContainer
{
    private ItemStack? result;

    public CraftingTable(InventoryType type = InventoryType.Crafting) : base(10, type)
    {
        if(type is not InventoryType.Crafting or InventoryType.Crafter)
            throw new ArgumentException("CraftingTable must be of type InventoryType.Crafting or InventoryType.Crafter", nameof(type));

        this.Title = type == InventoryType.Crafting ? "Crafting Table" : "Crafter";
    }

    public override void SetResult(ItemStack? result) => this.result = result;

    public override ItemStack? GetResult() => this.result;
}
