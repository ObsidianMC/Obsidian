using Obsidian.API.Inventory;

namespace Obsidian.API;

public sealed class Container : BaseContainer, IBlockEntity
{
    private const int PlayerHotbarStart = 36;
    private const int PlayerHotbarEnd = PlayerHotbarStart + 8;
    private const int PlayerMainInventoryStart = 9;
    private const int PlayerMainInventoryEnd = PlayerHotbarStart - 1;

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
        ArgumentNullException.ThrowIfNull(item);

        if (this.IsPlayerInventory)
        {
            int? slot = InventoryItem(item, PlayerHotbarStart, PlayerHotbarEnd);
            if (slot is null) slot = InventoryItem(item, PlayerMainInventoryStart, PlayerMainInventoryEnd);
            if (slot is not null) return slot.Value;
        }
        else
        {
            return base.AddItem(item);
        }

        return -1;
    }

    private int? InventoryItem(ItemStack item, int start, int end)
    {
        for (int i = start; i <= end; i++)
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
        return null;
    }
    public void ToNbt() => throw new NotImplementedException();
    public void FromNbt() => throw new NotImplementedException();
}
