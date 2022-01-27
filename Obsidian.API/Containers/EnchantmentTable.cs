namespace Obsidian.API.Containers;

public sealed class EnchantmentTable : BaseContainer, IBlockEntity
{
    public string Id => "enchantment_table";

    public Vector BlockPosition { get; set; }

    public EnchantmentTable() : base(2, InventoryType.Enchantment)
    {
        Title = "Enchantment Table";
    }

    public override void SetItem(int slot, ItemStack? item)
    {
        if (slot > Size - 1 || slot < 0)
            throw new IndexOutOfRangeException($"{slot} > {Size - 1}");

        items[slot] = item;
    }

    public void ToNbt() => throw new NotImplementedException();
    public void FromNbt() => throw new NotImplementedException();
}
