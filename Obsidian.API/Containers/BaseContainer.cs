using System.Collections;

namespace Obsidian.API;

public abstract class BaseContainer : IEnumerable<ItemStack>
{
    protected ItemStack?[] items;

    public int Size => items.Length;

    public InventoryType Type { get; }

    public ChatMessage? Title { get; set; }

    public Guid Uuid { get; } = Guid.NewGuid();

    public List<IPlayer> Viewers { get; } = new();

    public ItemStack? this[int index] { get => items[index]; set => items[index] = value; }

    public BaseContainer(int size) : this(size, InventoryType.Custom) { }

    internal BaseContainer(int size, InventoryType type)
    {
        Type = type;

        items = new ItemStack?[size];
    }

    //TODO match item meta
    public virtual int AddItem(ItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (int i = 0; i < Size; i++)
        {
            var invItem = items[i];

            if (invItem?.Type == item.Type)
            {
                //TODO use the items max stack size
                if (invItem.Count >= 64)
                    continue;

                invItem.Count += item.Count;

                return i;
            }

            if (invItem == null)
            {
                items[i] = item;

                return i;
            }
        }

        return -1;
    }

    public virtual void SetItem(int slot, ItemStack? item) => items[slot] = item;

    public virtual ItemStack? GetItem(int slot) => items[slot];

    public virtual bool RemoveItem(int slot)
    {
        if (items[slot] == null)
            return false;

        SetItem(slot, null);

        return true;
    }

    public virtual bool RemoveItem(int slot, int amount)
    {
        var item = items[slot];

        if (item == null)
            return false;

        item.Count -= amount;
        if (item.Count <= 0)
            SetItem(slot, null);

        return true;
    }

    public virtual bool RemoveItem(int slot, out ItemStack? removedItem)
    {
        var item = items[slot];

        if (item == null)
        {
            removedItem = null;
            return false;
        }

        removedItem = item;

        return RemoveItem(slot);
    }

    public virtual bool RemoveItem(int slot, int amount, out ItemStack? removedItem)
    {
        var item = items[slot];

        if (item == null)
        {
            removedItem = null;
            return false;
        }

        removedItem = item;

        return RemoveItem(slot, amount);
    }

    public virtual (int slot, bool forPlayer) GetDifference(int clickedSlot) =>
        clickedSlot > Size ? (clickedSlot - Size + 9, true) : (clickedSlot, false);

    public virtual void Resize(int newSize) => Array.Resize(ref items, newSize);

    public bool HasItems() => items.Any(x => x is not null);

    public IEnumerator<ItemStack> GetEnumerator() => (items as IEnumerable<ItemStack>).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public abstract class ResultContainer : BaseContainer
{
    protected ResultContainer(int size) : this(size, InventoryType.Custom) { }

    internal ResultContainer(int size, InventoryType type) : base(size, type) { }

    public abstract void SetResult(ItemStack? result);

    public abstract ItemStack? GetResult();
}
