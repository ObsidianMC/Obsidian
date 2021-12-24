namespace Obsidian.API.Containers;

public class SmeltingContainer : ResultContainer, IBlockEntity
{
    public string Id { get; }

    public short FuelBurnTime { get; set; }

    public short CookTime { get; set; }

    public short CookTimeTotal { get; set; }

    public Vector BlockPosition { get; set; }

    internal SmeltingContainer(InventoryType type, string id) : base(3, type)
    {
        if (type != InventoryType.Furnace || type != InventoryType.Smoker || type != InventoryType.BlastFurnace || type != InventoryType.Custom)
            throw new InvalidOperationException("Type must either be custom, furnace, blast furnace or smoker.");

        this.Id = id;
    }

    public SmeltingContainer(InventoryType type, int size, string id) : base(size, type)
    {
        if (type != InventoryType.Furnace || type != InventoryType.Smoker || type != InventoryType.BlastFurnace || type != InventoryType.Custom)
            throw new InvalidOperationException("Type must either be custom, furnace, blast furnace or smoker.");

        this.Id = id;
    }

    public void ToNbt() => throw new NotImplementedException();
    public void FromNbt() => throw new NotImplementedException();
    public virtual ItemStack? GetFuel() => throw new NotImplementedException();
    public virtual ItemStack?[] GetIngredients() => throw new NotImplementedException();
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    public override ItemStack? GetResult() => throw new NotImplementedException();
}
