namespace Obsidian.API.Containers;

public sealed class CraftingTable : ResultContainer
{
    public CraftingTable() : base(10, InventoryType.Crafting)
    {
        this.Title = "Crafting Table";
    }

    public override ItemStack? GetResult() => throw new NotImplementedException();
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
}
