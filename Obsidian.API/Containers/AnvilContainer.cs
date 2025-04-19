using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

public sealed class AnvilContainer : ResultContainer, IBlockEntity
{
    public string Id { get; }

    public Vector BlockPosition { get; set; }

    public AnvilContainer(string id) : base(3, InventoryType.Anvil)
    {
        this.Id = id;
    }

    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    public override ItemStack? GetResult() => throw new NotImplementedException();
    public void ToNbt() => throw new NotImplementedException();
    public void FromNbt() => throw new NotImplementedException();
    public IBlockEntity Clone() => new AnvilContainer(this.Id)
    {
        BlockPosition = this.BlockPosition,
        Title = this.Title,
        items = this.items
    };
}
