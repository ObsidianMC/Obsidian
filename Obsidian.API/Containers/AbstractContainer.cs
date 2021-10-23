using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API
{
    public abstract class AbstractContainer : IEnumerable<ItemStack>, IEnumerable
    {
        protected ItemStack?[] items;

        public int Size { get; protected set; }

        public InventoryType Type { get; }

        public ChatMessage? Title { get; set; }

        public Guid Uuid { get; private set; } = Guid.NewGuid();

        public List<IPlayer> Viewers { get; private set; } = new List<IPlayer>();

        public AbstractContainer(int size) : this(size, InventoryType.Custom) { }

        internal AbstractContainer(int size, InventoryType type)
        {
            this.Size = size;
            this.Type = type;

            this.items = new ItemStack?[size];
        }

        //TODO match item meta
        public virtual int AddItem(ItemStack item)
        {
            for (int i = 0; i < this.Size; i++)
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

            return -1;
        }

        public virtual void SetItem(int slot, ItemStack? item) => this.items[slot] = item;

        public virtual ItemStack? GetItem(int slot) => this.items[slot];

        public virtual bool RemoveItem(int slot, short amount = 1)
        {
            var item = this.items[slot];

            if (item == null)
                return false;

            if (amount >= 64 || item.Count - amount <= 0)
                this.items[slot] = null;
            else
                item.Count -= amount;

            return true;
        }

        public virtual bool TryRemoveItem(int slot, out ItemStack? removedItem)
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

        public virtual bool TryRemoveItem(int slot, short amount, out ItemStack? removedItem)
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

        public virtual bool HasItems() => this.items.Any(x => x is not null);

        public virtual (int slot, bool forPlayer) GetDifference(short clickedSlot) =>
            clickedSlot > this.Size ? (clickedSlot - this.Size + 9, true) : (clickedSlot, false);

        public IEnumerator<ItemStack> GetEnumerator() => (this.items as IEnumerable<ItemStack>).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public abstract class AbstractResultContainer : AbstractContainer
    {
        protected AbstractResultContainer(int size) : this(size, InventoryType.Custom) { }

        internal AbstractResultContainer(int size, InventoryType type) : base(size, type) { }

        public abstract void SetResult(ItemStack? result);

        public abstract ItemStack? GetResult();
    }

    public abstract class AbstractSmeltingContainer : AbstractResultContainer
    {
        protected AbstractSmeltingContainer(int size) : this(size, InventoryType.Custom) { }

        internal AbstractSmeltingContainer(int size, InventoryType type) : base(size, type) { }

        public short FuelBurnTime { get; set; }

        public short CookTime { get; set; }

        public short CookTimeTotal { get; set; }

        public abstract ItemStack? GetFuel();

        public abstract ItemStack? GetIngredient();
    }
}
