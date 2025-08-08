using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

public sealed class CraftingTable : ResultContainer
{
    public CraftingTable(InventoryType type = InventoryType.Crafting) : base(10, type)
    {
        if(type != InventoryType.Crafting || type != InventoryType.Crafter)
            throw new ArgumentException("CraftingTable must be of type InventoryType.Crafting or InventoryType.Crafter", nameof(type));

        this.Title = type == InventoryType.Crafting ? "Crafting Table" : "Crafter";
    }

    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    public override ItemStack? GetResult() => throw new NotImplementedException();
}
