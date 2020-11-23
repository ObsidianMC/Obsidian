using Obsidian.Entities;
using Obsidian.Util.Registry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Obsidian.Items
{
    public class Inventory
    {
        internal int Id { get; set; }

        internal int ActionsNumber { get; set; }

        internal bool OwnedByPlayer { get; set; }

        public Guid? Owner { get; set; }

        public InventoryType Type { get; set; }

        public string Title { get; set; }

        public int Size { get; set; } = 9 * 5;

        public List<Player> Viewers { get; private set; } = new List<Player>();

        private short[] items { get; }

        private ItemMeta[] metaStore { get; set; }

        public Inventory(Guid? owner = null)
        {
            this.Owner = owner;
            this.items = new short[this.Size];
            this.metaStore = new ItemMeta[this.Size];
        }

        public void AddItems(params ItemStack[] items)
        {
            foreach (var item in items)
            {
                if (item is null)
                    throw new NullReferenceException(nameof(item));

                this.AddItem(item);
            }
        }

        public int AddItem(ItemStack item)
        {
            if (this.OwnedByPlayer)
            {
                for (int i = 36; i < 45; i++)
                {
                    var invItem = this.items[i];
                    var itemMeta = this.metaStore[i];

                    if (invItem > 0 && invItem == item.Id)//TODO match item meta
                    {
                        if (itemMeta.Count >= 64)
                            continue;

                        itemMeta.Count += item.Count;

                        return i;
                    }

                    this.items[i] = item.Id;

                    return i;
                }

                for (int i = 9; i < 36; i++)
                {
                    var invItem = this.items[i];
                    var itemMeta = this.metaStore[i];

                    if (invItem > 0)
                    {
                        if (itemMeta.Count >= 64)
                            continue;

                        itemMeta.Count += item.Count;

                        return i;
                    }

                    this.items[i] = item.Id;

                    return i;
                }
            }
            else
            {
                for (int i = 0; i < this.Size; i++)
                {
                    var invItem = this.items[i];
                    var itemMeta = this.metaStore[i];

                    if (invItem == item.Id && invItem > 0)//TODO match item meta
                    {
                        if (itemMeta.Count >= 64)
                            continue;

                        itemMeta.Count += item.Count;

                        return i;
                    }

                    this.items[i] = item.Id;

                    return i;
                }
            }

            return 9;
        }

        public void SetItem(int slot, ItemStack item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.items[slot] = item.Id;
            this.metaStore[slot] = item.ItemMeta;
        }

        public ItemStack GetItem(int slot)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            return new ItemStack(this.items[slot], this.metaStore[slot]);
        }

        public bool RemoveItem(int slot, short amount = 1)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.items[slot];

            if (item <= 0)
                return false;

            var itemMeta = this.metaStore[slot];
            if (amount >= 64 || itemMeta.Count - amount <= 0)
            {
                this.items[slot] = 0;
                this.metaStore[slot] = default;
            }
            else
                itemMeta.Count -= amount;

            return true;
        }

        public IReadOnlyList<ItemStack> GetItems()
        {
            var list = new List<ItemStack>();
            for (int i = 0; i < this.Size - 1; i++)
            {
                var id = this.items[i];

                var itemMeta = this.metaStore[i];
                var item = Registry.GetItem(id);

                list.Add(new ItemStack(id, itemMeta)
                {
                    Present = true,
                    UnlocalizedName = item.UnlocalizedName
                });
            }

            return list;
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
