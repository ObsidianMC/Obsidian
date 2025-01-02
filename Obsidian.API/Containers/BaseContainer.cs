using Obsidian.API.Inventory;
using System.Collections;

namespace Obsidian.API;

public abstract class BaseContainer : IEnumerable<ItemStack>
{
    protected ItemStack?[] items;

    public int Size => this.items.Length;

    public InventoryType Type { get; }

    public ChatMessage Title { get; set; }

    public Guid Uuid { get; } = Guid.NewGuid();

    public List<IPlayer> Viewers { get; } = new();

    public ItemStack? this[int index] { get => this.items[index]; set => this.items[index] = value; }

    public BaseContainer(int size) : this(size, InventoryType.Custom) { }

    internal BaseContainer(int size, InventoryType type)
    {
        this.Type = type;

        this.items = new ItemStack?[size];
    }

    //TODO match item meta
    public virtual int AddItem(ItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (int i = 0; i < this.Size; i++)
        {
            var invItem = this.items[i];

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
                this.items[i] = item;

                return i;
            }
        }

        return -1;
    }

    public virtual void SetItem(int slot, ItemStack? item) => this.items[slot] = item;

    public virtual ItemStack? GetItem(int slot) => this.items[slot];

    public virtual bool RemoveItem(int slot)
    {
        if (this.items[slot] == null)
            return false;

        this.SetItem(slot, null);

        return true;
    }

    public virtual bool RemoveItem(int slot, int amount)
    {
        var item = this.items[slot];

        if (item == null)
            return false;

        item.Count -= amount;
        if (item.Count <= 0)
            this.SetItem(slot, null);

        return true;
    }

    public virtual bool RemoveItem(int slot, out ItemStack? removedItem)
    {
        var item = this.items[slot];

        if (item == null)
        {
            removedItem = null;
            return false;
        }

        removedItem = item;

        return this.RemoveItem(slot);
    }

    public virtual bool RemoveItem(int slot, int amount, out ItemStack? removedItem)
    {
        var item = this.items[slot];

        if (item == null)
        {
            removedItem = null;
            return false;
        }

        removedItem = item;

        return this.RemoveItem(slot, amount);
    }

    public virtual (int slot, bool forPlayer) GetDifference(int clickedSlot) =>
        clickedSlot > this.Size ? (clickedSlot - this.Size + 9, true) : (clickedSlot, false);

    public virtual void Resize(int newSize) => Array.Resize(ref this.items, newSize);

    public bool HasItems() => this.items.Any(x => x is not null);

    public IEnumerator<ItemStack> GetEnumerator() => (this.items as IEnumerable<ItemStack>).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public abstract class ResultContainer : BaseContainer
{
    protected ResultContainer(int size) : this(size, InventoryType.Custom) { }

    internal ResultContainer(int size, InventoryType type) : base(size, type) { }

    public abstract void SetResult(ItemStack? result);

    public abstract ItemStack? GetResult();
}
