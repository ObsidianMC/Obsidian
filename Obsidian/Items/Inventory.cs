using Obsidian.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Items
{
    public class Inventory
    {
        internal int Id { get; set; }

        internal int ActionsNumber { get; set; }

        public Player Owner { get; set; }

        public InventoryType Type { get; set; }

        public string Title { get; set; }

        public int Size { get; set; } = 9 * 4;

        public ConcurrentDictionary<int, ItemStack> Items { get; private set; } = new ConcurrentDictionary<int, ItemStack>();

        public List<Player> Viewers { get; private set; } = new List<Player>();

        public void AddItems(params ItemStack[] items)
        {
            foreach (var item in items)
            {
                if (item is null)
                    throw new NullReferenceException(nameof(item));

                this.AddItem(item);
            }
        }

        public void AddItem(ItemStack item)
        {
            int lastIndex = this.Items.Keys.OrderByDescending(x => x).FirstOrDefault();
            lastIndex = lastIndex == 0 ? 0 : lastIndex + 1;

            this.Items.TryAdd(lastIndex, item);
        }

        public void SetItem(int slot, ItemStack item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            if (this.Items.ContainsKey(slot))
                this.Items[slot] = item;
            else
                this.Items.TryAdd(slot, item);
        }

        public ItemStack GetItem(int slot)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            if (!this.Items.ContainsKey(slot))
                throw new KeyNotFoundException(nameof(slot));

            return this.Items.GetValueOrDefault(slot);
        }

        public bool RemoveItem(int slot, int amount = 1)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            if (!this.Items.ContainsKey(slot))
                return false;

            if (amount >= 64 || this.Items[slot].Count - amount <= 0 )
                return this.Items.TryRemove(slot, out var _);

            this.Items[slot].Count -= amount;

            return true;
        }
    }

    public enum InventoryType
    {
        Generic,
        Anvil,
        Beacon,
        BlastFurnace,
        BrewingStand,
        Crafting,
        Enchantment,
        Furnace,
        Grindstone,
        Hopper,
        Lectern,
        Loom,
        Merchant,
        ShulkerBox,
        Smoker,
        CartographyTable,
        Stonecutter
    }
}
