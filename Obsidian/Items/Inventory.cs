using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Items
{
    public class Inventory
    {
        internal static byte LastId { get; set; }

        internal byte Id { get; set; }

        internal int ActionsNumber { get; set; }

        internal bool OwnedByPlayer { get; set; }

        public List<Player> Viewers { get; private set; } = new List<Player>();

        public Position BlockPosition { get; set; }

        public Guid Owner { get; set; }

        public InventoryType Type { get; set; }

        public ChatMessage Title { get; set; }

        public int Size { get; }

        public ItemStack[] Items { get; }

        public Inventory(int size)
        {
            this.Size = size;
            this.Items = new ItemStack[this.Size];
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
                    var invItem = this.Items[i];

                    if (invItem == null)
                        continue;

                    if (invItem.Type == item.Type)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.Items[i] = item;

                    return i;
                }

                for (int i = 9; i < 36; i++)
                {
                    var invItem = this.Items[i];

                    if (invItem != null)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.Items[i] = item;

                    return i;
                }
            }
            else
            {
                for (int i = 0; i < this.Size; i++)
                {
                    var invItem = this.Items[i];

                    if (invItem == null)
                        continue;

                    if (invItem.Type == item.Type)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.Items[i] = item;

                    return i;
                }
            }

            return 9;
        }

        public void SetItem(int slot, ItemStack item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.Items[slot] = item;
        }

        public ItemStack GetItem(int slot)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException(nameof(slot));

            return this.Items[slot] ?? ItemStack.Air;
        }

        public bool RemoveItem(int slot, short amount = 1)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.Items[slot];

            if (item == null)
                return false;

            if (amount >= 64 || item.Count - amount <= 0)
                this.Items[slot] = null;
            else
                item.Count -= amount;

            return true;
        }

        public bool HasItems() => this.Items.Any(x => x is not null);
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
