using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

public sealed class CraftingTable : ResultContainer
{
    public CraftingTable() : base(10, InventoryType.Crafting)
    {
        this.Title = "Crafting Table";
    }

    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    public override ItemStack? GetResult() => throw new NotImplementedException();
}

public sealed class Crafter : ResultContainer
{
    public Crafter() : base(3 * 3, InventoryType.Crafter)
    {

    }

    public override ItemStack? GetResult() => throw new NotImplementedException();
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
}
