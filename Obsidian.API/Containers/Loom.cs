using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

public sealed class Loom : ResultContainer
{
    public Loom() : base(4, InventoryType.Loom)
    {
        this.Title = "Loom";
    }

    public override ItemStack? GetResult() => throw new NotImplementedException();
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
}
