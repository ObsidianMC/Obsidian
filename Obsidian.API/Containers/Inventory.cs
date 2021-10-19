﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.API
{
    public sealed class Inventory : IContainer
    {
        internal byte Id { get; init; }

        internal int StateId { get; set; }

        public List<IPlayer> Viewers { get; private set; } = new List<IPlayer>();

        public Vector? BlockPosition { get; internal set; }

        public Guid Uuid { get; private set; } = Guid.NewGuid();

        public Guid Owner { get; set; }

        public ChatMessage Title { get; set; } = ChatMessage.Empty;

        public InventoryType Type { get; }

        public int Size { get; }

        public bool IsPlayerInventory => this.Id == 0;

        private ItemStack?[] items;

        public Inventory(InventoryType type)
        {
            this.Type = type;

            this.Size = type switch
            {
                InventoryType.Beacon or InventoryType.Lectern => 1,

                InventoryType.Grindstone or
                InventoryType.CartographyTable or
                InventoryType.Anvil or
                InventoryType.BlastFurnace or
                InventoryType.Smoker or
                InventoryType.Furnace or
                InventoryType.Merchant => 3,

                InventoryType.BrewingStand or InventoryType.Hopper => 5,

                InventoryType.Crafting => 2 * 5,

                InventoryType.Stonecutter or InventoryType.Enchantment => 2,

                InventoryType.Loom => 4,

                InventoryType.Generic or InventoryType.ShulkerBox => 9 * 3,

                _ => 0
            };

            this.items = new ItemStack[this.Size];
        }

        public Inventory(int size)
        {
            this.Type = InventoryType.Generic;

            if (size % 9 != 0)
                throw new InvalidOperationException("Size must be divisble by 9");
            if (size > 9 * 6)
                throw new InvalidOperationException($"Size must be <= {9 * 6}");

            this.Size = size;
            this.items = new ItemStack[size + 1];
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
            if (this.IsPlayerInventory)
            {
                for (int i = 36; i < 45; i++)
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

                for (int i = 9; i < 36; i++)
                {
                    var invItem = this.items[i];

                    if (invItem != null)
                    {
                        if (invItem.Count >= 64)
                            continue;

                        invItem.Count += item.Count;

                        return i;
                    }

                    this.items[i] = item;

                    return i;
                }
            }
            else
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
                        this.items[i] = item;

                    return i;
                }
            }

            return 9;
        }

        public void SetItem(int slot, ItemStack? item)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            this.items[slot] = item;
        }

        public ItemStack? GetItem(int slot) =>
            slot > this.Size - 1 || slot < 0 ? throw new IndexOutOfRangeException(nameof(slot)) : this.items[slot];

        public bool RemoveItem(int slot, short amount = 1)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.items[slot];

            if (item == null)
                return false;

            if (amount >= 64 || item.Count - amount <= 0)
                this.items[slot] = null;
            else
                item.Count -= amount;

            return true;
        }

        public bool TryRemoveItem(int slot, out ItemStack? removedItem)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.items[slot];

            if (item == null)
            {
                removedItem = null;
                return false;
            }

            removedItem = item;

            return this.RemoveItem(slot);
        }

        public bool TryRemoveItem(int slot, short amount, out ItemStack? removedItem)
        {
            if (slot > this.Size - 1 || slot < 0)
                throw new IndexOutOfRangeException($"{slot} > {this.Size - 1}");

            var item = this.items[slot];

            if (item == null)
            {
                removedItem = null;
                return false;
            }

            removedItem = item;

            return this.RemoveItem(slot, amount);
        }

        public bool HasItems() => this.items.Any(x => x is not null);
        public IEnumerator<ItemStack> GetEnumerator() => (this.items as IEnumerable<ItemStack>).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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
        Stonecutter,

        Custom
    }
}