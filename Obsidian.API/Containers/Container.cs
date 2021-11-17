namespace Obsidian.API;

public sealed class Container : BaseContainer, ITileEntity
{
    internal int StateId { get; set; }

    public string Id { get; set; }

    public Vector BlockPosition { get; set; }

    public Guid Owner { get; set; }

    public bool IsPlayerInventory { get; internal init; }

    public Container(InventoryType type = InventoryType.Generic) : this(9 * 3, type) { }

    public Container(int size, InventoryType type = InventoryType.Generic) : base(size, type)
    {
        if (type is not InventoryType.Generic or InventoryType.ShulkerBox)
            throw new InvalidOperationException("Inventory type can only be Generic or ShulkerBox");
        if (size % 9 is not 0 && size is not 46 or 5)
            throw new InvalidOperationException("Size must be divisble by 9");
        if (size > 9 * 6)
            throw new InvalidOperationException($"Size must be <= {9 * 6}");
    }

    public override int AddItem(ItemStack item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (this.IsPlayerInventory)
        {
            for (int i = 45; i > 8; i--)
            {
                var invItem = this.items[i];

                if (invItem?.Type == item.Type)
                {
                    if (invItem.Count >= 64)
                        continue;

                    invItem.Count += item.Count;

                    return i;
                }

                if (invItem == null)
                {
                    this.items[i] = item;

                    return i;
                }
            }
        }
        else
        {
            return base.AddItem(item);
        }

        return -1;
    }


    public void ToNbt() => throw new NotImplementedException();
    public void FromNbt() => throw new NotImplementedException();
}
