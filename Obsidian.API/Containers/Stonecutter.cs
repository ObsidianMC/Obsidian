namespace Obsidian.API.Containers;

public sealed class Stonecutter : ResultContainer
{
    public Stonecutter() : base(2, InventoryType.Stonecutter)
    {
        Title = "Stonecutter";
    }

    public override ItemStack? GetResult() => throw new NotImplementedException();
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
}
