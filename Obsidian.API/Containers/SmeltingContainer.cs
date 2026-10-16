using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

public class SmeltingContainer : ResultContainer, IBlockEntity
{
    public string Id { get; }

    /// <summary>Ticks left until the lit fuel runs out (vanilla <c>lit_time_remaining</c>).</summary>
    public short FuelBurnTime { get; set; }

    /// <summary>The burn time of the fuel that lit the furnace (vanilla <c>lit_total_time</c>).</summary>
    public short FuelBurnTotal { get; set; }

    /// <summary>Ticks of cooking progress on the current input (vanilla <c>cooking_time_spent</c>).</summary>
    public short CookTime { get; set; }

    public short CookTimeTotal { get; set; }

    public Vector BlockPosition { get; set; }

    internal SmeltingContainer(InventoryType type, string id) : this(type, 3, id) { }

    public SmeltingContainer(InventoryType type, int size, string id) : base(size, type)
    {
        if (type is not InventoryType.Furnace and not InventoryType.Smoker and not InventoryType.BlastFurnace and not InventoryType.Custom)
            throw new InvalidOperationException("Type must either be custom, furnace, blast furnace or smoker.");

        this.Id = id;
    }

    // Slot 0 is the ingredient, slot 1 the fuel and slot 2 the result.
    public virtual ItemStack? GetFuel() => this.GetItem(1);
    public virtual ItemStack?[] GetIngredients() => [this.GetItem(0)];

    public override void SetResult(ItemStack? result) => this.SetItem(2, result);
    public override ItemStack? GetResult() => this.GetItem(2);

    public void ToNbt() => throw new NotImplementedException();
    public void FromNbt() => throw new NotImplementedException();
    public IBlockEntity Clone() => new SmeltingContainer(this.Type, this.Size, this.Id)
    {
        BlockPosition = this.BlockPosition,
        Title = this.Title,
        CustomName = this.CustomName,
        items = this.items,
        CookTime = this.CookTime,
        CookTimeTotal = this.CookTimeTotal,
        FuelBurnTime = this.FuelBurnTime,
        FuelBurnTotal = this.FuelBurnTotal,
    };
}
